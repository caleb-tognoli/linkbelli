namespace Linkbelli.Application.Sources;

/// <summary>Registers/removes a source's recurring schedule and triggers ad-hoc runs (Hangfire-backed).</summary>
public interface ISourceScheduler
{
    void Schedule(Guid sourceId, string cron);
    void Unschedule(Guid sourceId);
    void TriggerNow(Guid sourceId);
}
