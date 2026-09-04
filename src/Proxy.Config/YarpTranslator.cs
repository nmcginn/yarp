using Proxy.Config.Model;
using Yarp.ReverseProxy.Configuration;
using YarpRouteMatch = Yarp.ReverseProxy.Configuration.RouteMatch;

namespace Proxy.Config;

/// <summary>
/// Translates the validated internal model into YARP configuration (spec §4.2, ADR 0005). The
/// output goes straight into YARP's <c>InMemoryConfigProvider</c>; no custom provider is needed.
/// </summary>
public static class YarpTranslator
{
    /// <summary>Names of the YARP authorization policies routes are translated to.</summary>
    public static class AuthorizationPolicies
    {
        /// <summary>YARP's built-in value: no authorization is applied.</summary>
        public const string Anonymous = "anonymous";

        /// <summary>A named ASP.NET Core policy requiring an authenticated user; registered by the host.</summary>
        public const string Authenticated = "authenticated";
    }

    /// <summary>Per-route metadata keys carried on <see cref="RouteConfig.Metadata"/> for transforms, audit, and the status UI.</summary>
    public static class Metadata
    {
        public const string Application = "application";
        public const string Owner = "owner";
        public const string SourceFile = "sourceFile";
        public const string AuditLevel = "auditLevel";
        public const string IdentityHeaders = "identityHeaders";
        public const string RequiredGroups = "requiredGroups";
    }

    public static (IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters) Translate(RouteSet set)
    {
        var routes = new List<RouteConfig>();
        foreach (var file in set.Files)
        {
            foreach (var route in file.Routes)
            {
                routes.Add(TranslateRoute(file, route));
            }
        }

        var clusters = set.Clusters.Select(TranslateCluster).ToList();
        return (routes, clusters);
    }

    public static RouteConfig TranslateRoute(RouteFile file, RouteDefinition route) => new()
    {
        RouteId = route.RouteId,
        ClusterId = route.ClusterId,
        Match = new YarpRouteMatch
        {
            Hosts = route.Match.Hosts.ToArray(),
            Path = route.Match.Path,
        },
        AuthorizationPolicy = route.AuthorizationPolicy switch
        {
            AuthorizationPolicy.Anonymous => AuthorizationPolicies.Anonymous,
            _ => AuthorizationPolicies.Authenticated,
        },
        // Caching (spec §6.3) is translated to an OutputCachePolicy in Phase 6. The validator already
        // guarantees it is only ever enabled on anonymous routes.
        Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Metadata.Application] = file.Application,
            [Metadata.Owner] = file.Owner,
            [Metadata.SourceFile] = Path.GetFileName(file.Path),
            [Metadata.AuditLevel] = route.AuditLevel.ToString().ToLowerInvariant(),
            [Metadata.IdentityHeaders] = string.Join(',', route.IdentityHeaders.Select(h => h.ToString())),
            [Metadata.RequiredGroups] = string.Join(',', route.RequiredGroups),
        },
    };

    public static ClusterConfig TranslateCluster(ClusterDefinition cluster) => new()
    {
        ClusterId = cluster.ClusterId,
        Destinations = cluster.Destinations.ToDictionary(
            d => d.Key,
            d => new DestinationConfig { Address = d.Value.Address.ToString() },
            StringComparer.OrdinalIgnoreCase),
        HealthCheck = cluster.ActiveHealthCheck is { Enabled: true } check
            ? new HealthCheckConfig
            {
                Active = new ActiveHealthCheckConfig
                {
                    Enabled = true,
                    Path = check.Path,
                    Interval = check.Interval,
                    Policy = "ConsecutiveFailures",
                },
            }
            : null,
    };
}
