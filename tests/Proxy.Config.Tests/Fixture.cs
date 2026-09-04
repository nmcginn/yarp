using Proxy.Config.Model;

namespace Proxy.Config.Tests;

internal static class Fixture
{
    /// <summary>The allowlist the deployed environments use: cluster-internal DNS only.</summary>
    public static readonly DestinationAllowlist ClusterOnly = new([".svc.cluster.local"], []);

    public static string Dir(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string EnvironmentFile(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "environments", name);

    /// <summary>Loads a fixture directory and returns every error, in file/line order.</summary>
    public static IReadOnlyList<ConfigError> Errors(string fixture, DestinationAllowlist? allowlist = null)
    {
        var ex = Assert.Throws<ConfigException>(() => RouteConfigLoader.Load(Dir(fixture), allowlist ?? ClusterOnly));
        return ex.Errors;
    }

    public static ConfigError Single(IReadOnlyList<ConfigError> errors, string containing) =>
        Assert.Single(errors, e => e.Message.Contains(containing, StringComparison.Ordinal));
}
