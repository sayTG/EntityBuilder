using EntityBuilder.Interfaces;
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

    public async Task<IActionResult> TableData(string schema, string table)
    {
        if (string.IsNullOrEmpty(schema) || string.IsNullOrEmpty(table))
            return RedirectToAction("Index");

        var columns = await _metadataService.GetColumnsAsync(schema, table);
        var data = await _queryService.GetTableDataAsync(schema, table);

        var model = new TableDataViewModel
        {
            SchemaName = schema,
            TableName = table,
            Columns = columns,
            Data = data
        };

        return View(model);
    }

    [HttpGet]
    public IActionResult Query(string? sql = null)
    {
        return View(new QueryResultViewModel { Sql = sql ?? string.Empty });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Query(QueryResultViewModel model)
    {
        if (!string.IsNullOrWhiteSpace(model.Sql))
        {
            model.Result = await _queryService.ExecuteSelectAsync(model.Sql);
        }

        return View(model);
    }
}
