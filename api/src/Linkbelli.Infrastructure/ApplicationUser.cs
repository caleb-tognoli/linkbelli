using Microsoft.AspNetCore.Identity;

namespace Linkbelli.Infrastructure;

/// <summary>The Identity user. Guid-keyed to match OwnerId references across the domain.</summary>
public class ApplicationUser : IdentityUser<Guid>;
