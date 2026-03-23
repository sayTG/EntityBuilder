using EntityBuilder.Models;

namespace EntityBuilder.Interfaces;

public interface IQueryExecutionService
{
    Task<QueryResultSet> ExecuteSelectAsync(string sql, int maxRows = 1000);
    Task<QueryResultSet> GetTableDataAsync(string schemaName, string tableName, int maxRows = 1000);
}
