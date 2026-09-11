namespace Linkbelli.Application.Sources;

/// <summary>Registers/removes a source's recurring schedule and triggers ad-hoc runs (Hangfire-backed).</summary>
public interface ISourceScheduler
{
    /// <param name="timeZone">IANA zone the cron is read in; null means UTC.</param>
    void Schedule(Guid sourceId, string cron, string? timeZone = null);
    void Unschedule(Guid sourceId);
    void TriggerNow(Guid sourceId);
}
