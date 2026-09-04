# Testing strategy

From spec §9. The integration tests are not a formality — several of them are the enforcement
mechanism for security controls that are otherwise easy to refactor away.

## Unit tests

`tests/Proxy.Config.Tests`, `tests/Proxy.Core.Tests`. xUnit.

- **Every validation rule in spec §4.4 gets a test with a deliberately broken fixture.** One rule,
  one fixture file, one assertion on the error message. Fixtures live beside the tests so a
  reviewer can see what "broken" means.
- Claims mapping — IdP claim shapes in, `UserContext` out.
- YAML parsing — the schema, including the fields the schema deliberately does *not* accept.
- Translation to YARP types.

Error message quality is part of the contract: the validator is the primary feedback surface for
application teams, so assert on the message, file name, and line number, not just that an exception
was thrown.

## Integration tests

`tests/Proxy.Integration.Tests`. The tests start the real composition root (`ProxyProcess`) on
loopback with random ports and talk to it over HTTP, against a stub upstream that records what it
receives. `WebApplicationFactory` assumes a single host, and the proxy deliberately runs three
([ADR 0006](decisions/0006-one-process-three-hosts.md)); running the real thing also means the port
topology of spec §2 is under test. Redis arrives with Testcontainers in Phase 3.

| Test | What it protects |
|------|------------------|
| Full OIDC login flow against a test IdP (Keycloak container or stub) | The auth stack works end to end |
| Session survives a simulated pod restart when the key ring is shared | Spec §5.2.1 — and the §4.3 assumption that config changes are cheap |
| Session does not leak between distinct users | The obvious catastrophe |
| **Identity header injection is stripped** — every header, authenticated and anonymous routes, verified at a stub upstream | Spec §6.2. **This test is the point of that section.** Anonymous half exists; authenticated half lands with Phase 3. |
| Proxy routes answer only on 8080; health only on 8082 | Spec §2, security control 7 |
| Invalid config causes startup failure and readiness failure, not a partially-loaded route table | Spec §4.4 |
| Redis unavailable → sessions fail closed, anonymous routes keep working | The documented degradation mode |
| Destination allowlist rejects an external address, both in the validator CLI and at startup | Spec §4.4 — SSRF control |

Each of these maps to a control in [`security-controls.md`](security-controls.md). If you find
yourself deleting one, you are removing a control — say so explicitly in the PR.

## Load tests

k6 or NBomber, run before cutover, not continuously:

- Sustained throughput at expected peak
- p50 / p95 / p99 latency
- Memory stability over 30+ minutes
- Behavior when an upstream is slow, and when it is dead
- **Audit sink behavior at peak request rate** — this is where `proxy_audit_events_dropped_total`
  earns its existence

## Differential testing against the incumbent

Planned for Phase 6, but capture should start early — see
[open question 5](open-questions.md). Per-app quirks and header dependencies nobody wrote down
should be expected to dominate the cutover schedule. Traffic capture is cheap to collect now and
expensive to reconstruct later.

## Running tests

```bash
dotnet test                                # everything
dotnet test tests/Proxy.Config.Tests       # one project
dotnet test --filter FullyQualifiedName~HeaderStripping
```

Integration tests need Docker available for Testcontainers.
