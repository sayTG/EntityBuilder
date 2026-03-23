using EntityBuilder.Models;

namespace EntityBuilder.ViewModels;

public class TableDataViewModel
{
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public IReadOnlyList<ColumnMetadata> Columns { get; set; } = Array.Empty<ColumnMetadata>();
    public QueryResultSet Data { get; set; } = new();
}
