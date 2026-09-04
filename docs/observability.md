# Observability

Two log streams, kept strictly separate; Prometheus metrics; two health endpoints with different
jobs.

## Two log streams, never interleaved

**Application logs** → stdout → cluster log pipeline → `/aws/eks/...`. Serilog, JSON formatted.
Operational concern, short retention.

The format is Serilog's compact JSON: `@t` timestamp, `@l` level (absent for Information), `@m`
rendered message, then the event's properties as top-level fields. Every request log line carries
`CorrelationId`, `RequestPath`, `StatusCode`, and `Elapsed`. Minimum level is `Information`, with
`Microsoft.AspNetCore` at `Warning`; set `PROXY_Logging__MinimumLevel` (Helm `logging.minimumLevel`)
to `Debug` when a pod needs a closer look.

**Compliance audit events** → CloudWatch Logs, dedicated log group `/proxy/audit`, written directly
via `Serilog.Sinks.AwsCloudWatch`. A separate group so retention and IAM read access are controlled
independently of application logs.

Implementation requirements:

- **One log stream per pod**, named by pod name. CloudWatch throttles `PutLogEvents` per stream; a
  shared stream across replicas will throttle under load and drop events.
- **Batch writes, bound the in-memory queue explicitly**, and emit
  `proxy_audit_events_dropped_total` when the queue is full. **Alarm on any nonzero value.** Silent
  loss of compliance logs is worse than the outage that caused it.
- **Set log group retention in Terraform**, not by hand, and to whatever compliance requires rather
  than a default.

### Audit event shape

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

Event types: `request.completed`, `auth.login`, `auth.logout`, `auth.denied`, and `config.loaded`
(with git SHA, at startup).

Configuration change auditing is git history and does **not** belong in this stream.

> **Do not finalize this schema** until [open question 2](open-questions.md) is answered. The
> incumbent's log format has downstream consumers — SIEM rules, compliance reports, dashboards —
> that nobody has inventoried. Discovering a required field after cutover is far more expensive
> than finding it now. This is the highest schedule risk in the project.

### Migration path to Kinesis

When volume justifies it, attach a CloudWatch Logs subscription filter on `/proxy/audit` routing to
Firehose → S3. **This requires no application change**, which is why direct-to-CloudWatch is the
right v1 choice rather than a compromise.

Do not build a pluggable sink abstraction for this. See
[ADR 0003](decisions/0003-direct-cloudwatch-audit-sink.md).

## Metrics

Prometheus on port 8082. Beyond the standard ASP.NET Core and YARP meters:

| Metric | Type | Why it matters |
|--------|------|----------------|
| `proxy_config_git_sha` | info gauge | Divergence across pods indicates a stalled rollout |
| `proxy_config_routes_loaded` | gauge | Sanity check that config landed |
| `proxy_auth_decisions_total{decision}` | counter | Authorization behavior over time |
| `proxy_session_store_errors_total` | counter | Redis degradation, before users report it |
| `proxy_audit_events_dropped_total` | counter | **Alarm on nonzero.** Compliance log loss |

## Health endpoints

- **`/health/live`** — the process is up. **No dependency checks, ever.** A liveness probe that
  fails during a Redis blip makes Kubernetes restart every pod at once, turning a degradation into
  an outage.
- **`/health/ready`** — config parsed and loaded successfully. Fails on invalid config, which
  correctly stalls a rollout carrying a bad ConfigMap.

The distinction is the whole point: liveness answers "should this pod be killed," readiness answers
"should this pod receive traffic." Conflating them is how a dependency blip becomes downtime.

## Correlation

Every request gets a correlation id — the inbound `X-Request-Id` if present, otherwise generated —
assigned as the first middleware and attached to the logging scope, the audit event, and the
outbound request. It is the join key between application logs, audit events, and upstream logs.
