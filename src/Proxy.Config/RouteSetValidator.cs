using System.Text.RegularExpressions;
using Proxy.Config.Model;

namespace Proxy.Config;

/// <summary>
/// The cross-file validation rules of spec §4.4. Runs identically in CI (tools/ConfigValidator) and
/// at application startup; do not reimplement any of this elsewhere.
/// </summary>
public static partial class RouteSetValidator
{
    public static void Validate(IReadOnlyList<RouteFile> files, DestinationAllowlist allowlist, ErrorCollector errors)
    {
        CheckApplicationNames(files, errors);

        var routes = files.SelectMany(f => f.Routes).ToList();
        var clusters = files.SelectMany(f => f.Clusters).ToList();

        var clusterIds = CheckDuplicateIds(
            clusters.Select(c => (c.ClusterId, c.Location)), "clusterId", errors);
        CheckDuplicateIds(routes.Select(r => (r.RouteId, r.Location)), "routeId", errors);

        foreach (var route in routes)
        {
            if (!clusterIds.Contains(route.ClusterId))
            {
                errors.Add(route.Location, $"route '{route.RouteId}' references unknown clusterId '{route.ClusterId}'");
            }

            // Security control (spec §6.3): caching authenticated responses is a vary-by-identity
            // problem, and cross-user cache leakage is the worst bug this system can produce.
            if (route.CachingEnabled && route.AuthorizationPolicy != AuthorizationPolicy.Anonymous)
            {
                errors.Add(route.Location,
                    $"route '{route.RouteId}' enables caching but its authorizationPolicy is " +
                    $"'{route.AuthorizationPolicy.ToString().ToLowerInvariant()}'; caching is permitted only on anonymous routes (spec §6.3)");
            }
        }

        CheckMatchCollisions(routes, errors);

        // Security control (spec §4.4): without the allowlist, a merged route file is an SSRF and
        // data-exfiltration primitive. Enforced in CI and again at startup.
        foreach (var cluster in clusters)
        {
            foreach (var (name, destination) in cluster.Destinations)
            {
                if (allowlist.Reject(destination.Address) is { } reason)
                {
                    errors.Add(destination.Location,
                        $"destination '{name}' of cluster '{cluster.ClusterId}' points outside the internal allowlist: {reason}");
                }
            }
        }
    }

    private static void CheckApplicationNames(IReadOnlyList<RouteFile> files, ErrorCollector errors)
    {
        foreach (var file in files)
        {
            var expected = Path.GetFileNameWithoutExtension(file.Path);
            if (file.Application.Length > 0 && !string.Equals(file.Application, expected, StringComparison.Ordinal))
            {
                errors.Add(file.Path, 1,
                    $"'application' is '{file.Application}' but the file is named '{Path.GetFileName(file.Path)}'; " +
                    $"the file name must match the application name");
            }
        }
    }

    private static HashSet<string> CheckDuplicateIds(
        IEnumerable<(string Id, SourceLocation Location)> items, string field, ErrorCollector errors)
    {
        var seen = new Dictionary<string, SourceLocation>(StringComparer.Ordinal);
        foreach (var (id, location) in items)
        {
            if (seen.TryGetValue(id, out var first))
            {
                errors.Add(location, $"duplicate {field} '{id}'; first defined at {first}");
            }
            else
            {
                seen[id] = location;
            }
        }

        return seen.Keys.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Two routes that share a host and an equivalent path pattern have the same precedence; YARP
    /// would treat a request for them as ambiguous. Path patterns are compared with parameter names
    /// removed, so <c>/api/{id}</c> and <c>/api/{key}</c> collide.
    /// </summary>
    private static void CheckMatchCollisions(IReadOnlyList<RouteDefinition> routes, ErrorCollector errors)
    {
        var seen = new Dictionary<(string Host, string Path), RouteDefinition>();
        foreach (var route in routes)
        {
            var path = NormalizePath(route.Match.Path);
            foreach (var host in route.Match.Hosts)
            {
                var key = (host, path);
                if (seen.TryGetValue(key, out var other))
                {
                    if (!ReferenceEquals(other, route))
                    {
                        errors.Add(route.Location,
                            $"route '{route.RouteId}' matches the same host and path as route '{other.RouteId}' " +
                            $"({other.Location}): host '{host}', path '{route.Match.Path}'");
                    }
                }
                else
                {
                    seen[key] = route;
                }
            }
        }
    }

    private static string NormalizePath(string path)
    {
        var trimmed = path.Trim().TrimStart('/').ToLowerInvariant();
        return ParameterName().Replace(trimmed, m => m.Groups[1].Value.Length > 0 ? "{**}" : "{}");
    }

    // {name}, {name?}, {name:constraint}, {*name}, {**name} → {} or {**}
    [GeneratedRegex(@"\{(\*\*?)?[^}]*\}")]
    private static partial Regex ParameterName();
}
