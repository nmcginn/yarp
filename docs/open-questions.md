# Open questions

Decisions that are not ours to make alone, or that need data we do not have yet. Each blocks
something. Update the status line and record the outcome in an ADR when one is settled.

| # | Question | Blocks | Owner | Status |
|---|----------|--------|-------|--------|
| 1 | Domain topology | Phase 3 | _unassigned_ | Open |
| 2 | Compliance log consumers | Phase 4 | _unassigned_ | Open |
| 3 | Caching semantics of the incumbent | Phase 6 | _unassigned_ | Open |
| 4 | Upstream network reachability | Phase 4 design | _unassigned_ | Open |
| 5 | Undocumented incumbent behavior | Cutover | _unassigned_ | Open |
| 6 | Session cutover strategy | Phase 3 | _unassigned_ | Open |
| 7 | Route request workflow | Phase 2 rollout | _unassigned_ | Open |

---

### 1. Domain topology (spec §5.2.3)

Do all in-scope applications share a parent domain?

If yes, issue the auth cookie on the parent (`.corp.example.com`) and SSO works with one handshake.
If applications live on separate apex domains, we need a central auth domain plus per-domain token
exchange on first visit — materially more work.

**Working assumption for v1:** shared parent. Structure the code so the exchange flow can be added
without rework. **Must be confirmed before implementing Phase 3.**

### 2. Compliance log consumers (spec §8.1)

Who reads the incumbent's logs, and what fields do their rules depend on? SIEM rules, compliance
reports, and dashboards exist; nobody has inventoried them.

**Highest schedule risk in the project.** Discovering a required field after cutover is far more
expensive than finding it now. Note also that CloudWatch Logs Insights query syntax differs from
whatever those consumers use today, which may itself be a migration item.

**Action:** inventory consumers before finalizing the audit event schema in Phase 4.

### 3. Caching semantics (spec §6.3)

What does the incumbent actually cache? Expect most of it to be static assets (which belong in
CloudFront) or token/JWKS caching (in-process and trivial).

**Measure before building anything more sophisticated.** v1 ships output caching for anonymous
routes only.

### 4. Upstream network reachability (spec §6.2)

Are any upstreams reachable without traversing this proxy? If so, plain identity headers are
insufficient for those upstreams and signed assertions become required.

Determines whether the deferred signed-assertion mode graduates from "design for it" to "build it."

### 5. Undocumented incumbent behavior (spec §12.5)

Per-app quirks and header dependencies nobody wrote down should be expected to dominate the
schedule.

**Action:** plan traffic capture and differential testing against the existing proxy. This is
Phase 6 work but the capture should start early — it is cheap to collect and expensive to
reconstruct.

### 6. Session cutover strategy (spec §12.6)

Dual-run with shared session state, or a flag-day logout? Political as much as technical.

**Settle before Phase 3**, because dual-run constrains the ticket store and key ring design.

### 7. Route request workflow (spec §12.7)

New with the GitOps model. Do app teams have repo access and enough Git familiarity to open a PR?
What is the review SLA, and who is on call for it?

PR-based self-service is only self-service if review is fast; otherwise the platform team becomes
the same bottleneck the old system's owners were.

**Partially addressed:** [`contributing-routes.md`](contributing-routes.md) and the route-request
PR template exist. Still needed: named reviewers, a review SLA, and confirmation that app teams
have repo access.
