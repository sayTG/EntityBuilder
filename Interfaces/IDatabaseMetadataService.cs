using EntityBuilder.Models;

namespace EntityBuilder.Interfaces;

public interface IDatabaseMetadataService
{
    Task<IReadOnlyList<TableInfo>> GetTablesAsync();
    Task<IReadOnlyList<ColumnMetadata>> GetColumnsAsync(string schemaName, string tableName);
    Task<string> GetDatabaseNameAsync();
}
