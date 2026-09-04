using Proxy.Host;

namespace Proxy.Integration.Tests;

/// <summary>
/// Runs the real composition root (<see cref="ProxyProcess"/>) on loopback with random ports, from a
/// temporary route directory. Tests go through real Kestrel listeners because the port topology of
/// spec §2 is itself a security control.
/// </summary>
public sealed class ProxyFixture : IAsyncDisposable
{
    private readonly string _directory;
    private ProxyProcess? _proxy;

    private ProxyFixture(string directory)
    {
        _directory = directory;
    }

    public string RoutesDirectory => Path.Combine(_directory, "routes");

    public string EnvironmentFile => Path.Combine(_directory, "environment.yaml");

    public ProxyProcess Proxy => _proxy ?? throw new InvalidOperationException("not started");

    public HttpClient DataPlane => ClientFor(Proxy.DataPlaneAddress);

    public HttpClient Status => ClientFor(Proxy.StatusAddress);

    public HttpClient Ops => ClientFor(Proxy.OpsAddress);

    public static ProxyFixture Prepare(string environmentYaml, params (string FileName, string Yaml)[] routeFiles)
    {
        var directory = Path.Combine(Path.GetTempPath(), "proxy-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "routes"));

        var fixture = new ProxyFixture(directory);
        File.WriteAllText(fixture.EnvironmentFile, environmentYaml);
        foreach (var (fileName, yaml) in routeFiles)
        {
            File.WriteAllText(Path.Combine(fixture.RoutesDirectory, fileName), yaml);
        }

        return fixture;
    }

    public ProxySettings Settings => new()
    {
        RoutesDirectory = RoutesDirectory,
        EnvironmentFile = EnvironmentFile,
        ListenAddress = "127.0.0.1",
        DataPlanePort = 0,
        StatusPort = 0,
        OpsPort = 0,
    };

    public async Task<ProxyFixture> StartAsync(CancellationToken cancellationToken)
    {
        _proxy = ProxyProcess.Create(Settings);
        await _proxy.StartAsync(cancellationToken);
        return this;
    }

    public async ValueTask DisposeAsync()
    {
        if (_proxy is not null)
        {
            await _proxy.StopAsync();
            await _proxy.DisposeAsync();
        }

        Directory.Delete(_directory, recursive: true);
    }

    private static HttpClient ClientFor(string address) => new() { BaseAddress = new Uri(address) };

    public const string LoopbackEnvironment = """
        environment: test
        destinationAllowlist:
          dnsSuffixes: [.svc.cluster.local]
          cidrs: [127.0.0.0/8]
        """;

    public const string ClusterOnlyEnvironment = """
        environment: test
        destinationAllowlist:
          dnsSuffixes: [.svc.cluster.local]
          cidrs: []
        """;

    public static string AnonymousRoute(string routeId, string host, Uri upstream) => $$"""
        application: {{routeId}}
        owner: test-team
        routes:
          - routeId: {{routeId}}
            match:
              hosts: [{{host}}]
              path: "{**catch-all}"
            clusterId: {{routeId}}-svc
            authorizationPolicy: anonymous
        clusters:
          - clusterId: {{routeId}}-svc
            destinations:
              primary:
                address: {{upstream}}
        """;
}
