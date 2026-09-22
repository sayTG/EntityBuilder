using EntityBuilder.Models;

namespace EntityBuilder.Interfaces;

public interface IReportScheduleService
{
    Task<ScheduledReport> ScheduleReportAsync(ScheduledReport report);
    Task<List<ScheduledReport>> GetScheduledReportsAsync(string userEmail);
    Task<ScheduledReport?> GetScheduledReportAsync(string id, string userEmail);
    Task<bool> CancelScheduledReportAsync(string id, string userEmail);
    Task<bool> RerunScheduledReportAsync(string id, string userEmail);
    // rebuiltSql/rebuiltParameters come from IQueryExecutionService.BuildQueryAsync; when non-null
    // they replace the stored SQL + params. Callers must NOT pass client-supplied SQL directly.
    Task<ScheduledReport?> EditScheduledReportAsync(
        string id,
        string userEmail,
        EditScheduledReportRequest patch,
        string? rebuiltSql,
        Dictionary<string, string>? rebuiltParameters);
    Task<List<ScheduledReport>> GetDueReportsAsync();
    Task UpdateReportAsync(ScheduledReport report);
}
