namespace Linkbelli.Core.Entities;

/// <summary>A user's bookmark of a template they want to reuse.</summary>
public class UserSavedTemplate : BaseEntity<Guid>
{
    public Guid UserId { get; set; }
    public Guid TemplateId { get; set; }
    public SourceTemplate? Template { get; set; }
}
