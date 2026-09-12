using Linkbelli.Application.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Linkbelli.Tests;

/// <summary>
/// Refusing to start beats starting and destroying data later.
/// </summary>
public class StartupConfigurationTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Linkbelli.Api";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static (string, string)[] Complete =>
    [
        ("ConnectionStrings:Default", "Host=db;Database=linkbelli"),
        ("DataProtection:KeyRingPath", "/keys"),
    ];

    [Fact]
    public void A_local_run_needs_no_configuration_at_all()
    {
        // What makes `docker compose up` work against an empty tree, and worth keeping.
        var ex = Record.Exception(() => StartupConfiguration.ValidateOrThrow(Config(), new Env("Development")));

        Assert.Null(ex);
    }

    [Fact]
    public void A_complete_production_configuration_starts()
    {
        var ex = Record.Exception(
            () => StartupConfiguration.ValidateOrThrow(Config(Complete), new Env("Production")));

        Assert.Null(ex);
    }

    /// <summary>
    /// The one that motivated this. Without it the app runs perfectly until its first restart,
    /// then signs everybody out and makes every encrypted source secret undecryptable.
    /// </summary>
    [Fact]
    public void A_missing_key_ring_path_stops_production_starting()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => StartupConfiguration.ValidateOrThrow(
            Config(("ConnectionStrings:Default", "Host=db")), new Env("Production")));

        Assert.Contains("DataProtection:KeyRingPath", ex.Message);
        // The message has to say what goes wrong, or the reader just sets it to anything.
        Assert.Contains("undecryptable", ex.Message);
    }

    [Fact]
    public void A_missing_connection_string_stops_production_starting()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => StartupConfiguration.ValidateOrThrow(
            Config(("DataProtection:KeyRingPath", "/keys")), new Env("Production")));

        Assert.Contains("ConnectionStrings:Default", ex.Message);
    }

    [Fact]
    public void Everything_missing_is_reported_at_once()
    {
        // One restart per missing variable is a bad way to find out what they are.
        var ex = Assert.Throws<InvalidOperationException>(
            () => StartupConfiguration.ValidateOrThrow(Config(), new Env("Production")));

        Assert.Contains("ConnectionStrings:Default", ex.Message);
        Assert.Contains("DataProtection:KeyRingPath", ex.Message);
        Assert.Contains("__", ex.Message); // how to actually set them
    }

    /// <summary>Mail is optional, and stays optional.</summary>
    [Fact]
    public void No_mail_configured_is_a_valid_deployment()
    {
        var ex = Record.Exception(
            () => StartupConfiguration.ValidateOrThrow(Config(Complete), new Env("Production")));

        Assert.Null(ex);
    }

    /// <summary>
    /// But half-configured mail is not: reset links are built from PublicUrl and deliberately
    /// never from the request, so without it the message is unusable rather than merely wrong.
    /// </summary>
    [Fact]
    public void Mail_configured_without_a_public_url_stops_production_starting()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => StartupConfiguration.ValidateOrThrow(
            Config([.. Complete, ("Email:Host", "smtp.example.com")]), new Env("Production")));

        Assert.Contains("Email:PublicUrl", ex.Message);
        Assert.Contains("Email:FromAddress", ex.Message);
    }
}
