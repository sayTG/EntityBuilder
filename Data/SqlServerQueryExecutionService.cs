using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using EntityBuilder.Interfaces;
using EntityBuilder.Models;

namespace EntityBuilder.Data;

public partial class SqlServerQueryExecutionService : IQueryExecutionService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IDatabaseMetadataService _metadataService;

    private static readonly HashSet<string> ForbiddenKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE",
        "EXEC", "EXECUTE", "TRUNCATE", "GRANT", "REVOKE", "DENY",
        "MERGE", "BULK", "BACKUP", "RESTORE", "SHUTDOWN", "DBCC"
    };

    public SqlServerQueryExecutionService(
        IDbConnectionFactory connectionFactory,
        IDatabaseMetadataService metadataService)
    {
        _connectionFactory = connectionFactory;
        _metadataService = metadataService;
    }

    public async Task<QueryResultSet> ExecuteSelectAsync(string sql, int maxRows = 1000)
    {
        var result = new QueryResultSet();

        var validationError = ValidateSelectOnly(sql);
        if (validationError is not null)
        {
            result.ErrorMessage = validationError;
            return result;
        }

        var sw = Stopwatch.StartNew();

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 30;

            using var reader = command.ExecuteReader();

            for (int i = 0; i < reader.FieldCount; i++)
                result.Columns.Add(reader.GetName(i));

            int rowCount = 0;
            while (reader.Read() && rowCount < maxRows)
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                result.Rows.Add(row);
                rowCount++;
            }

            result.TotalRowsReturned = rowCount;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        sw.Stop();
        result.ExecutionTimeMs = sw.ElapsedMilliseconds;
        return result;
    }

    public async Task<QueryResultSet> GetTableDataAsync(string schemaName, string tableName, int maxRows = 1000)
    {
        var tables = await _metadataService.GetTablesAsync();
        var tableExists = tables.Any(t =>
            t.SchemaName.Equals(schemaName, StringComparison.OrdinalIgnoreCase) &&
            t.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase));

        if (!tableExists)
        {
            return new QueryResultSet { ErrorMessage = $"Table [{schemaName}].[{tableName}] not found." };
        }

        var sql = $"SELECT TOP {maxRows} * FROM [{schemaName}].[{tableName}]";
        return await ExecuteSelectAsync(sql, maxRows);
    }

    private static string? ValidateSelectOnly(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return "Query cannot be empty.";

        var trimmed = sql.Trim();

        if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            return "Only SELECT queries are allowed.";

        if (trimmed.Contains(';'))
            return "Multiple statements are not allowed.";

        var words = WordBoundaryRegex().Matches(trimmed);
        foreach (Match word in words)
        {
            if (ForbiddenKeywords.Contains(word.Value))
                return $"The keyword '{word.Value.ToUpper()}' is not allowed. Only SELECT queries are permitted.";
        }

        return null;
    }

    [GeneratedRegex(@"\b\w+\b")]
    private static partial Regex WordBoundaryRegex();
}
