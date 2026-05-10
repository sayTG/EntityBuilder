namespace EntityBuilder.Models;

public class ScheduleReportRequest
{
    public string Sql { get; set; } = "";
    public string? Subject { get; set; }
    public string? RecipientEmail { get; set; }
    public Dictionary<string, string>? DapperTemplateValues { get; set; }
    public ReportFrequency Frequency { get; set; } = ReportFrequency.Once;
    public string ScheduledTime { get; set; } = "08:00";
    public string? ScheduledDate { get; set; }
    public int? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
}
