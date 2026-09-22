using System.Security.Claims;
using EntityBuilder.Interfaces;
using EntityBuilder.Models;
using EntityBuilder.Utilities;
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

        var (sqlOk, sqlReason) = SqlSafetyGuard.EnsureSafeSelect(request.Sql);
        if (!sqlOk) return BadRequest(new { message = sqlReason });

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
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrEmpty(email))
            return BadRequest(new { message = "Could not determine user email." });

        // Preferred path: build SQL server-side from the structured definition. Client-supplied SQL
        // is never trusted for the schedule flow — QueryDefinition wins if provided.
        string sql;
        Dictionary<string, string> parameters;
        if (request.QueryDefinition != null)
        {
            var built = await _queryService.BuildQueryAsync(request.QueryDefinition);
            if (!built.IsSuccess)
                return BadRequest(new { message = built.ErrorMessage });
            sql = built.GeneratedSql ?? "";
            parameters = built.Parameters;
        }
        else
        {
            // Legacy path (older clients) — still guarded, still parameterised.
            if (string.IsNullOrWhiteSpace(request.Sql))
                return BadRequest(new { message = "No query to schedule." });
            var (sqlOk, sqlReason) = SqlSafetyGuard.EnsureSafeSelect(request.Sql);
            if (!sqlOk) return BadRequest(new { message = sqlReason });
            sql = request.Sql;
            parameters = request.DapperTemplateValues ?? new();
        }

        var displayName = User.FindFirstValue("DisplayName") ?? email;
        var recipientEmail = string.IsNullOrWhiteSpace(request.RecipientEmail) ? email : request.RecipientEmail;

        var now = DateTime.UtcNow;
        var report = new ScheduledReport
        {
            Sql = sql,
            QueryDefinition = request.QueryDefinition,
            Subject = request.Subject ?? "Entity Builder Report",
            RecipientEmail = recipientEmail,
            DisplayName = recipientEmail != email ? recipientEmail : displayName,
            DapperTemplateValues = parameters,
            CreatedBy = email,
            CreatedAt = now,
            Frequency = request.Frequency,
            ScheduledTime = request.ScheduledTime ?? "08:00",
            ScheduledDate = request.ScheduledDate,
            DayOfWeek = request.DayOfWeek,
            DayOfMonth = request.DayOfMonth,
            UtcOffsetMinutes = request.UtcOffsetMinutes,
            Status = ReportStatus.Queued
        };
        report.NextRun = ScheduleNextRunCalculator.Compute(report, now);

        await _reportScheduleService.ScheduleReportAsync(report);
        return Json(new { message = "Report scheduled successfully.", report });
    }

    [HttpGet]
    public async Task<IActionResult> GetScheduledReport(string id)
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrEmpty(email))
            return BadRequest(new { message = "Could not determine user email." });

        var report = await _reportScheduleService.GetScheduledReportAsync(id, email);
        if (report == null) return NotFound(new { message = "Scheduled report not found." });
        return Json(report);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RerunScheduledReport(string id)
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrEmpty(email))
            return BadRequest(new { message = "Could not determine user email." });

        var success = await _reportScheduleService.RerunScheduledReportAsync(id, email);
        if (!success)
            return NotFound(new { message = "Scheduled report not found." });

        return Json(new { message = "Report re-queued. It will run on the next worker tick." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateScheduledReport(string id, [FromBody] EditScheduledReportRequest request)
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrEmpty(email))
            return BadRequest(new { message = "Could not determine user email." });

        // If the client sent a structured definition, rebuild SQL server-side and let the service
        // replace the stored SQL + parameters. Raw SQL from the client is never accepted here.
        string? rebuiltSql = null;
        Dictionary<string, string>? rebuiltParams = null;
        if (request.QueryDefinition != null)
        {
            var built = await _queryService.BuildQueryAsync(request.QueryDefinition);
            if (!built.IsSuccess)
                return BadRequest(new { message = built.ErrorMessage });
            rebuiltSql = built.GeneratedSql;
            rebuiltParams = built.Parameters;
        }

        var updated = await _reportScheduleService.EditScheduledReportAsync(id, email, request, rebuiltSql, rebuiltParams);
        if (updated == null)
            return NotFound(new { message = "Scheduled report not found." });

        return Json(new { message = "Report updated.", report = updated });
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
