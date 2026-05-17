using EntityBuilder.Models;

namespace EntityBuilder.Interfaces;

public interface IReportScheduleService
{
    Task<ScheduledReport> ScheduleReportAsync(ScheduledReport report);
    Task<List<ScheduledReport>> GetScheduledReportsAsync(string userEmail);
    Task<bool> CancelScheduledReportAsync(string id, string userEmail);
    Task<List<ScheduledReport>> GetDueReportsAsync();
    Task UpdateReportAsync(ScheduledReport report);
}
