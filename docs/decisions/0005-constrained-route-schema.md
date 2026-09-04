# 0005. Constrained route schema, not raw YARP configuration

**Status:** Accepted (spec §4.2)
**Date:** 2026-09-04

## Context

Route files could accept YARP's own `RouteConfig` and `ClusterConfig` shapes directly, saving a
translation layer and giving application teams the full expressiveness of YARP.

But those files are merged by pull request, and the merge is the only gate. Full YARP
expressiveness in a merged file means arbitrary transforms and arbitrary destinations.

## Decision

Route files use a constrained internal schema (spec §4.2). Files are parsed into internal types,
validated, and then translated into YARP configuration. Raw YARP config is not accepted.

## Consequences

- The schema is a safety boundary: the destination allowlist, the anonymous-only caching rule, and
  the authorization policy vocabulary are all enforceable because the surface is small.
- New YARP capabilities require a deliberate schema addition rather than appearing automatically.
  That is the intended friction.
- There is a translation layer to maintain, and it needs unit tests.

## What this lets us not build

Nothing is saved here — this decision *adds* code. It is recorded because the temptation to delete
that code and "just pass through YARP options" will recur, and doing so silently disables the
validator and with it the SSRF control. Widening the schema is a security review, not a cleanup.
