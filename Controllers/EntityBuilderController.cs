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

    public EntityBuilderController(
        IDatabaseMetadataService metadataService,
        IQueryExecutionService queryService)
    {
        _metadataService = metadataService;
        _queryService = queryService;
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
}
