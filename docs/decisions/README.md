# Architecture decision records

Short records of decisions that shape the system, written so that the next person understands why
something is the way it is — and, importantly, **what the decision lets us not build**.

Write one when a decision has consequences beyond the PR that makes it. For a decision contained
within a PR, note it in the PR description instead.

Numbered sequentially. Copy [`0000-template.md`](0000-template.md).

| # | Decision | Status |
|---|----------|--------|
| [0001](0001-gitops-route-configuration.md) | Route configuration in Git, not a database | Accepted (spec) |
| [0002](0002-rolling-restart-over-hot-reload.md) | Config changes ship as a rolling restart | Accepted (spec) |
| [0003](0003-direct-cloudwatch-audit-sink.md) | Audit events written directly to CloudWatch Logs | Accepted (spec) |
| [0004](0004-server-side-sessions.md) | Session state server-side in Redis via `ITicketStore` | Accepted (spec) |
| [0005](0005-constrained-route-schema.md) | Constrained route schema, not raw YARP config | Accepted (spec) |
| [0006](0006-one-process-three-hosts.md) | One process, three ASP.NET Core hosts — one per listener | Accepted |

"Accepted (spec)" means the decision was made in [`../spec.md`](../spec.md) and is recorded here for
discoverability. Reversing one is a spec change, not a refactor.
