# Security controls

Requirements that exist for security reasons rather than style reasons. Each will look redundant or
over-careful in isolation. None of them are.

**Rule for implementers:** do not refactor these away for elegance. If you must touch the code, add
a comment referencing the spec section so the next person knows what they are looking at. Every
control below has a corresponding test; deleting the control without deleting the test should turn
CI red.

---

## 1. Unconditional identity-header stripping (spec §6.2)

Every header the proxy uses to assert identity to an upstream is removed from the outbound request
**on every request**, before any conditional logic, with no early return above the strip.

```csharp
proxyPipeline.AddRequestTransform(ctx =>
{
    // Security control (spec §6.2). Strip unconditionally, before anything else.
    foreach (var h in IdentityHeaders.All)
        ctx.ProxyRequest.Headers.Remove(h);

    if (ctx.HttpContext.User.Identity?.IsAuthenticated != true)
        return default;
    ...
});
```

**Why:** if an upstream trusts `X-Auth-User-Id` and a client can set it, that is an authentication
bypass. Anonymous routes are not exempt — they reach upstreams too.

**Test:** an integration test sends every identity header from the client on both authenticated and
anonymous routes and asserts a stub upstream never observes a client-supplied value.

## 2. Destination allowlist (spec §4.4)

Cluster destination addresses must resolve inside an allowlist of internal DNS suffixes and CIDRs.
Enforced in the `ConfigValidator` CI check **and** again at application startup.

**Why:** without it, a merged route file is an SSRF and data-exfiltration primitive — anyone who can
get a YAML file merged can point the authenticated proxy at an address they control. Code review
alone is not sufficient; reviewers miss a one-character hostname change.

**Test:** the allowlist rejects an external address, verified both through the validator CLI and at
startup.

## 3. Caching restricted to anonymous routes (spec §6.3, §4.4)

`caching.enabled: true` is a validation error on any route whose `authorizationPolicy` is not
`anonymous`.

**Why:** caching authenticated responses is a vary-by-identity problem, and cross-user cache
leakage is the worst bug this system can produce. v1 does not attempt it.

## 4. Shared, persistent Data Protection key ring (spec §5.2.1)

Keys persist to S3 (or SSM) protected by KMS, and `SetApplicationName` is identical across all pods.

**Why:** without persistence, every pod restart invalidates all sessions — and since config changes
ship as rolling restarts, every route change would log out every user. Without sharing, requests
bouncing between pods hit undecryptable cookies intermittently, which presents as random logouts
nobody can reproduce.

## 5. Server-side session state (spec §5.2.2)

Sessions live in Redis behind `ITicketStore`. The cookie carries only an opaque session id.

**Why:** enriched claims exceed the 4KB cookie limit and trigger chunking, which breaks awkwardly
with some upstreams. Server-side sessions also give forced logout and revocation, which compliance
will ask for.

## 6. Fatal startup validation (spec §4.4)

Invalid configuration stops the pod from starting and fails readiness. There is no partial load and
no "skip the broken route and carry on."

**Why:** a partially-loaded route table is an unpredictable security posture — a route may lose its
authorization policy while still serving. Failing readiness stalls the rollout with old, good pods
still serving, which is the correct outcome.

## 7. Status UI exposure (spec §2, §7)

Port 8081 is bound to an internal ClusterIP Service and must never appear in the public ALB target
group. It additionally requires authentication via the same IdP plus membership in a configured
admin group.

**Why:** the status UI discloses the full internal route and cluster topology. Network topology is
the primary control; authentication is the second layer, not the only one.

## 8. Liveness never checks dependencies (spec §8.3)

`/health/live` reports only that the process is up.

**Why:** availability, not confidentiality — but the failure is severe. A liveness probe that fails
during a Redis blip makes Kubernetes restart every pod simultaneously, turning a degradation into
an outage.

---

## Deferred, by design

- **Signed assertion mode (spec §6.2).** If any upstream is reachable on the network without
  traversing this proxy, plain headers are insufficient and a short-lived signed JWT is required so
  the app can verify provenance. This is [open question 4](open-questions.md). Keep header writing
  behind an interface so it can be enabled per route later; do not implement it in v1.
