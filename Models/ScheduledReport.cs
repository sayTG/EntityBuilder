namespace EntityBuilder.Models;

public enum ReportFrequency
{
    Once,
    Daily,
    Weekly,
    Monthly
}

public enum ReportStatus
{
    Queued,
    Sent,
    Failed,
    Cancelled
}

public class ScheduledReport
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Sql { get; set; } = "";
    public string Subject { get; set; } = "Entity Builder Report";
    public string RecipientEmail { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public Dictionary<string, string> DapperTemplateValues { get; set; } = new();
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ReportFrequency Frequency { get; set; } = ReportFrequency.Once;
    public string ScheduledTime { get; set; } = "08:00";
    public string? ScheduledDate { get; set; }
    public int? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Queued;
    public int UtcOffsetMinutes { get; set; }
    public DateTime? LastRun { get; set; }
    public DateTime NextRun { get; set; }
}
