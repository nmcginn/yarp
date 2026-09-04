using System.Net;
using Proxy.Core;

namespace Proxy.Integration.Tests;

public class DataPlaneTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Proxies_a_matching_host_to_the_upstream()
    {
        await using var upstream = await StubUpstream.StartAsync();
        await using var fixture = await ProxyFixture.Prepare(
            ProxyFixture.LoopbackEnvironment,
            ("echo.yaml", ProxyFixture.AnonymousRoute("echo", "echo.test", upstream.Address))).StartAsync(Ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/hello?x=1");
        request.Headers.Host = "echo.test";
        var response = await fixture.DataPlane.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = Assert.Single(upstream.Received);
        Assert.Equal("/hello?x=1", received.Path);
    }

    [Fact]
    public async Task Unknown_host_is_not_proxied()
    {
        await using var upstream = await StubUpstream.StartAsync();
        await using var fixture = await ProxyFixture.Prepare(
            ProxyFixture.LoopbackEnvironment,
            ("echo.yaml", ProxyFixture.AnonymousRoute("echo", "echo.test", upstream.Address))).StartAsync(Ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/hello");
        request.Headers.Host = "other.test";
        var response = await fixture.DataPlane.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(upstream.Received);
    }

    /// <summary>
    /// Security control (spec §6.2). Every identity header, client-supplied, must never reach the
    /// upstream. Phase 3 extends this test to authenticated routes; the anonymous half is the one
    /// that matters most until then, because anonymous routes reach upstreams too.
    /// </summary>
    [Fact]
    public async Task Client_supplied_identity_headers_never_reach_the_upstream()
    {
        await using var upstream = await StubUpstream.StartAsync();
        await using var fixture = await ProxyFixture.Prepare(
            ProxyFixture.LoopbackEnvironment,
            ("echo.yaml", ProxyFixture.AnonymousRoute("echo", "echo.test", upstream.Address))).StartAsync(Ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Host = "echo.test";
        foreach (var header in IdentityHeaders.All)
        {
            request.Headers.TryAddWithoutValidation(header, "injected-by-client");
        }

        request.Headers.TryAddWithoutValidation("x-auth-user-id", "injected-lowercase");
        request.Headers.TryAddWithoutValidation("X-Harmless", "kept");

        var response = await fixture.DataPlane.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = Assert.Single(upstream.Received);
        foreach (var header in IdentityHeaders.All)
        {
            Assert.False(received.Headers.ContainsKey(header), $"upstream observed client-supplied {header}");
        }

        Assert.Equal("kept", received.Headers["X-Harmless"]);
    }

    [Fact]
    public async Task Correlation_id_is_accepted_forwarded_and_echoed()
    {
        await using var upstream = await StubUpstream.StartAsync();
        await using var fixture = await ProxyFixture.Prepare(
            ProxyFixture.LoopbackEnvironment,
            ("echo.yaml", ProxyFixture.AnonymousRoute("echo", "echo.test", upstream.Address))).StartAsync(Ct);

        using var withId = new HttpRequestMessage(HttpMethod.Get, "/a");
        withId.Headers.Host = "echo.test";
        withId.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, "req-42");
        var response = await fixture.DataPlane.SendAsync(withId, Ct);

        Assert.Equal("req-42", response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
        Assert.Equal("req-42", upstream.Received.Single().Headers[CorrelationIdMiddleware.HeaderName]);

        using var withoutId = new HttpRequestMessage(HttpMethod.Get, "/b");
        withoutId.Headers.Host = "echo.test";
        response = await fixture.DataPlane.SendAsync(withoutId, Ct);

        var generated = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.Equal(32, generated.Length);
        Assert.Equal(generated, upstream.Received[1].Headers[CorrelationIdMiddleware.HeaderName]);
    }
}
