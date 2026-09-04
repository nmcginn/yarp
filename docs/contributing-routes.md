# Adding or changing a route

For application teams. You do not need to know anything about the proxy's internals to use it.

**The short version:** copy a template file, fill in four things, open a pull request, wait for a
green check and a review from the platform team. Your route is live a few minutes after merge.

---

## 1. Create your file

One YAML file per application, named after the application:

```
config/routes/<your-app>.yaml
```

Start from [`docs/templates/route.yaml`](templates/route.yaml) — it is a copy-paste starting point
with every field commented.

```bash
cp docs/templates/route.yaml config/routes/my-app.yaml
```

## 2. Fill it in

At minimum you need four things:

- **Hostname** your users will visit (`match.hosts`)
- **Destination** — the in-cluster address of your service (`clusters[].destinations`)
- **Who may access it** — `authorizationPolicy`, and `requiredGroups` if you want to restrict
  beyond "any authenticated employee"
- **Owning team** (`owner`) — used when something breaks and someone needs to find you

A minimal file:

```yaml
application: my-app
owner: my-team
routes:
  - routeId: my-app
    match:
      hosts: [my-app.corp.example.com]
      path: "{**catch-all}"
    clusterId: my-app-svc
    authorizationPolicy: authenticated
    requiredGroups: []          # empty = any authenticated user
    identityHeaders: [userId, email, groups, displayName]

clusters:
  - clusterId: my-app-svc
    destinations:
      primary:
        address: http://my-app.my-namespace.svc.cluster.local:8080/
```

Full field reference: [`configuration.md`](configuration.md).

## 3. Validate locally (optional but faster)

```bash
dotnet run --project tools/ConfigValidator -- config/routes
```

This is the exact check CI runs, so a green run locally means a green check on the PR.

## 4. Open a pull request

The route-request PR template will prompt you for the destination, required groups, and owning
team. Fill it in — reviewers use it to sanity-check the route without reading YAML.

Two things must happen before merge:

- the **config validation check** passes
- the **platform team** approves (CODEOWNERS requires this on `config/routes/`)

## 5. After merge

CI renders your file into a ConfigMap and Helm rolls the proxy pods. Expect **one to two minutes**.
This is a rolling restart, not a hot reload — that is deliberate, and it does not log anyone out.

Confirm your route landed on the status UI (`/api/status/routes` on the internal status service).
That page shows the route table as loaded in each pod, plus the git SHA it came from — it is the
fastest answer to "did my route actually deploy?"

---

## What your application receives

On an authenticated route, requests arrive with identity headers:

| Header | Contents |
|--------|----------|
| `X-Auth-User-Id` | Stable user identifier |
| `X-Auth-Email` | Email address |
| `X-Auth-Groups` | Comma-separated group names |
| `X-Auth-Display-Name` | Display name |

Request these with `identityHeaders`. **You can trust these headers**: the proxy strips any
client-supplied copy from every request before setting its own, so a value that reaches you came
from the proxy.

The one caveat: this holds only if your service is reachable *exclusively* through the proxy. If
your service also has a network path that bypasses the proxy, tell the platform team — the headers
alone are not sufficient in that case.

## Common review comments

- **Destination is outside the internal allowlist.** Destinations must be internal cluster or
  corporate addresses. This is a hard security control, not a preference; the validator will reject
  it before a human does.
- **`caching.enabled: true` on an authenticated route.** Not permitted — cross-user cache leakage.
  Caching is available on anonymous routes only.
- **`requiredGroups` left empty when it shouldn't be.** Empty means *any* authenticated employee.
  That is often right for internal tools and often wrong for anything with sensitive data.
- **Duplicate `routeId` or host collision with an existing app.** `routeId` is unique across every
  file in the repo.

## Rolling back

`git revert` the PR and merge. That is the whole rollback procedure — there is no separate config
version to restore.

## Getting help

Open an issue with the route-request template, or ask the platform team. If your route is merged
but not working, include the output of the status UI's config page (git SHA and checksum) — it
tells us immediately whether your change is actually live.
