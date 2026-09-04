## Route request

<!--
  For adding or changing a route in config/routes/.
  Guide: docs/contributing-routes.md — you should not need to know anything about the proxy.
-->

**Application:**
**Owning team:**
**Requested by:**

### The route

| | |
|---|---|
| **Hostname(s)** | <!-- e.g. my-app.corp.example.com --> |
| **Destination** | <!-- e.g. http://my-app.my-ns.svc.cluster.local:8080/ --> |
| **Access** | <!-- authenticated / anonymous --> |
| **Required groups** | <!-- group names, or "any authenticated user" --> |
| **New route or change?** | <!-- new / change to an existing route --> |

### If this is a change

<!-- What is different, and what happens to users during the rolling restart? -->

### Anything unusual

<!--
  Tell us if any of these apply — they change what we need to review:
  - Your service is also reachable on the network WITHOUT going through the proxy
  - You need response caching (only available on anonymous routes)
  - The upstream has an unusual keepalive timeout
  - There are host or path collisions with an existing application
-->

### Checklist

- [ ] File is `config/routes/<application>.yaml` and `application` matches the file name
- [ ] `routeId` and `clusterId` are unique across all files in `config/routes/`
- [ ] Destination is an internal address
- [ ] `requiredGroups` reflects who should actually have access (empty = **any** authenticated user)
- [ ] `caching.enabled` is `false`, or the route is `anonymous`
- [ ] Config validation check is green
