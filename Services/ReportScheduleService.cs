using System.Text.Json;
using EntityBuilder.Interfaces;
using EntityBuilder.Models;
using StackExchange.Redis;

namespace EntityBuilder.Services;

public class ReportScheduleService : IReportScheduleService
{
    private readonly IConnectionMultiplexer _redis;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ReportScheduleService(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    private static string GetKey(string userEmail) => $"scheduled-reports:{userEmail}";

    public async Task<ScheduledReport> ScheduleReportAsync(ScheduledReport report)
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(report, JsonOptions);
        await db.HashSetAsync(GetKey(report.CreatedBy), report.Id, json);
        return report;
    }

    public async Task<List<ScheduledReport>> GetScheduledReportsAsync(string userEmail)
    {
        var db = _redis.GetDatabase();
        var entries = await db.HashGetAllAsync(GetKey(userEmail));
        var reports = new List<ScheduledReport>();

        foreach (var entry in entries)
        {
            if (entry.Value.IsNullOrEmpty) continue;
            var report = JsonSerializer.Deserialize<ScheduledReport>(entry.Value.ToString(), JsonOptions);
            if (report != null)
                reports.Add(report);
        }

        return reports.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public async Task<bool> CancelScheduledReportAsync(string id, string userEmail)
    {
        var db = _redis.GetDatabase();
        var key = GetKey(userEmail);
        var existing = await db.HashGetAsync(key, id);

        if (existing.IsNullOrEmpty) return false;

        var report = JsonSerializer.Deserialize<ScheduledReport>(existing.ToString(), JsonOptions);
        if (report == null) return false;

        report.Status = ReportStatus.Cancelled;
        var json = JsonSerializer.Serialize(report, JsonOptions);
        await db.HashSetAsync(key, id, json);
        return true;
    }
}
