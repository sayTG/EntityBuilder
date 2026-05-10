using System.Security.Claims;
using EntityBuilder.Interfaces;
using EntityBuilder.Models;
using EntityBuilder.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EntityBuilder.Controllers;

[Authorize]
public class EntityBuilderController : Controller
{
    private readonly IDatabaseMetadataService _metadataService;
    private readonly IQueryExecutionService _queryService;
    private readonly IReportEmailService _reportEmailService;
    private readonly IReportScheduleService _reportScheduleService;

    public EntityBuilderController(
        IDatabaseMetadataService metadataService,
        IQueryExecutionService queryService,
        IReportEmailService reportEmailService,
        IReportScheduleService reportScheduleService)
    {
        _metadataService = metadataService;
        _queryService = queryService;
        _reportEmailService = reportEmailService;
        _reportScheduleService = reportScheduleService;
    }

    public async Task<IActionResult> Index()
    {
        var tables = await _metadataService.GetTablesAsync();
        var dbName = await _metadataService.GetDatabaseNameAsync();

        var model = new TableListViewModel
        {
            Tables = tables,
            DatabaseName = dbName
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> GetColumns(string schema, string table)
    {
        if (string.IsNullOrEmpty(schema) || string.IsNullOrEmpty(table))
            return BadRequest("Schema and table are required.");

        var columns = await _metadataService.GetColumnsAsync(schema, table);
        return Json(columns);
    }

    [HttpGet]
    public async Task<IActionResult> GetForeignKeys(string schema, string table)
    {
        if (string.IsNullOrEmpty(schema) || string.IsNullOrEmpty(table))
            return BadRequest("Schema and table are required.");

        var fks = await _metadataService.GetForeignKeysAsync(schema, table);
        return Json(fks);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExecuteQuery([FromBody] QueryBuilderRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _queryService.ExecuteStructuredQueryAsync(request);
        return Json(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendReportEmail([FromBody] SendReportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Sql))
            return BadRequest(new { message = "No SQL to send." });

        var token = User.FindFirstValue("AccessToken");
        if (string.IsNullOrEmpty(token))
            return Unauthorized(new { message = "Session expired. Please log in again." });

        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrEmpty(email))
            return BadRequest(new { message = "Could not determine recipient email." });

        var displayName = User.FindFirstValue("DisplayName") ?? email;

        var recipientEmail = string.IsNullOrWhiteSpace(request.RecipientEmail) ? email : request.RecipientEmail;
        var isCustomRecipient = !string.IsNullOrWhiteSpace(request.RecipientEmail);

        var reportRequest = new ReportEmailRequest
        {
            Sql = request.Sql,
            Token = token,
            RecipientEmail = recipientEmail,
            DisplayName = isCustomRecipient ? recipientEmail.Split(',')[0].Trim() : displayName,
            Subject = request.Subject ?? "Entity Builder Report",
            DapperTemplateValues = request.DapperTemplateValues ?? new()
        };

        var result = await _reportEmailService.SendReportAsync(reportRequest);

        if (result.Code != 1)
            return BadRequest(new { message = result.ShortDescription });

        return Json(new { message = result.ShortDescription });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ScheduleReport([FromBody] ScheduleReportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Sql))
            return BadRequest(new { message = "No SQL to schedule." });

        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrEmpty(email))
            return BadRequest(new { message = "Could not determine user email." });

        var displayName = User.FindFirstValue("DisplayName") ?? email;
        var recipientEmail = string.IsNullOrWhiteSpace(request.RecipientEmail) ? email : request.RecipientEmail;

        var now = DateTime.UtcNow;
        var timeParts = (request.ScheduledTime ?? "08:00").Split(':');
        var hour = int.Parse(timeParts[0]);
        var minute = timeParts.Length > 1 ? int.Parse(timeParts[1]) : 0;

        DateTime nextRun;
        switch (request.Frequency)
        {
            case ReportFrequency.Once:
                if (!string.IsNullOrEmpty(request.ScheduledDate) && DateOnly.TryParse(request.ScheduledDate, out var date))
                    nextRun = date.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc);
                else
                    nextRun = now.Date.AddDays(1).AddHours(hour).AddMinutes(minute);
                break;
            case ReportFrequency.Daily:
                nextRun = now.Date.AddHours(hour).AddMinutes(minute);
                if (nextRun <= now) nextRun = nextRun.AddDays(1);
                break;
            case ReportFrequency.Weekly:
                var targetDay = request.DayOfWeek ?? 1;
                nextRun = now.Date.AddHours(hour).AddMinutes(minute);
                while ((int)nextRun.DayOfWeek != targetDay || nextRun <= now)
                    nextRun = nextRun.AddDays(1);
                break;
            case ReportFrequency.Monthly:
                var targetDayOfMonth = request.DayOfMonth ?? 1;
                nextRun = new DateTime(now.Year, now.Month, Math.Min(targetDayOfMonth, DateTime.DaysInMonth(now.Year, now.Month)), hour, minute, 0, DateTimeKind.Utc);
                if (nextRun <= now) nextRun = nextRun.AddMonths(1);
                break;
            default:
                nextRun = now.AddDays(1);
                break;
        }

        var report = new ScheduledReport
        {
            Sql = request.Sql,
            Subject = request.Subject ?? "Entity Builder Report",
            RecipientEmail = recipientEmail,
            DisplayName = recipientEmail != email ? recipientEmail : displayName,
            DapperTemplateValues = request.DapperTemplateValues ?? new(),
            CreatedBy = email,
            CreatedAt = now,
            Frequency = request.Frequency,
            ScheduledTime = request.ScheduledTime ?? "08:00",
            ScheduledDate = request.ScheduledDate,
            DayOfWeek = request.DayOfWeek,
            DayOfMonth = request.DayOfMonth,
            Status = ReportStatus.Queued,
            NextRun = nextRun
        };

        await _reportScheduleService.ScheduleReportAsync(report);
        return Json(new { message = "Report scheduled successfully.", report });
    }

    [HttpGet]
    public async Task<IActionResult> ScheduledReports()
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrEmpty(email))
            return BadRequest(new { message = "Could not determine user email." });

        var reports = await _reportScheduleService.GetScheduledReportsAsync(email);
        return Json(reports);
    }

    [HttpDelete]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelScheduledReport(string id)
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrEmpty(email))
            return BadRequest(new { message = "Could not determine user email." });

        var success = await _reportScheduleService.CancelScheduledReportAsync(id, email);
        if (!success)
            return NotFound(new { message = "Scheduled report not found." });

        return Json(new { message = "Scheduled report cancelled." });
    }
}
