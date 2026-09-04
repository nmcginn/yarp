using Microsoft.Extensions.Configuration;

namespace Proxy.Host;

/// <summary>
/// Process-level settings: where the config files are and which ports to listen on. Read once from
/// environment variables prefixed <c>PROXY_</c> (<c>PROXY_Config__EnvironmentFile</c>,
/// <c>PROXY_Ports__DataPlane</c>) and command-line switches (<c>--Config:EnvironmentFile=…</c>).
/// Everything else the proxy does is driven by the route and environment files themselves.
/// </summary>
public sealed record ProxySettings
{
    public const string EnvironmentVariablePrefix = "PROXY_";

    /// <summary>Directory of route files. In the container this is the ConfigMap mount (spec §4.3).</summary>
    public string RoutesDirectory { get; init; } = "config/routes";

    /// <summary>The environment file carrying the destination allowlist. Required.</summary>
    public string? EnvironmentFile { get; init; }

    /// <summary>Address the listeners bind to. Any address in the container; loopback in tests.</summary>
    public string ListenAddress { get; init; } = "*";

    public int DataPlanePort { get; init; } = Ports.DataPlane;

    public int StatusPort { get; init; } = Ports.Status;

    public int OpsPort { get; init; } = Ports.Ops;

    public static ProxySettings Load(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables(EnvironmentVariablePrefix)
            .AddCommandLine(args)
            .Build();

        return new ProxySettings
        {
            RoutesDirectory = configuration["Config:RoutesDirectory"] ?? "config/routes",
            EnvironmentFile = configuration["Config:EnvironmentFile"],
            ListenAddress = configuration["Listen:Address"] ?? "*",
            DataPlanePort = configuration.GetValue("Ports:DataPlane", Ports.DataPlane),
            StatusPort = configuration.GetValue("Ports:Status", Ports.Status),
            OpsPort = configuration.GetValue("Ports:Ops", Ports.Ops),
        };
    }

    public string Url(int port) => $"http://{ListenAddress}:{port}";
}

/// <summary>The three listeners (spec §2). One surface per port, one port per surface.</summary>
public static class Ports
{
    /// <summary>Proxy data plane. Public, via the ALB.</summary>
    public const int DataPlane = 8080;

    /// <summary>Read-only status UI. Internal ClusterIP Service only — never on the public ALB target group.</summary>
    public const int Status = 8081;

    /// <summary>Health, readiness, and Prometheus metrics. Cluster-internal.</summary>
    public const int Ops = 8082;
}
