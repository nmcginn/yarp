# 0006. One process, three ASP.NET Core hosts — one per listener

**Status:** Accepted
**Date:** 2026-09-04

## Context

Spec §2 puts three surfaces on three ports in one container: the proxy data plane (8080), the
read-only status UI (8081), and health/metrics (8082). Port 8081 must never be reachable through
the public ALB, and the data plane must never serve operational endpoints.

The obvious implementation is one ASP.NET Core application listening on three ports with the
pipeline branched by `HttpContext.Connection.LocalPort`. That has a known footgun: inside a
`WebApplication`, calling `UseRouting()`/`UseEndpoints()` in a `MapWhen` branch registers the
branch's endpoints on the *global* route builder, so an endpoint mapped "for port 8081" is also
matched for requests on 8080. Avoiding it means either a custom routing `MatcherPolicy` that
filters endpoints by port, or remembering to tag every endpoint with the listener it belongs to —
and a forgotten tag is a silently exposed endpoint.

## Decision

`Proxy.Host` builds **three `WebApplication` instances in one process**, each with its own Kestrel
listener, its own DI container, and its own middleware pipeline (`ProxyProcess`). YARP is only
registered in the data-plane host. Health checks are only mapped in the ops host. The status UI
(Phase 5) is only mapped in the status host.

Shared state — the loaded route set, and later the Data Protection and Redis wiring — is passed in
explicitly as constructed objects or through shared extension methods.

## Consequences

- An endpoint can only be reached on the port it was mapped on. Nothing filters by port because
  nothing is shared by port. Security control 7 becomes structural rather than procedural.
- Each surface reads like a textbook ASP.NET Core application; there is no routing trick to learn.
- Startup and shutdown are coordinated in one place: all three start, and when any one is asked to
  stop (SIGTERM), all three stop.
- Cross-cutting registrations (authentication for the data plane *and* the status UI in Phase 3)
  are made twice, through one shared extension method. That duplication is visible and deliberate.
- `WebApplicationFactory<Program>` assumes one host, so integration tests start the real
  `ProxyProcess` on loopback with port 0 and talk to it over HTTP. This also means the tests
  exercise the actual port topology.

## What this lets us not build

A port-aware routing policy, listener metadata on every endpoint, and the test that would have to
prove nobody forgot the metadata.
