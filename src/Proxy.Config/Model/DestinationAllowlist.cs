using System.Net;

namespace Proxy.Config.Model;

/// <summary>
/// Security control (spec §4.4): cluster destination addresses must fall inside this allowlist of
/// internal DNS suffixes and CIDRs. Without it, a merged route file is an SSRF and data-exfiltration
/// primitive. Enforced by <c>tools/ConfigValidator</c> in CI and again at application startup.
/// </summary>
public sealed class DestinationAllowlist
{
    public static readonly DestinationAllowlist Empty = new([], []);

    /// <summary>
    /// Host name entries. An entry starting with '.' matches any host ending in that suffix
    /// (<c>.svc.cluster.local</c> matches <c>a.b.svc.cluster.local</c> but not <c>svc.cluster.local</c>).
    /// An entry without a leading '.' matches that exact host name (<c>localhost</c>).
    /// </summary>
    public IReadOnlyList<string> DnsSuffixes { get; }

    /// <summary>CIDR blocks that IP-literal destinations must fall inside.</summary>
    public IReadOnlyList<IPNetwork> Cidrs { get; }

    public DestinationAllowlist(IReadOnlyList<string> dnsSuffixes, IReadOnlyList<IPNetwork> cidrs)
    {
        DnsSuffixes = dnsSuffixes.Select(s => s.ToLowerInvariant()).ToArray();
        Cidrs = cidrs;
    }

    /// <summary>
    /// Returns null when the address is allowed, otherwise a human-readable reason it is not.
    /// </summary>
    public string? Reject(Uri address)
    {
        if (!address.IsAbsoluteUri)
        {
            return "address must be absolute (for example http://service.namespace.svc.cluster.local:8080/)";
        }

        if (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps)
        {
            return $"scheme '{address.Scheme}' is not allowed; use http or https";
        }

        if (!string.IsNullOrEmpty(address.UserInfo))
        {
            return "address must not contain user info";
        }

        if (address.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            var ip = IPAddress.Parse(address.IdnHost);
            if (Cidrs.Any(c => c.Contains(ip)))
            {
                return null;
            }

            return $"IP address {ip} is outside the allowed CIDRs ({Describe(Cidrs.Select(c => c.ToString()))})";
        }

        var host = address.IdnHost.ToLowerInvariant();
        foreach (var entry in DnsSuffixes)
        {
            if (entry.StartsWith('.'))
            {
                if (host.Length > entry.Length && host.EndsWith(entry, StringComparison.Ordinal))
                {
                    return null;
                }
            }
            else if (host == entry)
            {
                return null;
            }
        }

        return $"host '{host}' is outside the allowed DNS suffixes ({Describe(DnsSuffixes)})";
    }

    private static string Describe(IEnumerable<string> entries)
    {
        var list = string.Join(", ", entries);
        return list.Length == 0 ? "none configured" : list;
    }
}
