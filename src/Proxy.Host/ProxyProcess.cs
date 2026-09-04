using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Proxy.Config;
using Proxy.Config.Model;
using Proxy.Core;
using Proxy.Observability;
using Serilog;

namespace Proxy.Host;

/// <summary>
/// One process, three hosts (ADR 0006). Each listener of spec §2 is its own ASP.NET Core
/// application with its own pipeline, so an endpoint can only ever be reached on the port it was
/// mapped on. The status UI on 8081 cannot serve proxy routes; the data plane on 8080 cannot serve
/// health checks. Nothing has to be filtered by port because nothing is shared by port.
/// </summary>
public sealed class ProxyProcess : IAsyncDisposable
{
    private readonly WebApplication _dataPlane;
    private readonly WebApplication _status;
    private readonly WebApplication _ops;

    private ProxyProcess(WebApplication dataPlane, WebApplication status, WebApplication ops)
    {
        _dataPlane = dataPlane;
        _status = status;
        _ops = ops;
    }

    public RouteSet RouteSet { get; private init; } = null!;

    /// <summary>Addresses actually bound, useful when a port was 0 (tests).</summary>
    public string DataPlaneAddress => BoundAddress(_dataPlane);

    public string StatusAddress => BoundAddress(_status);

    public string OpsAddress => BoundAddress(_ops);

    /// <summary>
    /// Loads configuration and builds the hosts. Throws <see cref="ConfigException"/> when the
    /// configuration is invalid: the pod must not start with invalid config (spec §4.4).
    /// </summary>
    public static ProxyProcess Create(ProxySettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.EnvironmentFile))
        {
            throw new InvalidOperationException(
                $"Config:EnvironmentFile is required (set {ProxySettings.EnvironmentVariablePrefix}Config__EnvironmentFile " +
                "or pass --Config:EnvironmentFile=config/environments/local.yaml).");
        }

        var environment = EnvironmentFileParser.Load(settings.EnvironmentFile);
        var routeSet = RouteConfigLoader.Load(settings.RoutesDirectory, environment.DestinationAllowlist);

        // Phase 2: no authentication exists yet (Phase 3). An authenticated route must never be served
        // anonymously, so a configuration that needs authentication refuses to start rather than
        // silently degrading. Remove this guard when Phase 3 registers the authentication scheme.
        var needsAuth = routeSet.Routes.Where(r => r.AuthorizationPolicy == AuthorizationPolicy.Authenticated).ToList();
        if (needsAuth.Count > 0)
        {
            var errors = needsAuth
                .Select(r => new ConfigError(r.Location,
                    $"route '{r.RouteId}' requires authentication, but this build has no authentication (Phase 3); " +
                    "only anonymous routes can be served"))
                .ToList();
            throw new ConfigException(errors);
        }

        Log.Information(
            "Configuration loaded: {RouteCount} routes and {ClusterCount} clusters from {FileCount} files in {RoutesDirectory} (environment {Environment})",
            routeSet.Routes.Count, routeSet.Clusters.Count, routeSet.Files.Count, settings.RoutesDirectory, environment.Name);

        return new ProxyProcess(BuildDataPlane(settings, routeSet), BuildStatus(settings), BuildOps(settings, routeSet))
        {
            RouteSet = routeSet,
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(
            _dataPlane.StartAsync(cancellationToken),
            _status.StartAsync(cancellationToken),
            _ops.StartAsync(cancellationToken));
    }

    /// <summary>Runs until any host is asked to shut down (SIGTERM, Ctrl+C), then stops all of them.</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken);
        await Task.WhenAny(
            _dataPlane.WaitForShutdownAsync(cancellationToken),
            _status.WaitForShutdownAsync(cancellationToken),
            _ops.WaitForShutdownAsync(cancellationToken));
        await StopAsync();
    }

    public async Task StopAsync()
    {
        await Task.WhenAll(_dataPlane.StopAsync(), _status.StopAsync(), _ops.StopAsync());
    }

    public async ValueTask DisposeAsync()
    {
        await _dataPlane.DisposeAsync();
        await _status.DisposeAsync();
        await _ops.DisposeAsync();
    }

    private static WebApplication BuildDataPlane(ProxySettings settings, RouteSet routeSet)
    {
        var builder = CreateBuilder(settings.Url(settings.DataPlanePort));
        builder.Services.AddProxyDataPlane(routeSet);

        var app = builder.Build();
        app.UseProxyDataPlane();
        return app;
    }

    private static WebApplication BuildStatus(ProxySettings settings)
    {
        // Phase 5 maps the read-only status endpoints and Razor Pages here (spec §7). Until then the
        // listener exists, answers 404 to everything, and is already bound to the internal Service.
        var builder = CreateBuilder(settings.Url(settings.StatusPort));
        return builder.Build();
    }

    private static WebApplication BuildOps(ProxySettings settings, RouteSet routeSet)
    {
        var builder = CreateBuilder(settings.Url(settings.OpsPort));
        builder.Services.AddOpsHealthChecks(routeSet.Routes.Count, routeSet.Clusters.Count);

        var app = builder.Build();
        app.MapOpsEndpoints();
        return app;
    }

    private static WebApplicationBuilder CreateBuilder(string url)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(url);
        builder.Host.UseSerilog();
        return builder;
    }

    private static string BoundAddress(WebApplication app) =>
        app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
        ?? throw new InvalidOperationException("host is not started");
}
