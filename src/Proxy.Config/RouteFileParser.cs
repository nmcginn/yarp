using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing.Patterns;
using Proxy.Config.Model;
using Proxy.Config.Yaml;

namespace Proxy.Config;

/// <summary>
/// Parses one route file (spec §4.2) into the internal model. Shape errors — unknown fields,
/// missing required fields, malformed host patterns, unparseable durations, unknown enum values —
/// are reported here with file and line. Cross-file rules live in <see cref="RouteSetValidator"/>.
/// </summary>
public static partial class RouteFileParser
{
    private static readonly IReadOnlyDictionary<string, AuthorizationPolicy> AuthorizationPolicies =
        new Dictionary<string, AuthorizationPolicy>(StringComparer.Ordinal)
        {
            ["authenticated"] = AuthorizationPolicy.Authenticated,
            ["anonymous"] = AuthorizationPolicy.Anonymous,
        };

    private static readonly IReadOnlyDictionary<string, AuditLevel> AuditLevels =
        new Dictionary<string, AuditLevel>(StringComparer.Ordinal)
        {
            ["standard"] = AuditLevel.Standard,
            ["detailed"] = AuditLevel.Detailed,
        };

    private static readonly IReadOnlyDictionary<string, IdentityHeader> IdentityHeaders =
        new Dictionary<string, IdentityHeader>(StringComparer.Ordinal)
        {
            ["userId"] = IdentityHeader.UserId,
            ["email"] = IdentityHeader.Email,
            ["groups"] = IdentityHeader.Groups,
            ["displayName"] = IdentityHeader.DisplayName,
        };

    /// <summary>
    /// Parses <paramref name="yaml"/>. Returns null when the file is unusable; partial problems are
    /// recorded in <paramref name="errors"/> and the returned file may still be incomplete, so callers
    /// must check <see cref="ErrorCollector.HasErrors"/> before trusting the result.
    /// </summary>
    public static RouteFile? Parse(string file, string yaml, ErrorCollector errors)
    {
        var root = YamlDocumentLoader.LoadRootMapping(file, yaml, errors);
        if (root is null)
        {
            return null;
        }

        root.RejectUnknownKeys("application", "owner", "routes", "clusters");

        var application = root.RequiredString("application");
        var owner = root.RequiredString("owner");
        var routeNodes = root.RequiredMappingList("routes");
        var clusterNodes = root.RequiredMappingList("clusters");

        var routes = new List<RouteDefinition>();
        foreach (var node in routeNodes ?? [])
        {
            if (ParseRoute(node) is { } route)
            {
                routes.Add(route);
            }
        }

        var clusters = new List<ClusterDefinition>();
        foreach (var node in clusterNodes ?? [])
        {
            if (ParseCluster(node) is { } cluster)
            {
                clusters.Add(cluster);
            }
        }

        if (routeNodes is { Count: 0 })
        {
            errors.Add(file, root.LineOf("routes"), "'routes' must contain at least one route");
        }

        if (clusterNodes is { Count: 0 })
        {
            errors.Add(file, root.LineOf("clusters"), "'clusters' must contain at least one cluster");
        }

        return new RouteFile(file, application ?? string.Empty, owner ?? string.Empty, routes, clusters);
    }

    private static RouteDefinition? ParseRoute(MappingReader node)
    {
        node.RejectUnknownKeys(
            "routeId", "match", "clusterId", "authorizationPolicy", "requiredGroups",
            "identityHeaders", "caching", "auditLevel");

        var routeId = node.RequiredString("routeId");
        var match = ParseMatch(node.RequiredMapping("match"));
        var clusterId = node.RequiredString("clusterId");
        var policy = node.RequiredEnum("authorizationPolicy", AuthorizationPolicies);
        var requiredGroups = node.OptionalStringList("requiredGroups");
        var identityHeaders = node.OptionalEnumList("identityHeaders", IdentityHeaders);
        var auditLevel = node.OptionalEnum("auditLevel", AuditLevels, AuditLevel.Standard);

        var cachingEnabled = false;
        if (node.OptionalMapping("caching") is { } caching)
        {
            caching.RejectUnknownKeys("enabled");
            cachingEnabled = caching.OptionalBool("enabled", defaultValue: false);
        }

        if (routeId is null || match is null || clusterId is null || policy is null || auditLevel is null)
        {
            return null;
        }

        return new RouteDefinition(
            routeId,
            match,
            clusterId,
            policy.Value,
            requiredGroups,
            identityHeaders,
            cachingEnabled,
            auditLevel.Value,
            new SourceLocation(node.File, node.LineOf("routeId")));
    }

    private static RouteMatch? ParseMatch(MappingReader? node)
    {
        if (node is null)
        {
            return null;
        }

        node.RejectUnknownKeys("hosts", "path");

        var hosts = node.RequiredStringList("hosts");
        var path = node.RequiredString("path");

        var validHosts = new List<string>();
        foreach (var host in hosts ?? [])
        {
            if (HostPattern().IsMatch(host))
            {
                validHosts.Add(host.ToLowerInvariant());
            }
            else
            {
                Report(node, "hosts",
                    $"malformed host pattern '{host}'; expected a DNS name such as app.corp.example.com or *.corp.example.com");
            }
        }

        if (hosts is { Count: 0 })
        {
            Report(node, "hosts", "'match.hosts' must contain at least one host");
        }

        if (path is not null)
        {
            try
            {
                RoutePatternFactory.Parse(path);
            }
            catch (RoutePatternException ex)
            {
                Report(node, "path", $"malformed path pattern '{path}': {ex.Message}");
                path = null;
            }
        }

        if (hosts is null || path is null || validHosts.Count != hosts.Count)
        {
            return null;
        }

        return new RouteMatch(validHosts, path);
    }

    private static ClusterDefinition? ParseCluster(MappingReader node)
    {
        node.RejectUnknownKeys("clusterId", "destinations", "healthCheck");

        var clusterId = node.RequiredString("clusterId");
        var destinations = ParseDestinations(node.RequiredMapping("destinations"));
        var healthCheck = ParseHealthCheck(node.OptionalMapping("healthCheck"));

        if (clusterId is null || destinations is null)
        {
            return null;
        }

        return new ClusterDefinition(
            clusterId,
            destinations,
            healthCheck,
            new SourceLocation(node.File, node.LineOf("clusterId")));
    }

    private static Dictionary<string, Destination>? ParseDestinations(MappingReader? node)
    {
        if (node is null)
        {
            return null;
        }

        var result = new Dictionary<string, Destination>(StringComparer.Ordinal);
        foreach (var (name, destination) in node.Entries())
        {
            destination.RejectUnknownKeys("address");
            var address = destination.RequiredString("address");
            if (address is null)
            {
                continue;
            }

            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
            {
                Report(destination, "address", $"'{address}' is not a valid absolute URL");
                continue;
            }

            result[name] = new Destination(uri, new SourceLocation(destination.File, destination.LineOf("address")));
        }

        if (result.Count == 0)
        {
            Report(node, null, "'destinations' must contain at least one destination");
            return null;
        }

        return result;
    }

    private static ActiveHealthCheck? ParseHealthCheck(MappingReader? node)
    {
        if (node is null)
        {
            return null;
        }

        node.RejectUnknownKeys("active");
        var active = node.OptionalMapping("active");
        if (active is null)
        {
            return null;
        }

        active.RejectUnknownKeys("enabled", "path", "interval");
        var enabled = active.OptionalBool("enabled", defaultValue: true);
        var path = active.OptionalString("path") ?? "/health";
        var interval = active.OptionalDuration("interval") ?? TimeSpan.FromSeconds(10);

        if (!path.StartsWith('/'))
        {
            Report(active, "path", $"health check path '{path}' must start with '/'");
        }

        if (interval <= TimeSpan.Zero)
        {
            Report(active, "interval", "health check interval must be greater than zero");
        }

        return new ActiveHealthCheck(enabled, path, interval);
    }

    private static void Report(MappingReader node, string? key, string message)
    {
        var line = key is null ? node.Line : node.LineOf(key);
        // The reader owns the collector; route the message through it so every error shares one path.
        node.Report(line, message);
    }

    // DNS labels (letters, digits, hyphens) separated by dots, with an optional leading wildcard label.
    [GeneratedRegex(@"^(\*\.)?([a-z0-9]([a-z0-9-]*[a-z0-9])?\.)*[a-z0-9]([a-z0-9-]*[a-z0-9])?$", RegexOptions.IgnoreCase)]
    private static partial Regex HostPattern();
}
