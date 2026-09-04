# Architecture

A condensed, operational view. The full reasoning lives in [`spec.md`](spec.md).

## Shape

One container image, one Kubernetes Deployment, three Kestrel listeners.

```
                    ┌───────────────────────────────────────────┐
  internet ──ALB──▶ │ :8080  proxy data plane                   │ ──▶ upstream services
                    │                                           │     (cluster-internal)
  platform team ──▶ │ :8081  read-only status UI (ClusterIP)    │
                    │                                           │
  kube / prom   ──▶ │ :8082  health, readiness, metrics         │
                    └───────────────────────────────────────────┘
                            │            │              │
                          Redis      S3 + KMS      CloudWatch Logs
                     (sessions,   (Data Protection   (/proxy/audit)
                      output cache)   key ring)
```

Port 8081 must never be attached to the public ALB target group. It is protected by network
topology first, authentication second.

## Request path (port 8080)

Middleware order is deliberate; changing it changes the security properties.

1. Correlation id assignment — accept inbound `X-Request-Id` or generate one
2. Structured request logging scope
3. Exception handling
4. Authentication (cookie; OIDC challenge on miss)
5. Authorization — per-route policy from config
6. Output cache — only where the route opts in, and only on anonymous routes
7. YARP `MapReverseProxy`, with the identity-header transform

The transform strips every identity header unconditionally before adding any, on every request
including anonymous ones. See [`security-controls.md`](security-controls.md).

## External dependencies and failure behavior

| Dependency | Purpose | Failure behavior |
|------------|---------|------------------|
| Redis (ElastiCache) | Session ticket store, output cache | Sessions fail closed; anonymous routes unaffected; cache bypassed |
| S3 + KMS | Data Protection key ring persistence | Startup failure — do not start without it |
| Corporate IdP (OIDC) | Authentication | Existing sessions unaffected; new logins fail |
| CloudWatch Logs | Compliance audit sink | Bounded local buffer, alarm on drops |

Note what is absent: **no config database**. Route configuration is compiled from files in this
repository. Redis is the only stateful runtime dependency.

## State

Pods are fully stateless. No sticky sessions, ever.

- Session state is server-side in Redis behind `ITicketStore`; the cookie carries only an opaque
  session id.
- The Data Protection key ring is shared and persistent, so any pod can decrypt any cookie and a
  restart does not log anyone out.

Together these make rolling restarts session-preserving, which is what lets config changes ship as
restarts (see [ADR 0002](decisions/0002-rolling-restart-over-hot-reload.md)) and lets deployment
skip stickiness and connection-draining complexity.

## Config path

```
config/routes/*.yaml  ──PR + CI validation──▶  ConfigMap manifest  ──Helm──▶  /app/config/routes
                                                                                     │
                                        startup: read → merge → validate → translate → YARP
                                                                                     │
                                                              InMemoryConfigProvider ┘
```

Validation runs twice with the same code: in CI via `tools/ConfigValidator`, and at startup.
Startup failure is fatal and fails readiness, so a bad ConfigMap stalls the rollout with old pods
still serving.

## Health semantics

- `/health/live` — the process is up. **No dependency checks.** If this failed on a Redis blip,
  Kubernetes would restart pods and turn a degradation into an outage.
- `/health/ready` — config parsed and loaded successfully. Fails on invalid config, which is how a
  bad rollout stalls.

## Deployment

Minimum 3 replicas, PodDisruptionBudget `minAvailable: 2`, HPA on CPU and active request count,
rolling updates with `maxUnavailable: 0`.

Terraform owns: ElastiCache, the S3 key ring bucket, the KMS key, the `/proxy/audit` log group with
explicit retention, and IAM roles for service accounts. No RDS, no Firehose.
