using System.Net;
using Proxy.Config.Model;

namespace Proxy.Config.Tests;

public class DestinationAllowlistTests
{
    private static readonly DestinationAllowlist Allowlist = new(
        [".svc.cluster.local", "localhost"],
        [IPNetwork.Parse("10.0.0.0/8"), IPNetwork.Parse("::1/128")]);

    [Theory]
    [InlineData("http://hr-portal.hr.svc.cluster.local:8080/")]
    [InlineData("https://HR-PORTAL.hr.SVC.cluster.LOCAL/")]
    [InlineData("http://a.b.c.d.svc.cluster.local/prefix/")]
    [InlineData("http://localhost:5000/")]
    [InlineData("http://10.1.2.3:8080/")]
    [InlineData("http://[::1]:8080/")]
    public void Allows_internal_addresses(string address)
    {
        Assert.Null(Allowlist.Reject(new Uri(address)));
    }

    [Theory]
    [InlineData("http://svc.cluster.local/", "outside the allowed DNS suffixes")]
    [InlineData("http://evil.example.com/", "outside the allowed DNS suffixes")]
    [InlineData("http://hr.svc.cluster.local.evil.example.com/", "outside the allowed DNS suffixes")]
    [InlineData("http://notlocalhost/", "outside the allowed DNS suffixes")]
    [InlineData("http://169.254.169.254/latest/meta-data/", "outside the allowed CIDRs")]
    [InlineData("http://11.0.0.1/", "outside the allowed CIDRs")]
    [InlineData("http://[fe80::1]/", "outside the allowed CIDRs")]
    [InlineData("ftp://hr.hr.svc.cluster.local/", "scheme 'ftp' is not allowed")]
    [InlineData("http://user:pass@hr.hr.svc.cluster.local/", "must not contain user info")]
    public void Rejects_everything_else(string address, string reason)
    {
        var rejection = Allowlist.Reject(new Uri(address));

        Assert.NotNull(rejection);
        Assert.Contains(reason, rejection, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_allowlist_rejects_everything()
    {
        Assert.NotNull(DestinationAllowlist.Empty.Reject(new Uri("http://hr.hr.svc.cluster.local/")));
        Assert.NotNull(DestinationAllowlist.Empty.Reject(new Uri("http://10.0.0.1/")));
    }
}
