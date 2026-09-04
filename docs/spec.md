# Identity-Aware Reverse Proxy — Project Specification

**Status:** Draft for implementation (rev 2 — GitOps config, CloudWatch logging)
**Target runtime:** .NET 10 / ASP.NET Core, deployed to AWS EKS
**Audience:** Implementing engineer or coding agent

---

## 1. Purpose

Build a replacement for an existing internally-developed reverse proxy that has become unstable and is no longer maintained by anyone with working knowledge of it. The replacement is a single ASP.NET Core deployable built on YARP, handling OAuth/OIDC authentication, session management, identity header enrichment, routing, caching, and compliance logging for internal web applications.

### Design priorities, in order

1. **Comprehensibility.** A new engineer should understand the whole system in a day. Prefer boring, explicit solutions over clever ones. This constraint outranks performance and elegance.
2. **Operational predictability.** Failure modes must be understood and documented. Degraded-but-serving beats correct-but-down.
3. **Minimal custom surface.** Every line of custom code is a line someone else inherits. Use framework capabilities before writing your own.
4. **Horizontal scalability.** Fully stateless pods. No sticky sessions, ever.

### Non-goals

- Service mesh, east-west traffic, or L4 proxying.
- Kubernetes ingress controller behavior. An ALB terminates public traffic and forwards to this service.
- Multi-region active-active. Single region to start.
- API gateway features: rate limiting quotas, API key management, developer portal.
- A write-capable admin UI. Route configuration is managed through Git (§4).

---

## 2. Architecture Overview

One container image, one Deployment, three Kestrel listeners:

| Port | Surface | Exposure |
|------|---------|----------|
| 8080 | Proxy data plane | Public, via ALB |
| 8081 | Read-only status UI | Internal ClusterIP Service only |
| 8082 | Health, readiness, Prometheus metrics | Cluster-internal |

Port 8081 must never be attached to the public ALB target group. It is protected by network topology first, authentication second.

### External dependencies

| Dependency | Purpose | Failure behavior |
|------------|---------|------------------|
| Redis (ElastiCache) | Session ticket store, output cache | Sessions fail closed; anonymous routes unaffected; cache bypassed |
| S3 + KMS | Data Protection key ring persistence | Startup failure; do not start without it |
| Corporate IdP (OIDC) | Authentication | Existing sessions unaffected; new logins fail |
| CloudWatch Logs | Compliance audit sink | Bounded local buffer, alarm on drops |

Note what is absent: no config database. Route configuration is compiled from files in the repository. Redis is the only stateful runtime dependency.

---

## 3. Solution Layout

```
src/
  Proxy.Host/            # Composition root. Kestrel config, DI wiring, Program.cs
  Proxy.Core/            # YARP pipeline, transforms, config loading
  Proxy.Auth/            # OIDC handler config, ticket store, claims mapping
  Proxy.Config/          # Route schema, parsing, validation, translation to YARP
  Proxy.Status/          # Read-only status endpoints + Razor pages
  Proxy.Observability/   # Structured logging, audit events, metrics
tools/
  ConfigValidator/       # CLI wrapper over Proxy.Config, used by CI
config/
  routes/                # One YAML file per application. THIS IS THE CONFIG.
    hr-portal.yaml
    finance-reporting.yaml
  environments/
    dev.yaml             # environment-specific overrides
    prod.yaml
tests/
  Proxy.Core.Tests/
  Proxy.Config.Tests/
  Proxy.Integration.Tests/
deploy/
  helm/
  terraform/
```

Only `Proxy.Host` references everything. `Proxy.Core` must not reference `Proxy.Status`.

`tools/ConfigValidator` exists so CI runs the exact same validation code the proxy runs at startup. Do not reimplement validation in a script.

---

## 4. Configuration: GitOps

### 4.1 Model

Route configuration lives in `config/routes/` as YAML, one file per application. A pull request is the request mechanism for a new or changed route. Git history is the version history, `git revert` is the rollback, and the PR is the audit record.

Consequences that replace machinery from the previous revision:

- No config database, no snapshot table, no activation endpoint, no diff API, no write UI.
- CODEOWNERS on `config/routes/` enforces review by the platform team.
- CI validation gates merge, so invalid config never reaches the cluster.

Use YAML rather than JSON. Comments and lower punctuation noise matter when the file *is* the review artifact. Add `NetEscapades.Configuration.Yaml` or parse with YamlDotNet directly.

One file per application is deliberate: it minimizes merge conflicts when several teams change routes in the same week, and it leaves the door open for per-app CODEOWNERS entries later.

### 4.2 Route file schema

```yaml
# config/routes/hr-portal.yaml
application: hr-portal
owner: hr-platform-team          # informational; surfaced in status UI and audit
routes:
  - routeId: hr-portal
    match:
      hosts: [hr.corp.example.com]
      path: "{**catch-all}"
    clusterId: hr-portal-svc
    authorizationPolicy: authenticated   # authenticated | anonymous
    requiredGroups: [hr-users, hr-admins]   # empty = any authenticated user
    identityHeaders: [userId, email, groups, displayName]
    caching:
      enabled: false
    auditLevel: standard                 # standard | detailed

clusters:
  - clusterId: hr-portal-svc
    destinations:
      primary:
        address: http://hr-portal.hr.svc.cluster.local:8080/
    healthCheck:
      active:
        enabled: true
        path: /health
        interval: 00:00:10
```

This constrained domain model is a safety boundary. Do not accept raw YARP `RouteConfig` in these files. Parse into internal types, validate, then translate into YARP configuration.

### 4.3 Loading

At startup the app reads every file in `config/routes/`, merges them into a single model, validates, translates to YARP types, and hands them to YARP's built-in `InMemoryConfigProvider`. No custom `IProxyConfigProvider` is required.

Configuration files are delivered to pods as a **ConfigMap generated by CI from the repo** and mounted at `/app/config/routes`.

**A config change triggers a rolling restart, not a hot reload.** Set a checksum annotation on the pod template in Helm:

```yaml
annotations:
  checksum/routes: {{ include (print $.Template.BasePath "/routes-configmap.yaml") . | sha256sum | quote }}
```

Rationale, and this is a deliberate choice worth preserving:

- Config changes and code deploys then use one mechanism, so there is one thing to understand and one thing to debug.
- It sidesteps a real .NET problem: ConfigMap volume mounts are updated by swapping a `..data` symlink, and `FileSystemWatcher` does not fire reliably on that. `reloadOnChange: true` against a ConfigMap mount is a known source of silent staleness.
- The restart is nearly free. Given the shared Data Protection key ring (§5.2.1) and Redis ticket store (§5.2.2), rolling restarts are already session-preserving. That cost is paid in Phase 3 regardless.

If propagation latency of 1–2 minutes later proves annoying, the escape hatch is a polling reloader that hashes the merged file set every N seconds and calls `InMemoryConfigProvider.Update`. Structure the loading code so this can be added without touching the pipeline, but **do not build it in v1.**

### 4.4 Validation

`tools/ConfigValidator` runs as a required CI check on every PR touching `config/`. The same validation runs at application startup.

Rules:

- Duplicate `routeId` or `clusterId` across all files.
- Route referencing a nonexistent `clusterId`.
- Two routes matching the same host and path precedence.
- Destination addresses outside an allowlist of internal DNS suffixes and CIDRs. **This is a security control.** Without it, a merged route file is an SSRF and data-exfiltration primitive. Enforce in CI *and* at startup; code review is not sufficient on its own.
- Malformed host patterns, unparseable durations, unknown enum values.
- `caching.enabled: true` on a route whose `authorizationPolicy` is not `anonymous` (§6.3).

Validation failure at startup is fatal. The pod must not start with invalid config, and readiness must fail. Because config arrives via rolling update, a bad ConfigMap will stall the rollout with old pods still serving, which is the correct outcome.

The validator should emit human-readable errors with file name and line number. It is the primary feedback surface for app teams, and its output quality is what makes PR-based self-service tolerable.

---

## 5. Authentication and Session

### 5.1 Stack

ASP.NET Core cookie authentication plus the `OpenIdConnect` handler. Authorization code flow with PKCE. Do not implement OIDC by hand.

### 5.2 Non-negotiable requirements

These three will each cause intermittent, hard-to-diagnose production failures if omitted. Do not "simplify" them away.

**5.2.1 Data Protection key ring must be shared and persistent.**

```csharp
services.AddDataProtection()
    .PersistKeysToAWSSystemsManager("/proxy/dataprotection/")  // or S3
    .ProtectKeysWithAwsKms(kmsKeyArn)
    .SetApplicationName("corp-identity-proxy");
```

`SetApplicationName` must be identical across all pods. Without persistence, every pod restart invalidates all sessions — which, given §4.3, would mean every route change logs out every user. Without sharing, requests bouncing between pods hit undecryptable cookies intermittently.

**5.2.2 Session state lives server-side via `ITicketStore`.**

Implement `ITicketStore` backed by Redis. The cookie carries only an opaque session id. Enriched claims will exceed the 4KB cookie limit and trigger chunking, which breaks awkwardly with some upstreams. Server-side sessions also give forced logout and revocation, which compliance will ask for.

Key format: `session:{sessionId}`. TTL matches ticket expiry. Sliding expiration updates the TTL, not the cookie.

**5.2.3 Cookie scope must match the domain topology.**

If all applications sit under one parent domain, issue the cookie on the parent (`.corp.example.com`) and SSO works with one handshake. If applications live on separate apex domains, a central auth domain plus per-domain token exchange on first visit is required, which is materially more work.

> **Open question — must be answered before implementing this.** Confirm whether all in-scope applications share a parent domain. Assume the shared-parent case for v1 and structure the code so the exchange flow can be added without rework.

### 5.3 Claims mapping

Map IdP claims to a stable internal `UserContext`. Do not let IdP-specific claim names leak into transforms or audit events. If the IdP is replaced, exactly one file should change.

```csharp
public sealed record UserContext(
    string UserId,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Groups);
```

---

## 6. Proxy Pipeline

### 6.1 Middleware order (port 8080)

1. Correlation id assignment (accept inbound `X-Request-Id` or generate)
2. Structured request logging scope
3. Exception handling
4. Authentication
5. Authorization (per-route policy from config)
6. Output cache (only where the route enables it)
7. YARP `MapReverseProxy`

### 6.2 Identity header enrichment

```csharp
proxyPipeline.AddRequestTransform(ctx =>
{
    // Strip unconditionally, before anything else, on every request.
    foreach (var h in IdentityHeaders.All)
        ctx.ProxyRequest.Headers.Remove(h);

    if (ctx.HttpContext.User.Identity?.IsAuthenticated != true)
        return default;

    var user = ctx.HttpContext.GetUserContext();
    ctx.ProxyRequest.Headers.Add("X-Auth-User-Id", user.UserId);
    ctx.ProxyRequest.Headers.Add("X-Auth-Email", user.Email);
    ctx.ProxyRequest.Headers.Add("X-Auth-Groups", string.Join(',', user.Groups));
    return default;
});
```

**The unconditional strip is a security control.** If an upstream trusts `X-Auth-User-Id` and a client can set it, this is an authentication bypass. Strip on every request including anonymous ones, before any conditional logic, with no early return above it. It will look redundant. It is not.

Write an integration test that sends every identity header from the client on both authenticated and anonymous routes and asserts the upstream never observes a client-supplied value. This test is the point of the section.

**Signed assertion mode (design for it, defer implementation):** if any upstream is reachable on the network without traversing this proxy, plain headers are insufficient and a short-lived signed JWT is required so the app can verify provenance. Keep header writing behind an interface so this can be enabled per route later.

### 6.3 Caching

ASP.NET Core `OutputCache` with a Redis store. Default to **disabled** on every route; opt in per route.

For v1, caching is permitted **only** on routes with `authorizationPolicy: anonymous`, enforced by the validator (§4.4). Caching authenticated responses is a vary-by-identity problem, and cross-user cache leakage is the worst bug this system can produce.

> **Open question.** Determine what the incumbent actually caches. Expect most of it to be static assets (which belong in CloudFront) or token/JWKS caching (in-process and trivial). Measure before building anything more sophisticated.

### 6.4 Upstream connection tuning

Set `SocketsHttpHandler.PooledConnectionIdleTimeout` **below** the shortest upstream keepalive timeout. A mismatch produces sporadic 502s that present as network flakiness and are expensive to diagnose. Default 30 seconds, configurable.

Enable Server GC. Raise `ThreadPool` minimums above default; the default ramp shows up as latency spikes under burst on connection-heavy workloads.

---

## 7. Status UI (port 8081)

Read-only. Server-rendered Razor Pages, no JavaScript build toolchain.

| Endpoint | Purpose |
|----------|---------|
| `GET /api/status/routes` | Live route table **as loaded in this pod** |
| `GET /api/status/config` | Git SHA, build time, file checksum of loaded config |
| `GET /api/status/clusters` | Current active health check state per destination |

`/api/status/routes` must reflect this pod's in-memory state. It is the primary tool for diagnosing "did my route actually land," so it must not be implemented by re-reading files from disk.

Screens: route list (searchable, showing host, cluster, auth policy, owner, cache state); loaded config version; cluster health. Access requires authentication via the same IdP and membership in a configured admin group.

Link each route back to its source file in the repo. When someone asks why their route is not working, the first useful answer is usually which file and which commit is live.

---

## 8. Observability

### 8.1 Two separate log streams

**Application logs** → stdout → cluster log pipeline → `/aws/eks/...`. Serilog, JSON formatted. Operational concern, short retention.

**Compliance audit events** → CloudWatch Logs, dedicated log group `/proxy/audit`, written directly via `Serilog.Sinks.AwsCloudWatch`. Separate group so retention and IAM read access are controlled independently of application logs. Never interleaved.

Implementation notes:

- **One log stream per pod.** Use the pod name as the stream name. CloudWatch throttles `PutLogEvents` per stream, and a shared stream across replicas will throttle under load and drop events.
- Batch writes. Bound the in-memory queue explicitly and emit a `proxy_audit_events_dropped_total` metric when the queue is full. Alarm on any nonzero value. Silent loss of compliance logs is worse than the outage that caused it.
- Set the log group retention in Terraform, not by hand, and set it to whatever compliance requires rather than a default.

**Migration path to Kinesis:** when volume justifies it, attach a CloudWatch Logs subscription filter on `/proxy/audit` routing to Firehose → S3. This requires **no application change**, which is why direct-to-CloudWatch is the right v1 choice rather than a compromise. Do not build a pluggable sink abstraction for this.

Audit event shape:

```jsonc
{
  "timestamp": "2026-09-03T14:22:01.442Z",
  "eventType": "request.completed",
  "correlationId": "01J...",
  "userId": "...",
  "sourceIp": "...",
  "host": "hr.corp.example.com",
  "method": "GET",
  "path": "/reports/q3",
  "routeId": "hr-portal",
  "statusCode": 200,
  "durationMs": 142,
  "authDecision": "allow"
}
```

Also audit: `auth.login`, `auth.logout`, `auth.denied`, `config.loaded` (with git SHA, on startup).

Configuration change auditing is now Git history and does not belong in this stream.

> **Open question — highest schedule risk in the project.** The incumbent's log format has downstream consumers (SIEM rules, compliance reports, dashboards) that nobody has inventoried. Identify them before finalizing this schema. Discovering a required field after cutover is far more expensive than finding it now. Note also that CloudWatch Logs Insights query syntax differs from whatever those consumers use today, which may itself be a migration item.

### 8.2 Metrics

Prometheus on 8082. Beyond the standard ASP.NET Core and YARP meters:

- `proxy_config_git_sha` (info gauge) — divergence across pods indicates a stalled rollout
- `proxy_config_routes_loaded` (gauge)
- `proxy_auth_decisions_total{decision}`
- `proxy_session_store_errors_total`
- `proxy_audit_events_dropped_total`

### 8.3 Health endpoints

- `/health/live` — process is up. No dependency checks. Never fails on a dependency outage, or Kubernetes will restart pods during a Redis blip and turn a degradation into an outage.
- `/health/ready` — config parsed and loaded successfully. Fails on invalid config, which correctly stalls a rollout carrying a bad ConfigMap.

---

## 9. Testing Requirements

**Unit:** validation rules (each rule in §4.4 gets a test with a deliberately broken fixture), claims mapping, YAML parsing, translation to YARP types.

**Integration** (`WebApplicationFactory` + Testcontainers for Redis):

- Full OIDC login flow against a test IdP (Keycloak container or stub).
- Session survives simulated pod restart when the key ring is shared.
- Session does *not* leak between distinct users.
- **Identity header injection is stripped** — every header, authenticated and anonymous routes, verified at a stub upstream.
- Invalid config causes startup failure and readiness failure, not a partially-loaded route table.
- Redis unavailable → sessions fail closed, anonymous routes keep working.
- Destination allowlist rejects an external address, both in the validator CLI and at startup.

**Load** (k6 or NBomber, before cutover): sustained throughput at expected peak, p50/p95/p99, memory stability over 30+ minutes, behavior when an upstream is slow or dead, and audit sink behavior at peak request rate.

---

## 10. Deployment

Helm chart. Minimum 3 replicas, PodDisruptionBudget `minAvailable: 2`, HPA on CPU and active request count. Rolling updates with `maxUnavailable: 0`.

CI pipeline on merge to main:

1. Build and test.
2. Run `ConfigValidator` against `config/routes/`.
3. Render `config/routes/*.yaml` into a ConfigMap manifest.
4. Build and push image.
5. Helm upgrade. The checksum annotation (§4.3) means a config-only change still produces a rolling restart.

Sessions survive pod replacement given the shared key ring and Redis ticket store, so no stickiness or connection draining complexity is required.

Terraform for ElastiCache, the S3 key ring bucket, KMS key, the `/proxy/audit` CloudWatch log group with explicit retention, and IAM roles for service accounts. No RDS, no Firehose.

---

## 11. Implementation Phases

Each phase ends in a state that is deployable and demonstrable.

**Phase 1 — Proxy skeleton.** `Proxy.Host` with three listeners. YARP with a single hardcoded route. Health endpoints. Container image, Helm chart, deployed to dev and proxying successfully. No auth.

**Phase 2 — GitOps config.** YAML schema, parser, validator, `ConfigValidator` CLI, CI check, ConfigMap generation, checksum-triggered rollout. Routes now come from the repo. Prove that a bad config stalls the rollout with old pods still serving.

**Phase 3 — Authentication.** OIDC, cookie auth, Data Protection key ring, Redis `ITicketStore`, claims mapping, per-route authorization. Prove session survival across a rolling restart — this also validates the §4.3 assumption that config changes are cheap.

**Phase 4 — Enrichment and audit.** Request transforms with unconditional stripping. Audit stream to CloudWatch with per-pod streams and bounded buffering. Metrics.

**Phase 5 — Status UI.** Read-only status endpoints and Razor pages. Small phase now.

**Phase 6 — Hardening.** Load testing, connection tuning, output cache for anonymous routes, runbook, failure-mode documentation, CODEOWNERS and contribution guide for `config/routes/`.

---

## 12. Open Questions

1. **Domain topology** (§5.2.3) — do all applications share a parent domain? Determines whether token exchange is needed.
2. **Compliance log consumers** (§8.1) — who reads the incumbent's logs, and what fields do their rules depend on? Highest schedule risk.
3. **Caching semantics** (§6.3) — what does the incumbent actually cache?
4. **Upstream network reachability** (§6.2) — are any upstreams reachable without traversing the proxy? Determines whether signed assertions are required.
5. **Undocumented incumbent behavior** — plan traffic capture and differential testing against the existing proxy. Per-app quirks and header dependencies nobody wrote down should be expected to dominate the schedule.
6. **Session cutover strategy** — dual-run with shared session state, or a flag-day logout? Political as much as technical. Settle before Phase 3.
7. **Route request workflow** — new with the GitOps model. Do app teams have repo access and enough Git familiarity to open a PR? What is the review SLA, and who is on call for it? PR-based self-service is only self-service if review is fast; otherwise the platform team becomes the bottleneck the old system's owners were. Write a short contribution guide with a copy-paste template file, and consider a PR template that prompts for the destination, required groups, and owning team.

---

## 13. Guidance for the Implementer

- Where a requirement is marked as a security control or non-negotiable, do not refactor it away for elegance. Add a comment referencing the section number instead.
- Prefer framework features over custom implementations. If you find yourself writing an OIDC flow, a cache eviction policy, or a connection pool, stop and check whether ASP.NET Core already provides it.
- Do not add abstraction layers for hypothetical future requirements. The two anticipated extension points are §6.2's signed-assertion interface and §4.3's reload hook; neither should be built in v1.
- The absence of a config database is intentional. Do not reintroduce one to solve a propagation-latency or history problem — Git and the rolling restart handle both.
- Where this spec is ambiguous, choose the more boring option and note the decision in the PR description.
