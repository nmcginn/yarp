using Proxy.Config.Model;

namespace Proxy.Core;

/// <summary>
/// The headers this proxy uses to assert identity to upstreams (spec §6.2). Any header in
/// <see cref="All"/> is stripped from every proxied request before anything else happens.
/// </summary>
public static class IdentityHeaders
{
    public const string UserId = "X-Auth-User-Id";
    public const string Email = "X-Auth-Email";
    public const string Groups = "X-Auth-Groups";
    public const string DisplayName = "X-Auth-Display-Name";

    public static readonly IReadOnlyList<string> All = [UserId, Email, Groups, DisplayName];

    public static string HeaderName(IdentityHeader header) => header switch
    {
        IdentityHeader.UserId => UserId,
        IdentityHeader.Email => Email,
        IdentityHeader.Groups => Groups,
        IdentityHeader.DisplayName => DisplayName,
        _ => throw new ArgumentOutOfRangeException(nameof(header), header, "unknown identity header"),
    };
}
