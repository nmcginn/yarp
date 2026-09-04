# 0002. Config changes ship as a rolling restart, not a hot reload

**Status:** Accepted (spec §4.3)
**Date:** 2026-09-04

## Context

Route configuration reaches pods as a ConfigMap mounted at `/app/config/routes`. The obvious design
is to watch that directory and reload the route table in place.

Two problems. First, hot reload adds a second mechanism for getting a change into production, with
its own failure modes and its own debugging. Second — and specific to .NET — ConfigMap volume
mounts are updated by swapping a `..data` symlink, and `FileSystemWatcher` does not fire reliably on
that. `reloadOnChange: true` against a ConfigMap mount is a known source of silent staleness: the
pod keeps serving old config and nothing indicates it.

## Decision

A config change triggers a rolling restart. Helm sets a checksum annotation on the pod template:

```yaml
annotations:
  checksum/routes: {{ include (print $.Template.BasePath "/routes-configmap.yaml") . | sha256sum | quote }}
```

Config changes and code deploys then use one mechanism.

The restart is nearly free: given the shared Data Protection key ring and the Redis ticket store,
rolling restarts are already session-preserving. That cost is paid in Phase 3 regardless.

## Consequences

- One thing to understand and one thing to debug.
- Propagation latency of one to two minutes.
- A bad ConfigMap stalls the rollout with old pods still serving — the correct outcome, and one that
  a hot reload would not give us.
- The proxy needs no custom `IProxyConfigProvider`; YARP's `InMemoryConfigProvider` is enough.

## What this lets us not build

No file watcher, no reload pipeline, no "is my config stale" diagnostics.

If propagation latency later proves annoying, the escape hatch is a polling reloader that hashes the
merged file set every N seconds and calls `InMemoryConfigProvider.Update`. Structure the loading
code so this can be added without touching the pipeline — but **do not build it in v1.**
