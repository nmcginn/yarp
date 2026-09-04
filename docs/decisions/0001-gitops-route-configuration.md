# 0001. Route configuration in Git, not a database

**Status:** Accepted (spec §4)
**Date:** 2026-09-04

## Context

The incumbent proxy stores route configuration in a database with a write-capable admin UI. That
design carries an activation workflow, a snapshot table, a diff view, and an audit trail — all of
which have to be built, secured, and maintained, and all of which duplicate things a version
control system already does well.

Route changes are low-frequency, need review by the platform team, and need an auditable history.

## Decision

Route configuration lives in `config/routes/` as YAML, one file per application. A pull request is
the request mechanism. Git history is the version history, `git revert` is the rollback, and the PR
is the audit record. CI validation gates merge; CODEOWNERS enforces platform team review.

YAML rather than JSON, because comments and lower punctuation noise matter when the file *is* the
review artifact.

One file per application, because it minimizes merge conflicts when several teams change routes in
the same week, and leaves the door open for per-app CODEOWNERS entries later.

## Consequences

- Configuration and code share one review, approval, and deploy mechanism.
- Propagation is not instant: a change is live one to two minutes after merge.
- Application teams need repo access and enough Git familiarity to open a PR, and the platform team
  needs a review SLA — otherwise self-service becomes the same bottleneck the old system had. This
  is [open question 7](../open-questions.md).
- Redis becomes the only stateful runtime dependency.

## What this lets us not build

No config database. No snapshot table. No activation endpoint. No diff API. No write-capable admin
UI. No config-change audit stream — git history covers it.

Do not reintroduce any of these to solve a propagation-latency or history problem. Git and the
rolling restart handle both.
