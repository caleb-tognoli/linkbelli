using Microsoft.AspNetCore.Identity;

namespace Linkbelli.Application.Identity;

/// <summary>The Identity user. Guid-keyed to match OwnerId references across the domain.</summary>
public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Whether the user opts in to seeing NSFW content. Default false.</summary>
    public bool ShowNsfw { get; set; }
}
