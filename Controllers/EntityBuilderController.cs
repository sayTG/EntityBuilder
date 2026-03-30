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

    public EntityBuilderController(
        IDatabaseMetadataService metadataService,
        IQueryExecutionService queryService,
        IReportEmailService reportEmailService)
    {
        _metadataService = metadataService;
        _queryService = queryService;
        _reportEmailService = reportEmailService;
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

        var reportRequest = new ReportEmailRequest
        {
            Sql = request.Sql,
            Token = token,
            RecipientEmail = recipientEmail,
            DisplayName = recipientEmail != email ? recipientEmail : displayName,
            Subject = request.Subject ?? "Entity Builder Report",
            DapperTemplateValues = request.DapperTemplateValues ?? new()
        };

        var result = await _reportEmailService.SendReportAsync(reportRequest);

        if (result.Code != 1)
            return BadRequest(new { message = result.ShortDescription });

        return Json(new { message = result.ShortDescription });
    }
}
