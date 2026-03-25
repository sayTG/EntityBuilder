using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using EntityBuilder.Configuration;
using EntityBuilder.Interfaces;
using EntityBuilder.Models;
using Microsoft.Extensions.Options;

namespace EntityBuilder.Data;

public partial class SqlServerQueryExecutionService : IQueryExecutionService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IDatabaseMetadataService _metadataService;
    private readonly int? _maxDop;

    private static readonly HashSet<string> ForbiddenKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE",
        "EXEC", "EXECUTE", "TRUNCATE", "GRANT", "REVOKE", "DENY",
        "MERGE", "BULK", "BACKUP", "RESTORE", "SHUTDOWN", "DBCC"
    };

    public SqlServerQueryExecutionService(
        IDbConnectionFactory connectionFactory,
        IDatabaseMetadataService metadataService,
        IOptions<DatabaseSettings> dbSettings)
    {
        _connectionFactory = connectionFactory;
        _metadataService = metadataService;
        _maxDop = dbSettings.Value.MaxDop;
    }

    private string AppendMaxDop(string sql) =>
        _maxDop.HasValue ? $"{sql} OPTION (MAXDOP {_maxDop.Value})" : sql;

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
            command.CommandText = AppendMaxDop(sql);
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

    public async Task<QueryResultSet> ExecuteStructuredQueryAsync(QueryBuilderRequest request)
    {
        var result = new QueryResultSet
        {
            CurrentPage = request.Page,
            PageSize = Math.Min(request.PageSize, 200)
        };

        var sw = Stopwatch.StartNew();

        try
        {
            // Validate all table references
            var allTables = await _metadataService.GetTablesAsync();
            var tableSet = new HashSet<string>(
                allTables.Select(t => $"{t.SchemaName}.{t.TableName}"),
                StringComparer.OrdinalIgnoreCase);

            var mainKey = $"{request.Schema}.{request.Table}";
            if (!tableSet.Contains(mainKey))
            {
                result.ErrorMessage = $"Table {mainKey} not found.";
                return result;
            }

            // Build alias map: "schema.table" -> alias
            var aliases = new List<(string Schema, string Table, string Alias)>
            {
                (request.Schema, request.Table, "t0")
            };

            for (int i = 0; i < request.Joins.Count; i++)
            {
                var join = request.Joins[i];
                var joinKey = $"{join.Schema}.{join.Table}";
                if (!tableSet.Contains(joinKey))
                {
                    result.ErrorMessage = $"Joined table {joinKey} not found.";
                    return result;
                }
                aliases.Add((join.Schema, join.Table, $"t{i + 1}"));
            }

            // Fetch and cache columns for all referenced tables
            var columnsByTable = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (schema, table, _) in aliases)
            {
                var key = $"{schema}.{table}";
                if (!columnsByTable.ContainsKey(key))
                {
                    var cols = await _metadataService.GetColumnsAsync(schema, table);
                    columnsByTable[key] = new HashSet<string>(
                        cols.Select(c => c.ColumnName),
                        StringComparer.OrdinalIgnoreCase);
                }
            }

            var aliasLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (schema, table, alias) in aliases)
                aliasLookup[$"{schema}.{table}"] = alias;

            // Build SELECT clause
            var selectParts = new List<string>();
            if (request.SelectedColumns.Count == 0)
            {
                selectParts.Add("*");
            }
            else
            {
                foreach (var col in request.SelectedColumns)
                {
                    var tblKey = $"{col.Schema}.{col.Table}";
                    if (!columnsByTable.TryGetValue(tblKey, out var validCols) || !validCols.Contains(col.Column))
                    {
                        result.ErrorMessage = $"Column {col.Column} not found on {tblKey}.";
                        return result;
                    }
                    var alias = aliasLookup[tblKey];
                    selectParts.Add($"[{alias}].[{col.Column}]");
                }
            }

            // Build FROM clause
            var fromClause = $"[{request.Schema}].[{request.Table}] AS [t0]";

            // Build JOIN clauses
            var allowedJoinTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "FULL OUTER JOIN"
            };

            var joinClauses = new List<string>();
            for (int i = 0; i < request.Joins.Count; i++)
            {
                var join = request.Joins[i];
                if (!allowedJoinTypes.Contains(join.JoinType))
                {
                    result.ErrorMessage = $"Invalid join type: {join.JoinType}";
                    return result;
                }

                var alias = $"t{i + 1}";

                // Parse left column: "schema.table.column"
                var leftParts = join.LeftColumn.Split('.');
                if (leftParts.Length != 3)
                {
                    result.ErrorMessage = $"Invalid left column reference: {join.LeftColumn}";
                    return result;
                }

                var leftTblKey = $"{leftParts[0]}.{leftParts[1]}";
                if (!aliasLookup.TryGetValue(leftTblKey, out var leftAlias))
                {
                    result.ErrorMessage = $"Table {leftTblKey} not found in query.";
                    return result;
                }

                if (!columnsByTable.TryGetValue(leftTblKey, out var leftCols) || !leftCols.Contains(leftParts[2]))
                {
                    result.ErrorMessage = $"Column {leftParts[2]} not found on {leftTblKey}.";
                    return result;
                }

                var rightTblKey = $"{join.Schema}.{join.Table}";
                if (!columnsByTable.TryGetValue(rightTblKey, out var rightCols) || !rightCols.Contains(join.RightColumn))
                {
                    result.ErrorMessage = $"Column {join.RightColumn} not found on {rightTblKey}.";
                    return result;
                }

                joinClauses.Add(
                    $"{join.JoinType} [{join.Schema}].[{join.Table}] AS [{alias}] " +
                    $"ON [{leftAlias}].[{leftParts[2]}] = [{alias}].[{join.RightColumn}]");
            }

            // Build WHERE clause with parameters
            var allowedOperators = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "=", "!=", "<>", ">", "<", ">=", "<=", "LIKE", "IN", "IS NULL", "IS NOT NULL"
            };

            var whereParts = new List<string>();
            var parameters = new List<(string Name, object Value)>();
            int paramIndex = 0;

            for (int i = 0; i < request.WhereConditions.Count; i++)
            {
                var cond = request.WhereConditions[i];
                if (!allowedOperators.Contains(cond.Operator))
                {
                    result.ErrorMessage = $"Invalid operator: {cond.Operator}";
                    return result;
                }

                var colParts = cond.Column.Split('.');
                if (colParts.Length != 3)
                {
                    result.ErrorMessage = $"Invalid column reference: {cond.Column}";
                    return result;
                }

                var colTblKey = $"{colParts[0]}.{colParts[1]}";
                if (!aliasLookup.TryGetValue(colTblKey, out var colAlias))
                {
                    result.ErrorMessage = $"Table {colTblKey} not found in query.";
                    return result;
                }

                if (!columnsByTable.TryGetValue(colTblKey, out var colValid) || !colValid.Contains(colParts[2]))
                {
                    result.ErrorMessage = $"Column {colParts[2]} not found on {colTblKey}.";
                    return result;
                }

                string clause;
                if (cond.Operator is "IS NULL" or "IS NOT NULL")
                {
                    clause = $"[{colAlias}].[{colParts[2]}] {cond.Operator}";
                }
                else if (cond.Operator.Equals("IN", StringComparison.OrdinalIgnoreCase))
                {
                    var values = (cond.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var inParams = new List<string>();
                    foreach (var v in values)
                    {
                        var pName = $"@p{paramIndex++}";
                        inParams.Add(pName);
                        parameters.Add((pName, v));
                    }
                    clause = $"[{colAlias}].[{colParts[2]}] IN ({string.Join(", ", inParams)})";
                }
                else
                {
                    var pName = $"@p{paramIndex++}";
                    parameters.Add((pName, cond.Value ?? (object)DBNull.Value));
                    clause = $"[{colAlias}].[{colParts[2]}] {cond.Operator} {pName}";
                }

                if (i > 0)
                {
                    var connector = cond.Connector?.Equals("OR", StringComparison.OrdinalIgnoreCase) == true ? "OR" : "AND";
                    whereParts.Add($"{connector} {clause}");
                }
                else
                {
                    whereParts.Add(clause);
                }
            }

            var whereClause = whereParts.Count > 0 ? "WHERE " + string.Join(" ", whereParts) : "";
            var joinSql = string.Join("\n", joinClauses);

            // Expose parameters for report email
            foreach (var (name, value) in parameters)
                result.Parameters[name.TrimStart('@')] = value?.ToString() ?? "";

            // Build GROUP BY clause
            var allowedAggregateFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "COUNT", "SUM", "AVG", "MIN", "MAX"
            };

            var hasGroupBy = request.GroupByColumns.Count > 0;
            var groupByParts = new List<string>();

            if (hasGroupBy)
            {
                // Override SELECT with group by columns + aggregates
                selectParts.Clear();

                foreach (var gb in request.GroupByColumns)
                {
                    var gbKey = $"{gb.Schema}.{gb.Table}";
                    if (!columnsByTable.TryGetValue(gbKey, out var gbCols) || !gbCols.Contains(gb.Column))
                    {
                        result.ErrorMessage = $"Column {gb.Column} not found on {gbKey}.";
                        return result;
                    }
                    var gbAlias = aliasLookup[gbKey];
                    var colRef = $"[{gbAlias}].[{gb.Column}]";
                    selectParts.Add(colRef);
                    groupByParts.Add(colRef);
                }

                foreach (var agg in request.AggregateColumns)
                {
                    if (!allowedAggregateFunctions.Contains(agg.Function))
                    {
                        result.ErrorMessage = $"Invalid aggregate function: {agg.Function}";
                        return result;
                    }

                    string aggExpr;
                    if (agg.Column == "*")
                    {
                        aggExpr = $"{agg.Function.ToUpper()}(*)";
                    }
                    else
                    {
                        var aggKey = $"{agg.Schema}.{agg.Table}";
                        if (!columnsByTable.TryGetValue(aggKey, out var aggCols) || !aggCols.Contains(agg.Column))
                        {
                            result.ErrorMessage = $"Column {agg.Column} not found on {aggKey}.";
                            return result;
                        }
                        var aggAlias = aliasLookup[aggKey];
                        aggExpr = $"{agg.Function.ToUpper()}([{aggAlias}].[{agg.Column}])";
                    }

                    var aliasName = string.IsNullOrWhiteSpace(agg.Alias) ? $"{agg.Function}_{agg.Column}" : agg.Alias;
                    selectParts.Add($"{aggExpr} AS [{aliasName}]");
                }

                if (selectParts.Count == 0)
                {
                    result.ErrorMessage = "GROUP BY requires at least one column or aggregate.";
                    return result;
                }
            }

            var selectClause = string.Join(", ", selectParts);
            var groupByClause = groupByParts.Count > 0 ? "GROUP BY " + string.Join(", ", groupByParts) : "";

            // Build ORDER BY clause
            var orderByParts = new List<string>();
            foreach (var ob in request.OrderByColumns)
            {
                var obParts = ob.Column.Split('.');
                if (obParts.Length != 3)
                {
                    result.ErrorMessage = $"Invalid ORDER BY column reference: {ob.Column}";
                    return result;
                }

                var obTblKey = $"{obParts[0]}.{obParts[1]}";
                if (!aliasLookup.TryGetValue(obTblKey, out var obAlias))
                {
                    result.ErrorMessage = $"Table {obTblKey} not found in query.";
                    return result;
                }

                if (!columnsByTable.TryGetValue(obTblKey, out var obCols) || !obCols.Contains(obParts[2]))
                {
                    result.ErrorMessage = $"Column {obParts[2]} not found on {obTblKey}.";
                    return result;
                }

                var direction = ob.Direction?.Equals("DESC", StringComparison.OrdinalIgnoreCase) == true ? "DESC" : "ASC";
                orderByParts.Add($"[{obAlias}].[{obParts[2]}] {direction}");
            }

            var orderByClause = orderByParts.Count > 0
                ? "ORDER BY " + string.Join(", ", orderByParts)
                : "ORDER BY (SELECT NULL)";

            // Count query — use SELECT 1 to avoid duplicate column name / unnamed column errors
            var countInner = hasGroupBy
                ? $"SELECT 1 AS __x FROM {fromClause} {joinSql} {whereClause} {groupByClause}"
                : $"SELECT 1 AS __x FROM {fromClause} {joinSql} {whereClause}";
            var countSql = $"SELECT COUNT(*) FROM ({countInner}) AS __count";

            // Data query with pagination
            var dataQuery = $"SELECT {selectClause} FROM {fromClause} {joinSql} {whereClause} {groupByClause}";
            var offset = (request.Page - 1) * result.PageSize;
            var dataSql = $"{dataQuery} {orderByClause} OFFSET {offset} ROWS FETCH NEXT {result.PageSize} ROWS ONLY";

            // Store generated SQL for display
            result.GeneratedSql = dataSql;

            // Execute
            using var connection = _connectionFactory.CreateConnection();
            connection.Open();

            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = AppendMaxDop(countSql);
                countCmd.CommandTimeout = 120;
                AddParameters(countCmd, parameters);
                result.TotalRows = (int)countCmd.ExecuteScalar()!;
            }

            using (var dataCmd = connection.CreateCommand())
            {
                dataCmd.CommandText = AppendMaxDop(dataSql);
                dataCmd.CommandTimeout = 120;
                AddParameters(dataCmd, parameters);

                using var reader = dataCmd.ExecuteReader();
                for (int i = 0; i < reader.FieldCount; i++)
                    result.Columns.Add(reader.GetName(i));

                while (reader.Read())
                {
                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    result.Rows.Add(row);
                }

                result.TotalRowsReturned = result.Rows.Count;
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        sw.Stop();
        result.ExecutionTimeMs = sw.ElapsedMilliseconds;
        return result;
    }

    private static void AddParameters(IDbCommand command, List<(string Name, object Value)> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            var param = command.CreateParameter();
            param.ParameterName = name;
            param.Value = value;
            command.Parameters.Add(param);
        }
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
