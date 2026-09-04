namespace Proxy.Config.Model;

/// <summary>
/// Every route file in the directory, merged and validated. This is what gets translated to YARP
/// and what the status UI reports as "loaded in this pod" (spec §7).
/// </summary>
public sealed record RouteSet(
    IReadOnlyList<RouteFile> Files,
    IReadOnlyList<RouteDefinition> Routes,
    IReadOnlyList<ClusterDefinition> Clusters);
