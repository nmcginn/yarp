# Roadmap

Derived from spec §11. Each phase ends in a state that is **deployable and demonstrable** — not a
half-integrated branch. Do not start a phase's work while the previous phase is not running in dev.

**Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done

Current phase: **Phase 2 — GitOps config** (code complete; first dev deployment pending)

Phases 1 and 2 were built together because nothing in Phase 1 was deployable without a cluster and
Phase 2 replaces Phase 1's hardcoded route anyway. Both remain undeployed until a dev cluster,
image registry, and ALB exist — those items are the open ones below.

---

## Phase 0 — Repository setup

Not in the spec; the groundwork the spec assumes exists.

- [x] Docs: spec vendored, roadmap, architecture summary, ADRs, open questions
- [x] `CLAUDE.md` with layout, invariants, and conventions
- [x] `.editorconfig`, `global.json`, `Directory.Build.props`
- [x] `CODEOWNERS`, PR templates (general + route request), route-request issue template
- [x] `config/` skeleton with environment files and a copy-paste route template
- [x] CI workflow skeleton (build, test, format, config validation — guarded until projects exist)
- [x] Solution and project skeleton (`Proxy.slnx`, central package management)

**Exit:** a contributor can clone, read `CLAUDE.md`, and know where their change goes.

---

## Phase 1 — Proxy skeleton

Spec §2, §3, §8.3, §10.

- [x] `Proxy.Host` with three Kestrel listeners (8080 data plane, 8081 status, 8082 ops) — as three
      hosts in one process, [ADR 0006](decisions/0006-one-process-three-hosts.md)
- [x] YARP wired up — routes come from config (Phase 2) rather than a hardcoded route
- [x] `/health/live` (no dependency checks) and `/health/ready` on 8082
- [x] Serilog JSON to stdout, with a correlation id on every request (spec §6.1 step 1)
- [x] Dockerfile; `publish-image.yml` pushes to GHCR on merge (runs on first merge to `master`)
- [x] Helm chart: 3 replicas, `maxUnavailable: 0`, PDB `minAvailable: 2`, ClusterIP service for 8081;
      `deploy/helm/verify-render.py` checks the rendered manifests in CI
- [ ] Deployed to dev and proxying real traffic to one upstream — needs a cluster, `DEPLOY_ENABLED`,
      and the secrets named in `deploy.yml`

**No auth in this phase.**

**Exit:** a request to the dev hostname reaches an upstream through the proxy; `/health/ready`
gates the rollout.

**Watch for:** port 8081 must not be in the ALB target group — verify the rendered manifests, not
just the chart values.

---

## Phase 2 — GitOps config

Spec §4, §10.

- [x] `Proxy.Config`: internal route/cluster model, YAML parsing (YamlDotNet), file merge
- [x] Validation rules (all of spec §4.4), each with a broken-fixture unit test:
  - [x] duplicate `routeId` / `clusterId` across files
  - [x] route referencing a nonexistent `clusterId`
  - [x] two routes matching the same host and path precedence
  - [x] **destination outside the internal allowlist** (security control)
  - [x] malformed host patterns, unparseable durations, unknown enum values
  - [x] `caching.enabled: true` on a non-anonymous route
- [x] Human-readable validator errors with file name and line number; every error, not just the first
- [x] Translation to YARP types, handed to `InMemoryConfigProvider`
- [x] `tools/ConfigValidator` CLI wrapping the same code (validates against every environment file)
- [~] Required CI check on every PR touching `config/` — `validate-config.yml` runs; making it
      *required* is a branch-protection setting on the repository
- [x] CI renders `config/routes/*.yaml` into a ConfigMap manifest (`stage-config.sh` + chart)
- [x] Helm `checksum/routes` (and `checksum/environment`) pod annotation triggering rolling restart
- [x] Startup validation is fatal; readiness fails on invalid config (integration-tested)
- [ ] **Demonstrate** that a bad ConfigMap stalls the rollout with old pods still serving — needs dev

**Exit:** a route lands in dev by merging a PR. **Demonstrate that a bad ConfigMap stalls the
rollout with old pods still serving.**

**Depends on:** nothing blocking. The destination allowlist values need a decision from the
platform/network owners — track in [open questions](open-questions.md). `.svc.cluster.local` is the
placeholder in `dev.yaml` and `prod.yaml`.

**Note:** until Phase 3, the host refuses to start if any route is `authenticated` — an
authenticated route must never be served anonymously. Only anonymous routes can be onboarded in dev
before then.

---

## Phase 3 — Authentication

Spec §5.

- [ ] Cookie authentication + `OpenIdConnect` handler, authorization code flow with PKCE
- [ ] Data Protection key ring persisted to S3 (or SSM) and protected with KMS; identical
      `SetApplicationName` across pods
- [ ] Redis-backed `ITicketStore`, key format `session:{sessionId}`, TTL matching ticket expiry
- [ ] Claims mapping to internal `UserContext` — IdP claim names confined to one file
- [ ] Per-route authorization policy from config (`authenticated` / `anonymous`, `requiredGroups`)
- [ ] Redis unavailable → sessions fail closed, anonymous routes keep working
- [ ] Integration tests: full login flow, session survives simulated pod restart, no session leak
      between users

**Exit:** demonstrate session survival across a rolling restart. That also validates the §4.3
assumption that config changes are cheap.

**Blocked on:** open questions 1 (domain topology) and 6 (session cutover strategy). Settle both
before starting.

---

## Phase 4 — Enrichment and audit

Spec §6.2, §8.1, §8.2.

- [x] Request transform with **unconditional identity-header stripping** before any conditional
      logic — pulled forward into Phase 2: the incumbent sets these same headers, so an upstream
      migrated before Phase 4 could otherwise be spoofed through an anonymous route
- [ ] Header writing behind an interface so signed-assertion mode can be added per route later
      (do **not** implement signed assertions)
- [~] Integration test: client sends every identity header on authenticated and anonymous routes;
      upstream never observes a client-supplied value — anonymous half exists; authenticated half
      needs Phase 3
- [ ] Audit events to CloudWatch Logs group `/proxy/audit` via `Serilog.Sinks.AwsCloudWatch`
- [ ] One log stream per pod, named by pod name
- [ ] Batched writes, explicitly bounded queue, `proxy_audit_events_dropped_total` metric + alarm
- [ ] Audit event types: `request.completed`, `auth.login`, `auth.logout`, `auth.denied`,
      `config.loaded` (with git SHA)
- [ ] Prometheus metrics on 8082: `proxy_config_git_sha`, `proxy_config_routes_loaded`,
      `proxy_auth_decisions_total{decision}`, `proxy_session_store_errors_total`,
      `proxy_audit_events_dropped_total`
- [ ] Terraform: log group with explicit retention

**Exit:** audit stream visible in CloudWatch with per-pod streams; header injection test green.

**Blocked on:** open question 2 (compliance log consumers) — highest schedule risk in the project.
Finalize the audit schema only after the downstream consumers are inventoried. Question 4
(upstream reachability) determines whether signed assertions become required later.

---

## Phase 5 — Status UI

Spec §7.

- [ ] `GET /api/status/routes` — live route table **from this pod's in-memory state**, never
      re-read from disk
- [ ] `GET /api/status/config` — git SHA, build time, config file checksum
- [ ] `GET /api/status/clusters` — active health check state per destination
- [ ] Razor Pages: searchable route list (host, cluster, auth policy, owner, cache state), loaded
      config version, cluster health
- [ ] Each route links back to its source file in the repo
- [ ] Auth via the same IdP; membership in a configured admin group required

Small phase. Read-only, server-rendered, no JavaScript build toolchain.

---

## Phase 6 — Hardening

Spec §6.4, §9, §12.7.

- [ ] Load testing (k6 or NBomber): sustained peak throughput, p50/p95/p99, 30+ minute memory
      stability, slow/dead upstream behavior, audit sink at peak request rate
- [ ] `SocketsHttpHandler.PooledConnectionIdleTimeout` set below the shortest upstream keepalive
      (default 30s, configurable)
- [ ] Server GC enabled; `ThreadPool` minimums raised
- [ ] Output cache with Redis store, anonymous routes only
- [ ] Runbook completed ([`runbook.md`](runbook.md)) and failure modes documented
- [ ] CODEOWNERS refined for `config/routes/`; contribution guide finalized with review SLA
- [ ] Traffic capture and differential testing against the incumbent (open question 5)

**Exit:** cutover-ready.
