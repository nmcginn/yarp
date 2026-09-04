using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Proxy.Core;

namespace Proxy.Core.Tests;

public class CorrelationIdMiddlewareTests
{
    [Theory]
    [InlineData("req-123")]
    [InlineData("01J8ZK0Q4M3XN5Y7W9V2B6C8DA")]
    [InlineData("4bf92f3577b34da6a3ce929d0e0e4736")]
    public void Keeps_a_well_formed_inbound_id(string inbound)
    {
        Assert.Equal(inbound, CorrelationIdMiddleware.ResolveCorrelationId(new StringValues(inbound)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has spaces")]
    [InlineData("new\nline")]
    [InlineData("<script>")]
    public void Replaces_a_malformed_inbound_id(string inbound)
    {
        var resolved = CorrelationIdMiddleware.ResolveCorrelationId(new StringValues(inbound));

        Assert.NotEqual(inbound, resolved);
        Assert.Equal(32, resolved.Length);
    }

    [Fact]
    public void Replaces_an_over_long_id_and_multiple_values()
    {
        Assert.Equal(32, CorrelationIdMiddleware.ResolveCorrelationId(new StringValues(new string('a', 129))).Length);
        Assert.Equal(32, CorrelationIdMiddleware.ResolveCorrelationId(new StringValues(["a", "b"])).Length);
        Assert.Equal(32, CorrelationIdMiddleware.ResolveCorrelationId(StringValues.Empty).Length);
    }

    [Fact]
    public async Task Sets_trace_identifier_and_response_header()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "abc-123";
        string? seenByNext = null;

        var middleware = new CorrelationIdMiddleware(
            ctx =>
            {
                seenByNext = ctx.TraceIdentifier;
                return Task.CompletedTask;
            },
            NullLogger<CorrelationIdMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal("abc-123", seenByNext);
        Assert.Equal("abc-123", context.Response.Headers[CorrelationIdMiddleware.HeaderName]);
    }
}
