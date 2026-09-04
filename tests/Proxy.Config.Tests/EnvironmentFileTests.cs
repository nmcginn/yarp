namespace Proxy.Config.Tests;

public class EnvironmentFileTests
{
    [Fact]
    public void Valid_environment_file_loads()
    {
        var environment = EnvironmentFileParser.Load(Fixture.EnvironmentFile("valid.yaml"));

        Assert.Equal("test", environment.Name);
        Assert.Equal([".svc.cluster.local", "localhost"], environment.DestinationAllowlist.DnsSuffixes);
        Assert.Equal(2, environment.DestinationAllowlist.Cidrs.Count);
        Assert.Null(environment.DestinationAllowlist.Reject(new Uri("http://10.2.3.4/")));
        Assert.NotNull(environment.DestinationAllowlist.Reject(new Uri("http://11.2.3.4/")));
    }

    [Fact]
    public void Invalid_cidr_is_rejected()
    {
        var ex = Assert.Throws<ConfigException>(() => EnvironmentFileParser.Load(Fixture.EnvironmentFile("bad-cidr.yaml")));

        var error = Assert.Single(ex.Errors);
        Assert.Equal(4, error.Location.Line);
        Assert.Equal("cidrs entry '10.0.0.0/33' is not a valid CIDR such as 10.0.0.0/8", error.Message);
    }

    [Fact]
    public void Sections_that_do_not_exist_yet_are_rejected()
    {
        var ex = Assert.Throws<ConfigException>(() => EnvironmentFileParser.Load(Fixture.EnvironmentFile("unknown-section.yaml")));

        var error = Assert.Single(ex.Errors);
        Assert.Equal(4, error.Location.Line);
        Assert.StartsWith("unknown field 'redis'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_file_is_a_config_error()
    {
        var ex = Assert.Throws<ConfigException>(() => EnvironmentFileParser.Load(Fixture.EnvironmentFile("nope.yaml")));

        Assert.Contains("cannot read environment file", Assert.Single(ex.Errors).Message, StringComparison.Ordinal);
    }

    /// <summary>The checked-in environment files must themselves be valid.</summary>
    [Theory]
    [InlineData("dev.yaml")]
    [InlineData("prod.yaml")]
    [InlineData("local.yaml")]
    public void Repository_environment_files_are_valid(string name)
    {
        var path = Path.Combine(RepositoryRoot(), "config", "environments", name);

        var environment = EnvironmentFileParser.Load(path);

        Assert.Equal(Path.GetFileNameWithoutExtension(name), environment.Name);
        Assert.Null(environment.DestinationAllowlist.Reject(new Uri("http://app.ns.svc.cluster.local:8080/")));
        Assert.NotNull(environment.DestinationAllowlist.Reject(new Uri("http://example.com/")));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Proxy.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
