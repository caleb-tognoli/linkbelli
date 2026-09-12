using Linkbelli.Application.Identity;

namespace Linkbelli.Tests;

/// <summary>
/// A username is a public path segment, so what it may contain is a publishing decision rather
/// than a formatting one.
/// </summary>
public class UsernamePolicyTests
{
    [Theory]
    [InlineData("alice")]
    [InlineData("Alice")]
    [InlineData("a_b")]
    [InlineData("caleb-t")]
    [InlineData("user123")]
    [InlineData("abc")]
    public void Accepts_an_ordinary_name(string username) =>
        Assert.Null(UsernamePolicy.Validate(username));

    /// <summary>
    /// The reason this exists. The sign-in field takes a username or an email, so people typed
    /// an email into the sign-up box — and it was published on a profile page and in sitemap.xml.
    /// </summary>
    [Theory]
    [InlineData("someone@example.com")]
    [InlineData("first.last@example.co.uk")]
    public void Refuses_an_email_address(string username)
    {
        var problem = UsernamePolicy.Validate(username);

        Assert.NotNull(problem);
        Assert.Contains("email address", problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Refuses_nothing_at_all(string? username) =>
        Assert.NotNull(UsernamePolicy.Validate(username));

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    public void Refuses_something_too_short_to_identify_anyone(string username) =>
        Assert.Contains("between", UsernamePolicy.Validate(username));

    [Fact]
    public void Refuses_something_longer_than_the_column()
    {
        var problem = UsernamePolicy.Validate(new string('a', UsernamePolicy.MaxLength + 1));

        Assert.Contains("between", problem);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("has/slash")]
    [InlineData("has.dot")]
    [InlineData("has%20encoded")]
    [InlineData("emoji\U0001F600")]
    public void Refuses_characters_that_would_have_to_be_escaped_in_a_url(string username) =>
        Assert.NotNull(UsernamePolicy.Validate(username));

    [Theory]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("_leading")]
    [InlineData("trailing_")]
    public void Refuses_punctuation_at_the_edges(string username) =>
        Assert.Contains("start or end", UsernamePolicy.Validate(username));

    /// <summary>
    /// The half that matters is impersonation: an "admin" or "support" profile on the instance's
    /// own domain is a phishing primitive.
    /// </summary>
    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("support")]
    [InlineData("linkbelli")]
    [InlineData("api")]
    [InlineData("login")]
    [InlineData("no-reply")]
    public void Refuses_a_reserved_name(string username) =>
        Assert.Contains("reserved", UsernamePolicy.Validate(username));

    [Fact]
    public void Ignores_surrounding_whitespace_rather_than_refusing_it() =>
        Assert.Null(UsernamePolicy.Validate("  alice  "));

    /// <summary>
    /// Accounts predating the policy keep working; what they lose is republication of the name.
    /// </summary>
    [Theory]
    [InlineData("someone@example.com", false)]
    [InlineData("a", false)]
    [InlineData("alice", true)]
    public void Says_whether_an_existing_name_is_safe_to_publish(string username, bool expected) =>
        Assert.Equal(expected, UsernamePolicy.IsPublishable(username));
}
