namespace EntityBuilder.Models;

// Fields the user is allowed to change on an existing scheduled report.
// If QueryDefinition is supplied, the server rebuilds SQL + parameters from it and replaces the
// stored ones. The client never sends SQL text directly — that closes the injection surface.
public class EditScheduledReportRequest
{
    public QueryBuilderRequest? QueryDefinition { get; set; }
    public string? Subject { get; set; }
    public string? RecipientEmail { get; set; }
    public ReportFrequency Frequency { get; set; } = ReportFrequency.Once;
    public string ScheduledTime { get; set; } = "08:00";
    public string? ScheduledDate { get; set; }
    public int? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public int UtcOffsetMinutes { get; set; }
}
