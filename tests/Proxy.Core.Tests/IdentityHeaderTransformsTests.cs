using Proxy.Core;

namespace Proxy.Core.Tests;

public class IdentityHeaderTransformsTests
{
    [Fact]
    public void Strips_every_identity_header_and_nothing_else()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://upstream/");
        foreach (var header in IdentityHeaders.All)
        {
            request.Headers.TryAddWithoutValidation(header, "client-supplied");
        }

        request.Headers.TryAddWithoutValidation("x-auth-user-id", "lowercase-variant");
        request.Headers.TryAddWithoutValidation("X-Custom", "keep");

        IdentityHeaderTransforms.StripIdentityHeaders(request);

        foreach (var header in IdentityHeaders.All)
        {
            Assert.False(request.Headers.Contains(header), $"{header} should have been stripped");
        }

        Assert.Equal("keep", Assert.Single(request.Headers.GetValues("X-Custom")));
    }

    [Fact]
    public void All_covers_every_identity_header_enum_value()
    {
        var fromEnum = Enum.GetValues<Proxy.Config.Model.IdentityHeader>().Select(IdentityHeaders.HeaderName).ToHashSet();

        Assert.Equal(fromEnum, IdentityHeaders.All.ToHashSet());
    }
}
