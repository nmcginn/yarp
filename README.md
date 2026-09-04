# Identity-aware reverse proxy

A single ASP.NET Core deployable built on [YARP](https://microsoft.github.io/reverse-proxy/) that
handles OIDC authentication, session management, identity header enrichment, routing, caching, and
compliance logging for internal web applications. It replaces an unmaintained in-house proxy.

.NET 10 / ASP.NET Core, deployed to AWS EKS behind an ALB.

## Status

**Phase 2 — GitOps config, code complete; not yet deployed.** The proxy runs, routes come from
`config/routes/`, the validator gates PRs, and the Helm chart renders. Authentication (Phase 3) is
next. See [`docs/roadmap.md`](docs/roadmap.md).

## I want to…

| | |
|---|---|
| **Add or change a route for my app** | [`docs/contributing-routes.md`](docs/contributing-routes.md) |
| **Understand how this works** | [`docs/architecture.md`](docs/architecture.md), then [`docs/spec.md`](docs/spec.md) |
| **Work on the proxy itself** | [`CLAUDE.md`](CLAUDE.md) and [`docs/roadmap.md`](docs/roadmap.md) |
| **Operate it** | [`docs/runbook.md`](docs/runbook.md) |
| **Know what must not be refactored away** | [`docs/security-controls.md`](docs/security-controls.md) |

Full documentation index: [`docs/README.md`](docs/README.md).

## Shape of the system

Three listeners in one container: `:8080` proxy data plane (public via ALB), `:8081` read-only
status UI (internal only, never on the public ALB), `:8082` health and Prometheus metrics.

Route configuration is GitOps: `config/routes/*.yaml`, one file per application, validated in CI,
delivered as a ConfigMap, applied by a rolling restart. There is no config database and no admin UI
— [that is deliberate](docs/decisions/0001-gitops-route-configuration.md).

Redis is the only stateful runtime dependency.

## Design priorities, in order

1. **Comprehensibility** — a new engineer should understand the whole system in a day.
2. **Operational predictability** — degraded-but-serving beats correct-but-down.
3. **Minimal custom surface** — every line of custom code is a line someone else inherits.
4. **Horizontal scalability** — fully stateless pods, no sticky sessions.

## Layout

```
src/     Proxy.Host, Proxy.Core, Proxy.Auth, Proxy.Config, Proxy.Status, Proxy.Observability
tools/   ConfigValidator — the CI check, wrapping the same validation the proxy runs at startup
config/  routes/ (the route config) and environments/
tests/   unit and integration tests
deploy/  helm/ and terraform/
docs/    spec, roadmap, ADRs, runbook, guides
```

## Building

Requires the .NET 10 SDK (see [`global.json`](global.json)) and Docker for integration tests.

```bash
dotnet build
dotnet test
dotnet run --project tools/ConfigValidator          # validate config/routes against every environment
dotnet run --project src/Proxy.Host                 # run locally against config/environments/local.yaml
```

## License

MIT — see [`LICENSE`](LICENSE).
