# Documentation

| Document | What it is |
|----------|------------|
| [`spec.md`](spec.md) | The authoritative project specification. When anything here disagrees with it, the spec wins. |
| [`roadmap.md`](roadmap.md) | Phase plan and current status. Start here to find out what to work on. |
| [`architecture.md`](architecture.md) | Condensed operational view: ports, request path, dependencies, failure behavior. |
| [`configuration.md`](configuration.md) | Route file schema and validation rules. |
| [`contributing-routes.md`](contributing-routes.md) | Guide for application teams adding or changing a route. |
| [`security-controls.md`](security-controls.md) | The non-negotiable controls and why each exists. Read before refactoring near one. |
| [`observability.md`](observability.md) | Log streams, audit event schema, metrics, health endpoints. |
| [`testing.md`](testing.md) | What gets tested and which tests enforce which control. |
| [`runbook.md`](runbook.md) | Failure modes and operational procedures. Skeleton; completed in Phase 6. |
| [`open-questions.md`](open-questions.md) | Unsettled decisions and what each one blocks. |
| [`decisions/`](decisions/) | Architecture decision records. |
| [`templates/route.yaml`](templates/route.yaml) | Copy-paste starting point for a new route file. |

## Reading order

**New engineer, first day:** `architecture.md` → `spec.md` §1–§8 → `security-controls.md`.

**Picking up implementation work:** `roadmap.md` → the spec sections it references →
`open-questions.md` to check nothing you need is unsettled.

**Adding a route for your app:** `contributing-routes.md`, and nothing else.
