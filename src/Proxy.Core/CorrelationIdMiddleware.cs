using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Proxy.Core;

/// <summary>
/// First middleware on the data plane (spec §6.1): accepts an inbound <c>X-Request-Id</c> or
/// generates one, exposes it as <see cref="HttpContext.TraceIdentifier"/>, echoes it on the response,
/// and attaches it to the logging scope. It is the join key between application logs, audit events,
/// and upstream logs.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Request-Id";

    // Long enough for any sane id format (UUIDs, ULIDs, W3C trace ids); short enough that a client
    // cannot use the header to stuff logs.
    private const int MaxLength = 128;

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context.Request.Headers[HeaderName]);
        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }

    /// <summary>Uses the inbound value when it is a well-formed token; otherwise generates a new id.</summary>
    public static string ResolveCorrelationId(StringValues inbound)
    {
        var candidate = inbound.Count == 1 ? inbound[0] : null;
        if (!string.IsNullOrEmpty(candidate) && candidate.Length <= MaxLength && candidate.All(IsTokenChar))
        {
            return candidate;
        }

        return Guid.NewGuid().ToString("N");
    }

    private static bool IsTokenChar(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':' or '+' or '/' or '=';
}
