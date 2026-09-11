namespace Linkbelli.Core.Entities;

/// <summary>
/// A ready-made source config with the hard parts already filled in. Writing a scraper's CSS
/// selectors or a JSON API's paths is the steepest part of setting one up, and it is the same
/// work every time for any given service — so it is done once here and the person only supplies
/// what is genuinely theirs, like a channel name.
/// </summary>
public class SourceTemplate : BaseEntity<Guid>
{
    /// <summary>Stable identifier for a built-in, so seeding can update rather than duplicate it.</summary>
    public string? Key { get; set; }

    public required string Name { get; set; }

    /// <summary>What this pulls in, in a sentence someone choosing between templates can read.</summary>
    public required string Description { get; set; }

    public SourceType Type { get; set; }

    /// <summary>
    /// The config, with <c>{{placeholder}}</c> markers where a value from the person goes.
    /// Everything else — selectors, JSON paths, headers — is already correct.
    /// </summary>
    public required string BaseConfig { get; set; }

    /// <summary>The fields to ask for, as JSON: key, label, placeholder, help, required.</summary>
    public required string Fields { get; set; }

    /// <summary>
    /// Shipped with the app rather than created by an admin. Built-ins are re-seeded on startup,
    /// so a fix to a selector reaches everyone who used the template.
    /// </summary>
    public bool Builtin { get; set; }

    /// <summary>Suggested cron for this kind of source; the person can still change it.</summary>
    public string? SuggestedSchedule { get; set; }
}
