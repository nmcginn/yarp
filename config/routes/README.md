# Route configuration

**This directory is the route configuration.** One YAML file per application. Everything the proxy
routes is defined here and nowhere else.

Adding or changing a route: [`docs/contributing-routes.md`](../../docs/contributing-routes.md).
Schema and validation rules: [`docs/configuration.md`](../../docs/configuration.md).
Copy-paste starting point: [`docs/templates/route.yaml`](../../docs/templates/route.yaml).

## Rules

- File name matches the `application` name: `config/routes/<app>.yaml`.
- `routeId` and `clusterId` are unique across **every** file here, not just within one.
- Only `*.yaml` files are loaded. This README is ignored.
- Every PR touching this directory requires platform team approval (CODEOWNERS) and a green
  config validation check.

Validate locally — the same code CI and the proxy run:

```bash
dotnet run --project tools/ConfigValidator -- config/routes
```

Rollback is `git revert` on the PR.
