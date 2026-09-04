using Proxy.Config;
using Proxy.Host;

namespace Proxy.Integration.Tests;

/// <summary>
/// Invalid configuration is fatal at startup (spec §4.4, security control 6). Nothing listens, so
/// readiness cannot pass, and a rollout carrying the bad ConfigMap stalls with old pods serving.
/// </summary>
public class StartupValidationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task External_destination_is_rejected_at_startup()
    {
        // Security control (spec §4.4): the same allowlist the validator CLI enforces in CI.
        await using var fixture = ProxyFixture.Prepare(
            ProxyFixture.ClusterOnlyEnvironment,
            ("exfil.yaml", ProxyFixture.AnonymousRoute("exfil", "exfil.test", new Uri("http://attacker.example.com/"))));

        var ex = Assert.Throws<ConfigException>(() => ProxyProcess.Create(fixture.Settings));

        var error = Assert.Single(ex.Errors);
        Assert.EndsWith("exfil.yaml", error.Location.File, StringComparison.Ordinal);
        Assert.Equal(14, error.Location.Line);
        Assert.Contains("points outside the internal allowlist", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_route_file_fails_startup_rather_than_loading_the_good_routes()
    {
        var good = ProxyFixture.AnonymousRoute("good", "good.test", new Uri("http://good.ns.svc.cluster.local/"));
        var broken = ProxyFixture.AnonymousRoute("broken", "broken.test", new Uri("http://broken.ns.svc.cluster.local/"))
            .Replace("authorizationPolicy: anonymous", "authorizationPolicy: whenever", StringComparison.Ordinal);

        await using var fixture = ProxyFixture.Prepare(ProxyFixture.ClusterOnlyEnvironment, ("good.yaml", good), ("broken.yaml", broken));

        var ex = Assert.Throws<ConfigException>(() => ProxyProcess.Create(fixture.Settings));

        var error = Assert.Single(ex.Errors);
        Assert.EndsWith("broken.yaml", error.Location.File, StringComparison.Ordinal);
        Assert.Equal(9, error.Location.Line);
        Assert.StartsWith("unknown routes[0].authorizationPolicy 'whenever'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authenticated_routes_refuse_to_start_until_authentication_exists()
    {
        // Phase 2 guard: an authenticated route must never be served anonymously.
        var route = ProxyFixture.AnonymousRoute("hr", "hr.test", new Uri("http://hr.ns.svc.cluster.local/"))
            .Replace("authorizationPolicy: anonymous", "authorizationPolicy: authenticated", StringComparison.Ordinal);

        await using var fixture = ProxyFixture.Prepare(ProxyFixture.ClusterOnlyEnvironment, ("hr.yaml", route));

        var ex = Assert.Throws<ConfigException>(() => ProxyProcess.Create(fixture.Settings));

        Assert.Contains("requires authentication, but this build has no authentication (Phase 3)", Assert.Single(ex.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_environment_file_setting_is_an_explicit_error()
    {
        await using var fixture = ProxyFixture.Prepare(ProxyFixture.ClusterOnlyEnvironment);

        var ex = Assert.Throws<InvalidOperationException>(() => ProxyProcess.Create(fixture.Settings with { EnvironmentFile = null }));

        Assert.Contains("Config:EnvironmentFile is required", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_routes_directory_starts_with_no_routes()
    {
        await using var fixture = await ProxyFixture.Prepare(ProxyFixture.ClusterOnlyEnvironment).StartAsync(Ct);

        Assert.Empty(fixture.Proxy.RouteSet.Routes);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await fixture.Ops.GetAsync("/health/ready", Ct)).StatusCode);
    }
}
