namespace Proxy.Config.Model;

/// <summary>
/// One parsed <c>config/routes/*.yaml</c> file (spec §4.2). This is the constrained internal model —
/// raw YARP configuration is never accepted (ADR 0005).
/// </summary>
public sealed record RouteFile(
    string Path,
    string Application,
    string Owner,
    IReadOnlyList<RouteDefinition> Routes,
    IReadOnlyList<ClusterDefinition> Clusters);

public sealed record RouteDefinition(
    string RouteId,
    RouteMatch Match,
    string ClusterId,
    AuthorizationPolicy AuthorizationPolicy,
    IReadOnlyList<string> RequiredGroups,
    IReadOnlyList<IdentityHeader> IdentityHeaders,
    bool CachingEnabled,
    AuditLevel AuditLevel,
    SourceLocation Location);

public sealed record RouteMatch(IReadOnlyList<string> Hosts, string Path);

public sealed record ClusterDefinition(
    string ClusterId,
    IReadOnlyDictionary<string, Destination> Destinations,
    ActiveHealthCheck? ActiveHealthCheck,
    SourceLocation Location);

public sealed record Destination(Uri Address, SourceLocation Location);

public sealed record ActiveHealthCheck(bool Enabled, string Path, TimeSpan Interval);

public enum AuthorizationPolicy
{
    Authenticated,
    Anonymous,
}

public enum AuditLevel
{
    Standard,
    Detailed,
}

/// <summary>The identity headers a route may ask the proxy to send upstream (spec §6.2).</summary>
public enum IdentityHeader
{
    UserId,
    Email,
    Groups,
    DisplayName,
}
