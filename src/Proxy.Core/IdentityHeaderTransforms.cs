using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Proxy.Core;

/// <summary>
/// Security control (spec §6.2): strips every identity header from every proxied request —
/// authenticated and anonymous alike — before any conditional logic. If an upstream trusts
/// <c>X-Auth-User-Id</c> and a client can set it, that is an authentication bypass.
///
/// Identity header <em>enrichment</em> arrives in Phase 4, behind an interface so signed-assertion
/// mode can be added per route later. It goes after the strip in this same transform, never before.
/// </summary>
public sealed class IdentityHeaderTransforms : ITransformProvider
{
    public void ValidateRoute(TransformRouteValidationContext context)
    {
    }

    public void ValidateCluster(TransformClusterValidationContext context)
    {
    }

    public void Apply(TransformBuilderContext context)
    {
        context.AddRequestTransform(transformContext =>
        {
            // Security control (spec §6.2). Strip unconditionally, before anything else, on every
            // request. No early return may be added above this loop.
            StripIdentityHeaders(transformContext.ProxyRequest);
            return default;
        });
    }

    public static void StripIdentityHeaders(HttpRequestMessage proxyRequest)
    {
        foreach (var header in IdentityHeaders.All)
        {
            proxyRequest.Headers.Remove(header);
        }
    }
}
