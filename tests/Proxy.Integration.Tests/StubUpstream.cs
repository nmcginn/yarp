using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Proxy.Integration.Tests;

/// <summary>
/// An upstream that records what it receives. Tests assert on what the proxy actually sent, which
/// is the only thing that matters for the header-stripping control (spec §6.2).
/// </summary>
public sealed class StubUpstream : IAsyncDisposable
{
    private readonly WebApplication _app;

    private StubUpstream(WebApplication app)
    {
        _app = app;
    }

    public Uri Address => new(_app.Urls.Single());

    public List<ReceivedRequest> Received { get; } = [];

    public static async Task<StubUpstream> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        var stub = new StubUpstream(app);

        app.Map("/{**path}", (HttpContext context) =>
        {
            var received = new ReceivedRequest(
                context.Request.Path + context.Request.QueryString,
                context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase));
            stub.Received.Add(received);
            return Results.Json(received, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        });

        await app.StartAsync();
        return stub;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    public sealed record ReceivedRequest(string Path, Dictionary<string, string> Headers);
}
