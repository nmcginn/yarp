# Runbook

**Status: skeleton.** Filled in as each phase lands; completed in Phase 6. Every entry needs a real
verification step — an untested runbook is worse than none, because it is trusted at 3am.

## Quick reference

| Question | Where to look |
|----------|---------------|
| Did my route actually deploy? | Status UI `/api/status/routes` and `/api/status/config` (git SHA + checksum) per pod |
| Is the rollout stuck? | `proxy_config_git_sha` diverging across pods; pods failing readiness |
| Is the proxy up? | `/health/live` on 8082 |
| Is this pod serving? | `/health/ready` on 8082 |
| Are we losing audit events? | `proxy_audit_events_dropped_total` — should always be 0 |

## Failure modes

### Redis unavailable

**Symptoms:** authenticated requests fail; `proxy_session_store_errors_total` climbing; anonymous
routes still serving.

**Expected behavior:** sessions fail closed. Anonymous routes are unaffected. Output cache is
bypassed. **Pods must not restart** — `/health/live` does not check dependencies, by design.

**Action:** _TBD (Phase 3)._ Restore ElastiCache; sessions recover without a deploy. Confirm no
pod restart loop began.

### IdP unavailable

**Symptoms:** new logins fail; existing sessions continue working.

**Action:** _TBD (Phase 3)._

### Rollout stalled on bad config

**Symptoms:** new pods failing readiness; old pods still serving; `proxy_config_git_sha` shows a
mix of SHAs.

**This is the designed outcome, not an incident** — a bad ConfigMap stalls the rollout instead of
taking the service down.

**Action:** read the startup logs of a failing pod; the validator names the file and line.
`git revert` the offending PR and merge. _Detail TBD (Phase 2)._

### Sporadic 502s from upstreams

**Likely cause:** `SocketsHttpHandler.PooledConnectionIdleTimeout` is at or above an upstream's
keepalive timeout. Presents as network flakiness and is expensive to diagnose from the symptom.

**Action:** _TBD (Phase 6)._ Compare the configured idle timeout against the shortest upstream
keepalive; ours must be lower. Default 30s.

### Random logouts / intermittent auth failures

**Likely cause:** Data Protection key ring not shared or not persistent, or `SetApplicationName`
differing across pods. Requests bouncing between pods hit undecryptable cookies.

**Action:** _TBD (Phase 3)._ Verify all pods resolve the same key ring location and application
name.

### Audit events dropping

**Symptoms:** `proxy_audit_events_dropped_total` nonzero. Alarms should already have fired.

**Severity:** high. Silent loss of compliance logs is worse than the outage that caused it.

**Action:** _TBD (Phase 4)._ Check for CloudWatch `PutLogEvents` throttling — most likely cause is
multiple pods sharing a log stream instead of one stream per pod.

### Upstream slow or dead

**Action:** _TBD (Phase 6, informed by load testing)._

## Routine operations

- **Roll back a route change:** `git revert` the PR, merge. Config-only changes still produce a
  rolling restart via the `checksum/routes` annotation.
- **Force a user's logout:** _TBD (Phase 3)._ Server-side sessions make revocation possible; the
  procedure needs writing.
- **Deploy:** merge to main. CI builds, tests, validates config, renders the ConfigMap, pushes the
  image, and runs `helm upgrade`.
