using System.Net;

namespace Proxy.Integration.Tests;

/// <summary>
/// One surface per port (spec §2, security control 7). The status listener must never be able to
/// serve proxy routes, and the data plane must never serve operational endpoints.
/// </summary>
public class ListenerIsolationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_endpoints_answer_only_on_the_ops_port()
    {
        await using var upstream = await StubUpstream.StartAsync();
        await using var fixture = await ProxyFixture.Prepare(
            ProxyFixture.LoopbackEnvironment,
            ("echo.yaml", ProxyFixture.AnonymousRoute("echo", "echo.test", upstream.Address))).StartAsync(Ct);

        var live = await fixture.Ops.GetAsync("/health/live", Ct);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("Healthy", await live.Content.ReadAsStringAsync(Ct));

        var ready = await fixture.Ops.GetAsync("/health/ready", Ct);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.NotFound, (await fixture.DataPlane.GetAsync("/health/live", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.DataPlane.GetAsync("/health/ready", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Status.GetAsync("/health/live", Ct)).StatusCode);
    }

    [Fact]
    public async Task Proxy_routes_answer_only_on_the_data_plane_port()
    {
        await using var upstream = await StubUpstream.StartAsync();
        await using var fixture = await ProxyFixture.Prepare(
            ProxyFixture.LoopbackEnvironment,
            ("echo.yaml", ProxyFixture.AnonymousRoute("echo", "echo.test", upstream.Address))).StartAsync(Ct);

        foreach (var client in new[] { fixture.Status, fixture.Ops })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/hello");
            request.Headers.Host = "echo.test";
            var response = await client.SendAsync(request, Ct);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        Assert.Empty(upstream.Received);
    }

    [Fact]
    public async Task Three_distinct_listeners_are_bound()
    {
        await using var fixture = await ProxyFixture.Prepare(
            ProxyFixture.LoopbackEnvironment,
            ("echo.yaml", ProxyFixture.AnonymousRoute("echo", "echo.test", new Uri("http://127.0.0.1:9/")))).StartAsync(Ct);

        var addresses = new[] { fixture.Proxy.DataPlaneAddress, fixture.Proxy.StatusAddress, fixture.Proxy.OpsAddress };

        Assert.Equal(3, addresses.Distinct().Count());
        Assert.All(addresses, a => Assert.StartsWith("http://127.0.0.1:", a, StringComparison.Ordinal));
    }
}
