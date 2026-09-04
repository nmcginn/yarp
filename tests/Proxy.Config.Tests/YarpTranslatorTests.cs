namespace Proxy.Config.Tests;

public class YarpTranslatorTests
{
    [Fact]
    public void Translates_routes_with_policy_and_metadata()
    {
        var set = RouteConfigLoader.Load(Fixture.Dir("valid"), Fixture.ClusterOnly);

        var (routes, clusters) = YarpTranslator.Translate(set);

        Assert.Equal(3, routes.Count);
        Assert.Equal(2, clusters.Count);

        var hr = Assert.Single(routes, r => r.RouteId == "hr-portal");
        Assert.Equal("hr-portal-svc", hr.ClusterId);
        Assert.Equal(["hr.corp.example.com"], hr.Match.Hosts);
        Assert.Equal("{**catch-all}", hr.Match.Path);
        Assert.Equal(YarpTranslator.AuthorizationPolicies.Authenticated, hr.AuthorizationPolicy);
        Assert.NotNull(hr.Metadata);
        Assert.Equal("hr-portal", hr.Metadata[YarpTranslator.Metadata.Application]);
        Assert.Equal("hr-platform-team", hr.Metadata[YarpTranslator.Metadata.Owner]);
        Assert.Equal("hr-portal.yaml", hr.Metadata[YarpTranslator.Metadata.SourceFile]);
        Assert.Equal("detailed", hr.Metadata[YarpTranslator.Metadata.AuditLevel]);
        Assert.Equal("UserId,Email,Groups,DisplayName", hr.Metadata[YarpTranslator.Metadata.IdentityHeaders]);
        Assert.Equal("hr-users,hr-admins", hr.Metadata[YarpTranslator.Metadata.RequiredGroups]);

        var docs = Assert.Single(routes, r => r.RouteId == "public-docs");
        Assert.Equal(YarpTranslator.AuthorizationPolicies.Anonymous, docs.AuthorizationPolicy);
        Assert.Equal(["docs.corp.example.com", "*.docs.corp.example.com"], docs.Match.Hosts);
    }

    [Fact]
    public void Translates_clusters_with_destinations_and_active_health_checks()
    {
        var set = RouteConfigLoader.Load(Fixture.Dir("valid"), Fixture.ClusterOnly);

        var (_, clusters) = YarpTranslator.Translate(set);

        var hr = Assert.Single(clusters, c => c.ClusterId == "hr-portal-svc");
        Assert.NotNull(hr.Destinations);
        Assert.Equal("http://hr-portal.hr.svc.cluster.local:8080/", Assert.Single(hr.Destinations).Value.Address);
        Assert.NotNull(hr.HealthCheck?.Active);
        Assert.True(hr.HealthCheck.Active.Enabled);
        Assert.Equal("/health", hr.HealthCheck.Active.Path);
        Assert.Equal(TimeSpan.FromSeconds(10), hr.HealthCheck.Active.Interval);
        Assert.Equal("ConsecutiveFailures", hr.HealthCheck.Active.Policy);

        var docs = Assert.Single(clusters, c => c.ClusterId == "public-docs-svc");
        Assert.NotNull(docs.Destinations);
        Assert.Equal(2, docs.Destinations.Count);
        Assert.Null(docs.HealthCheck);
    }
}
