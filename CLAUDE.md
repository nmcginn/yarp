# CLAUDE.md

Guidance for anyone — human or agent — working in this repository.

## What this is

An identity-aware reverse proxy: a single ASP.NET Core deployable built on
[YARP](https://microsoft.github.io/reverse-proxy/) that handles OIDC authentication, session
management, identity header enrichment, routing, caching, and compliance logging for internal
web applications. It replaces an unmaintained in-house proxy.

Target runtime is .NET 10 / ASP.NET Core, deployed to AWS EKS behind an ALB.

The authoritative specification is [`docs/spec.md`](docs/spec.md). When this file and the spec
disagree, the spec wins — and the disagreement is a bug in this file, so fix it.

## Design priorities, in order

1. **Comprehensibility.** A new engineer should understand the whole system in a day. Boring and
   explicit beats clever. This outranks performance and elegance.
2. **Operational predictability.** Failure modes are documented. Degraded-but-serving beats
   correct-but-down.
3. **Minimal custom surface.** Every line of custom code is a line someone else inherits. Use
   framework capabilities before writing your own.
4. **Horizontal scalability.** Fully stateless pods. No sticky sessions, ever.

## Repository layout

```
src/
  Proxy.Host/            Composition root: Kestrel listeners, DI wiring, Program.cs
  Proxy.Core/            YARP pipeline, transforms, config translation
  Proxy.Auth/            OIDC handler config, Redis ticket store, claims mapping
  Proxy.Config/          Route schema, YAML parsing, validation
  Proxy.Status/          Read-only status endpoints + Razor Pages
  Proxy.Observability/   Structured logging, audit events, metrics
tools/
  ConfigValidator/       CLI wrapper over Proxy.Config, run by CI
config/
  routes/                One YAML file per application. THIS IS THE ROUTE CONFIG.
  environments/          Environment-specific overrides
tests/
  Proxy.Core.Tests/
  Proxy.Config.Tests/
  Proxy.Integration.Tests/
deploy/
  helm/
  terraform/
docs/                    Spec, roadmap, ADRs, runbook, contribution guides
```

**Project reference rules:**
- Only `Proxy.Host` references everything.
- `Proxy.Core` must **not** reference `Proxy.Status`.
- `tools/ConfigValidator` wraps `Proxy.Config` — CI must run the exact same validation code the
  proxy runs at startup. Never reimplement validation in a script.

## Ports

| Port | Surface | Exposure |
|------|---------|----------|
| 8080 | Proxy data plane | Public, via ALB |
| 8081 | Read-only status UI | Internal ClusterIP Service only — **never** on the public ALB target group |
| 8082 | Health, readiness, Prometheus metrics | Cluster-internal |

## Non-negotiable invariants

These are security and correctness controls, not style choices. Do not refactor them away for
elegance. If you touch code near one, leave a comment referencing the spec section. Full list and
rationale: [`docs/security-controls.md`](docs/security-controls.md).

1. **Identity headers are stripped unconditionally** on every proxied request — authenticated and
   anonymous alike — before any conditional logic, with no early return above the strip. An
   upstream that trusts `X-Auth-User-Id` plus a client that can set it is an authentication bypass.
   (spec §6.2)
2. **Destination addresses are allowlisted** to internal DNS suffixes and CIDRs, enforced both in
   CI and at startup. Without it, a merged route file is an SSRF and exfiltration primitive.
   (spec §4.4)
3. **Caching is permitted only on `authorizationPolicy: anonymous` routes** in v1, enforced by the
   validator. Cross-user cache leakage is the worst bug this system can produce. (spec §6.3)
4. **The Data Protection key ring is shared and persistent**, with an identical
   `SetApplicationName` across all pods. (spec §5.2.1)
5. **Session state lives server-side** behind `ITicketStore`; the cookie carries only an opaque
   session id. (spec §5.2.2)
6. **Invalid config is fatal at startup** and fails readiness. A bad ConfigMap must stall the
   rollout with old pods still serving. (spec §4.4)
7. **`/health/live` never checks dependencies.** A Redis blip must not become a pod restart storm.
   (spec §8.3)
8. **Port 8081 is never attached to the public ALB.** (spec §2)

## Configuration is GitOps

Route configuration lives in `config/routes/` as YAML, one file per application. A pull request is
the request mechanism; git history is the version history; `git revert` is the rollback; the PR is
the audit record.

There is **no config database, no snapshot table, no activation endpoint, no diff API, no write
UI**, and this is deliberate. Do not reintroduce one to solve a propagation-latency or history
problem.

A config change triggers a **rolling restart, not a hot reload** — see
[ADR 0002](docs/decisions/0002-rolling-restart-over-hot-reload.md). Do not add a file watcher or
`reloadOnChange: true` against the ConfigMap mount.

Schema reference and validation rules: [`docs/configuration.md`](docs/configuration.md).
App-team-facing guide: [`docs/contributing-routes.md`](docs/contributing-routes.md).

## Where we are

See [`docs/roadmap.md`](docs/roadmap.md) for the phase plan and current status. Each phase ends in
a state that is deployable and demonstrable; do not start the next phase's work while the current
one is not deployed.

Several decisions are still open and some of them block specific phases — see
[`docs/open-questions.md`](docs/open-questions.md) before starting Phase 3 or Phase 4.

## Conventions

- **Language/style:** C# with nullable reference types and `TreatWarningsAsErrors` enabled
  (`Directory.Build.props`). Formatting is `.editorconfig` + `dotnet format`.
- **Tests:** xUnit. Every validation rule in spec §4.4 gets a unit test with a deliberately broken
  fixture. Integration tests use `WebApplicationFactory` + Testcontainers for Redis. See
  [`docs/testing.md`](docs/testing.md).
- **Commits:** imperative subject, reference the spec section when implementing one
  (`Proxy.Config: reject destinations outside the allowlist (spec §4.4)`).
- **PRs:** where the spec is ambiguous, choose the more boring option and note the decision in the
  PR description. If the decision has consequences beyond the PR, write an ADR instead.
- **ADRs:** short markdown files in `docs/decisions/`, numbered sequentially. Record the decision
  and, importantly, what it lets us *not* build.

## Common commands

The .NET SDK is not yet vendored into CI containers or this repo's tooling; these are the commands
as they will exist from Phase 1 onward.

```bash
dotnet build                                     # build everything
dotnet test                                      # all tests
dotnet test tests/Proxy.Config.Tests             # one project
dotnet format --verify-no-changes                # style gate (as CI runs it)
dotnet run --project tools/ConfigValidator -- config/routes   # validate route config
dotnet run --project src/Proxy.Host               # run locally
```

## Things not to build

Explicitly out of scope. Adding any of these is a regression, not a feature.

- Service mesh, east-west traffic, L4 proxying.
- Kubernetes ingress controller behavior — an ALB terminates public traffic.
- Multi-region active-active.
- API gateway features: rate limit quotas, API key management, developer portal.
- A write-capable admin UI.
- A pluggable audit sink abstraction. The migration path to Kinesis is a CloudWatch subscription
  filter and requires no application change. (spec §8.1)
- A polling config reloader (spec §4.3) or the signed-assertion header mode (spec §6.2) — these are
  the two anticipated extension points. Structure code so they *can* be added; do not build them
  in v1.
- A custom `IProxyConfigProvider`. YARP's `InMemoryConfigProvider` is sufficient.
