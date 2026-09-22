using System.Text.Json;
using EntityBuilder.Interfaces;
using EntityBuilder.Models;
using EntityBuilder.Utilities;
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

    public async Task<ScheduledReport?> GetScheduledReportAsync(string id, string userEmail)
    {
        var db = _redis.GetDatabase();
        var existing = await db.HashGetAsync(GetKey(userEmail), id);
        if (existing.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<ScheduledReport>(existing.ToString(), JsonOptions);
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
        await SaveAsync(db, key, id, report);
        return true;
    }

    public async Task<bool> RerunScheduledReportAsync(string id, string userEmail)
    {
        var db = _redis.GetDatabase();
        var key = GetKey(userEmail);
        var existing = await db.HashGetAsync(key, id);

        if (existing.IsNullOrEmpty) return false;

        var report = JsonSerializer.Deserialize<ScheduledReport>(existing.ToString(), JsonOptions);
        if (report == null) return false;

        // Requeue for immediate pickup on the worker's next tick, regardless of prior status.
        report.Status = ReportStatus.Queued;
        report.NextRun = DateTime.UtcNow;
        await SaveAsync(db, key, id, report);
        return true;
    }

    public async Task<ScheduledReport?> EditScheduledReportAsync(
        string id,
        string userEmail,
        EditScheduledReportRequest patch,
        string? rebuiltSql,
        Dictionary<string, string>? rebuiltParameters)
    {
        var db = _redis.GetDatabase();
        var key = GetKey(userEmail);
        var existing = await db.HashGetAsync(key, id);

        if (existing.IsNullOrEmpty) return null;

        var report = JsonSerializer.Deserialize<ScheduledReport>(existing.ToString(), JsonOptions);
        if (report == null) return null;

        // Apply patch — CreatedBy, Id, CreatedAt stay put.
        // SQL + parameters only change when the caller passes a rebuilt result (i.e. patch.QueryDefinition
        // was present and IQueryExecutionService.BuildQueryAsync succeeded). Client SQL is never stored.
        if (patch.QueryDefinition != null && rebuiltSql != null)
        {
            report.Sql = rebuiltSql;
            report.QueryDefinition = patch.QueryDefinition;
            report.DapperTemplateValues = rebuiltParameters ?? new();
        }

        if (!string.IsNullOrWhiteSpace(patch.Subject)) report.Subject = patch.Subject!;
        if (!string.IsNullOrWhiteSpace(patch.RecipientEmail))
        {
            report.RecipientEmail = patch.RecipientEmail!;
            report.DisplayName = patch.RecipientEmail!; // keep displayName in sync so worker email uses the new address
        }
        report.Frequency = patch.Frequency;
        report.ScheduledTime = patch.ScheduledTime ?? "08:00";
        report.ScheduledDate = patch.ScheduledDate;
        report.DayOfWeek = patch.DayOfWeek;
        report.DayOfMonth = patch.DayOfMonth;
        report.UtcOffsetMinutes = patch.UtcOffsetMinutes;

        // Editing implies "queue me with the new schedule" — even if the previous run failed.
        report.Status = ReportStatus.Queued;
        report.NextRun = ScheduleNextRunCalculator.Compute(report, DateTime.UtcNow);

        await SaveAsync(db, key, id, report);
        return report;
    }

    private static Task SaveAsync(IDatabase db, string key, string id, ScheduledReport report)
    {
        var json = JsonSerializer.Serialize(report, JsonOptions);
        return db.HashSetAsync(key, id, json);
    }

    public async Task<List<ScheduledReport>> GetDueReportsAsync()
    {
        var db = _redis.GetDatabase();
        var server = _redis.GetServers().First();
        var dueReports = new List<ScheduledReport>();

        await foreach (var key in server.KeysAsync(pattern: "scheduled-reports:*"))
        {
            var entries = await db.HashGetAllAsync(key);
            foreach (var entry in entries)
            {
                if (entry.Value.IsNullOrEmpty) continue;
                var report = JsonSerializer.Deserialize<ScheduledReport>(entry.Value.ToString(), JsonOptions);
                if (report != null && report.Status == ReportStatus.Queued && report.NextRun <= DateTime.UtcNow)
                    dueReports.Add(report);
            }
        }

        return dueReports;
    }

    public async Task UpdateReportAsync(ScheduledReport report)
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(report, JsonOptions);
        await db.HashSetAsync(GetKey(report.CreatedBy), report.Id, json);
    }
}
