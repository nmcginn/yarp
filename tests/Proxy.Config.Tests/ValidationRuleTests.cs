using Proxy.Config.Model;

namespace Proxy.Config.Tests;

/// <summary>
/// Every validation rule in spec §4.4, each with a deliberately broken fixture. Assertions cover the
/// message, the file, and the line: the validator is the primary feedback surface for app teams.
/// </summary>
public class ValidationRuleTests
{
    [Fact]
    public void Valid_fixture_loads_and_ignores_non_yaml_files()
    {
        var set = RouteConfigLoader.Load(Fixture.Dir("valid"), Fixture.ClusterOnly);

        Assert.Equal(2, set.Files.Count);
        Assert.Equal(3, set.Routes.Count);
        Assert.Equal(2, set.Clusters.Count);

        var hr = Assert.Single(set.Routes, r => r.RouteId == "hr-portal");
        Assert.Equal(AuthorizationPolicy.Authenticated, hr.AuthorizationPolicy);
        Assert.Equal(["hr-users", "hr-admins"], hr.RequiredGroups);
        Assert.Equal([IdentityHeader.UserId, IdentityHeader.Email, IdentityHeader.Groups, IdentityHeader.DisplayName], hr.IdentityHeaders);
        Assert.Equal(AuditLevel.Detailed, hr.AuditLevel);
        Assert.False(hr.CachingEnabled);
        Assert.Equal(4, hr.Location.Line);
        Assert.EndsWith("hr-portal.yaml", hr.Location.File, StringComparison.Ordinal);

        var hrCluster = Assert.Single(set.Clusters, c => c.ClusterId == "hr-portal-svc");
        Assert.Equal(new Uri("http://hr-portal.hr.svc.cluster.local:8080/"), hrCluster.Destinations["primary"].Address);
        Assert.Equal(new ActiveHealthCheck(true, "/health", TimeSpan.FromSeconds(10)), hrCluster.ActiveHealthCheck);

        var docs = Assert.Single(set.Routes, r => r.RouteId == "public-docs");
        Assert.True(docs.CachingEnabled);
        Assert.Equal(["docs.corp.example.com", "*.docs.corp.example.com"], docs.Match.Hosts);
        Assert.Empty(docs.RequiredGroups);
        Assert.Empty(docs.IdentityHeaders);
        Assert.Equal(AuditLevel.Standard, docs.AuditLevel);
    }

    [Fact]
    public void Duplicate_routeId_and_clusterId_across_files_are_rejected()
    {
        var errors = Fixture.Errors("duplicate-ids");

        var route = Fixture.Single(errors, "duplicate routeId 'shared'");
        Assert.EndsWith("b.yaml", route.Location.File, StringComparison.Ordinal);
        Assert.Equal(4, route.Location.Line);
        Assert.Contains("a.yaml:4", route.Message, StringComparison.Ordinal);

        var cluster = Fixture.Single(errors, "duplicate clusterId 'shared-svc'");
        Assert.EndsWith("b.yaml", cluster.Location.File, StringComparison.Ordinal);
        Assert.Equal(11, cluster.Location.Line);
        Assert.Contains("a.yaml:11", cluster.Message, StringComparison.Ordinal);

        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void Route_referencing_nonexistent_cluster_is_rejected()
    {
        var errors = Fixture.Errors("unknown-cluster");

        var error = Assert.Single(errors);
        Assert.Equal("route 'app' references unknown clusterId 'does-not-exist'", error.Message);
        Assert.Equal(4, error.Location.Line);
    }

    [Fact]
    public void Two_routes_matching_the_same_host_and_path_are_rejected()
    {
        var errors = Fixture.Errors("host-path-collision");

        var error = Assert.Single(errors);
        Assert.EndsWith("second.yaml", error.Location.File, StringComparison.Ordinal);
        Assert.Equal(4, error.Location.Line);
        Assert.Contains("route 'second' matches the same host and path as route 'first'", error.Message, StringComparison.Ordinal);
        Assert.Contains("first.yaml:4", error.Message, StringComparison.Ordinal);
        Assert.Contains("host 'shared.corp.example.com'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Destination_outside_the_internal_allowlist_is_rejected()
    {
        // Security control (spec §4.4). A host that merely *contains* the suffix must not pass.
        var errors = Fixture.Errors("external-destination");

        Assert.Equal(2, errors.Count);

        var dns = Fixture.Single(errors, "destination 'primary' of cluster 'exfil-svc'");
        Assert.Equal(14, dns.Location.Line);
        Assert.Contains("host 'exfil.svc.cluster.local.attacker.example.com' is outside the allowed DNS suffixes (.svc.cluster.local)", dns.Message, StringComparison.Ordinal);

        var ip = Fixture.Single(errors, "destination 'secondary' of cluster 'exfil-svc'");
        Assert.Equal(16, ip.Location.Line);
        Assert.Contains("IP address 203.0.113.10 is outside the allowed CIDRs (none configured)", ip.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_fixture_passes_when_the_allowlist_permits_it()
    {
        var permissive = new DestinationAllowlist([".attacker.example.com"], [System.Net.IPNetwork.Parse("203.0.113.0/24")]);

        var set = RouteConfigLoader.Load(Fixture.Dir("external-destination"), permissive);

        Assert.Single(set.Routes);
    }

    [Fact]
    public void Malformed_host_patterns_are_rejected()
    {
        var errors = Fixture.Errors("malformed-host");

        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Equal(6, e.Location.Line));
        Fixture.Single(errors, "malformed host pattern 'https://app.corp.example.com/path'");
        Fixture.Single(errors, "malformed host pattern 'app.corp.example.com:8080'");
    }

    [Fact]
    public void Malformed_path_patterns_are_rejected()
    {
        var errors = Fixture.Errors("malformed-path");

        var error = Assert.Single(errors);
        Assert.Equal(7, error.Location.Line);
        Assert.StartsWith("malformed path pattern '/api/{**rest}/{id}'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unparseable_durations_are_rejected()
    {
        var errors = Fixture.Errors("bad-duration");

        var error = Assert.Single(errors);
        Assert.Equal(19, error.Location.Line);
        Assert.Equal("'clusters[0].healthCheck.active.interval' is not a valid duration; expected HH:MM:SS, got 'ten seconds'", error.Message);
    }

    [Fact]
    public void Unknown_enum_values_are_rejected_with_the_valid_values_listed()
    {
        var errors = Fixture.Errors("unknown-enum");

        Assert.Equal(3, errors.Count);

        var policy = Fixture.Single(errors, "unknown routes[0].authorizationPolicy 'sometimes'");
        Assert.Equal(9, policy.Location.Line);
        Assert.EndsWith("expected one of: authenticated, anonymous", policy.Message, StringComparison.Ordinal);

        var header = Fixture.Single(errors, "unknown routes[0].identityHeaders 'shoeSize'");
        Assert.Equal(10, header.Location.Line);
        Assert.EndsWith("expected one of: userId, email, groups, displayName", header.Message, StringComparison.Ordinal);

        var audit = Fixture.Single(errors, "unknown routes[0].auditLevel 'loud'");
        Assert.Equal(11, audit.Location.Line);
        Assert.EndsWith("expected one of: standard, detailed", audit.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Caching_on_a_non_anonymous_route_is_rejected()
    {
        // Security control (spec §6.3): cross-user cache leakage is the worst bug this system can produce.
        var errors = Fixture.Errors("caching-on-authenticated");

        var error = Assert.Single(errors);
        Assert.Equal(4, error.Location.Line);
        Assert.Equal(
            "route 'app' enables caching but its authorizationPolicy is 'authenticated'; caching is permitted only on anonymous routes (spec §6.3)",
            error.Message);
    }

    [Fact]
    public void Fields_outside_the_constrained_schema_are_rejected()
    {
        // ADR 0005: raw YARP options must never pass through.
        var errors = Fixture.Errors("unknown-field");

        Assert.Equal(2, errors.Count);

        var transforms = Fixture.Single(errors, "unknown field 'transforms' in routes[0]");
        Assert.Equal(10, transforms.Location.Line);
        Assert.Contains("allowed fields: routeId, match, clusterId", transforms.Message, StringComparison.Ordinal);

        var lb = Fixture.Single(errors, "unknown field 'loadBalancingPolicy' in clusters[0]");
        Assert.Equal(17, lb.Location.Line);
    }

    [Fact]
    public void Missing_required_fields_are_each_reported()
    {
        var errors = Fixture.Errors("missing-required");

        Fixture.Single(errors, "'owner' is required");
        Fixture.Single(errors, "'routes[0].match.path' is required");
        Fixture.Single(errors, "'routes[0].clusterId' is required");
        var address = Fixture.Single(errors, "'clusters[0].destinations.primary.address' is required");
        Assert.Equal(10, address.Location.Line);
    }

    [Fact]
    public void Application_must_match_the_file_name()
    {
        var errors = Fixture.Errors("application-mismatch");

        var error = Assert.Single(errors);
        Assert.Equal(1, error.Location.Line);
        Assert.Equal("'application' is 'finance' but the file is named 'hr-portal.yaml'; the file name must match the application name", error.Message);
    }

    [Fact]
    public void Invalid_yaml_reports_the_line()
    {
        var errors = Fixture.Errors("invalid-yaml");

        var error = Assert.Single(errors);
        Assert.StartsWith("invalid YAML", error.Message, StringComparison.Ordinal);
        Assert.InRange(error.Location.Line, 5, 8);
    }

    [Fact]
    public void Missing_directory_is_an_error_not_an_empty_route_table()
    {
        var errors = Fixture.Errors("does-not-exist");

        var error = Assert.Single(errors);
        Assert.Equal("routes directory does not exist", error.Message);
    }

    [Fact]
    public void ConfigException_message_lists_every_error_with_location()
    {
        var ex = Assert.Throws<ConfigException>(() => RouteConfigLoader.Load(Fixture.Dir("unknown-enum"), Fixture.ClusterOnly));

        Assert.StartsWith("Configuration is invalid (3 errors):", ex.Message, StringComparison.Ordinal);
        Assert.Contains("app.yaml:9: unknown routes[0].authorizationPolicy", ex.Message, StringComparison.Ordinal);
    }
}
