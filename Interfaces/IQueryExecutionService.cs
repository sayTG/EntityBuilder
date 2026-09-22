using EntityBuilder.Models;

namespace EntityBuilder.Interfaces;

public interface IQueryExecutionService
{
    Task<QueryResultSet> ExecuteSelectAsync(string sql, int maxRows = 1000);
    Task<QueryResultSet> GetTableDataAsync(string schemaName, string tableName, int maxRows = 1000);
    Task<QueryResultSet> ExecuteStructuredQueryAsync(QueryBuilderRequest request);

    // Validate + build the unpaginated SELECT SQL and its parameters WITHOUT executing anything.
    // Used for scheduled reports so the SQL body is always server-generated from the structured
    // request — never trusted from the client.
    Task<QueryResultSet> BuildQueryAsync(QueryBuilderRequest request);
}
