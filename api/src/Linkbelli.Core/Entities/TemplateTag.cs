namespace Linkbelli.Core.Entities;

public class TemplateTag : BaseEntity<Guid>, ISoftDeletable
{
    public Guid TemplateId { get; set; }
    public Guid TagId { get; set; }
    public SourceTemplate? Template { get; set; }
    public Tag? Tag { get; set; }
}
