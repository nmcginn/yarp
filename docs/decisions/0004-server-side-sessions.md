# 0004. Session state server-side in Redis via `ITicketStore`

**Status:** Accepted (spec §5.2.1, §5.2.2)
**Date:** 2026-09-04

## Context

ASP.NET Core cookie authentication stores the whole authentication ticket in the cookie by default.
With enriched claims — groups in particular — that exceeds the 4KB cookie limit and triggers cookie
chunking, which breaks awkwardly with some upstreams.

Pods are stateless and requests are not sticky, so any pod must be able to read any session.

## Decision

Sessions are stored server-side in Redis behind `ITicketStore`. The cookie carries only an opaque
session id. Key format `session:{sessionId}`; TTL matches ticket expiry; sliding expiration updates
the TTL, not the cookie.

The Data Protection key ring is persisted (S3 or SSM, protected by KMS) and shared, with
`SetApplicationName("corp-identity-proxy")` identical across all pods.

## Consequences

- Cookies stay small; no chunking.
- Forced logout and revocation become possible, which compliance will ask for.
- Sessions survive pod replacement, which is what makes [ADR 0002](0002-rolling-restart-over-hot-reload.md)
  affordable and lets deployment skip stickiness and connection draining.
- Redis becomes a hard dependency for authenticated traffic. When it is down, sessions fail closed
  and anonymous routes keep working; pods must not restart, so `/health/live` does not check it.
- Omitting either half is a production incident that is hard to diagnose: without persistence, every
  restart logs out every user; without sharing, requests bouncing between pods hit undecryptable
  cookies intermittently.

## What this lets us not build

No sticky sessions, no session affinity at the ALB, no connection draining on deploy, no custom
cookie chunking workarounds.
