using System.Data;
using System.Text.RegularExpressions;
using EntityBuilder.Interfaces;
using EntityBuilder.Models;

namespace EntityBuilder.Data;

public partial class SqliteMetadataService : IDatabaseMetadataService
{
    // SQLite has no concept of schemas. We expose every table under a single
    // logical schema named "main" (SQLite's default database name) so the rest
    // of the app — which keys everything on "schema.table" — keeps working and
    // the generated [main].[table] SQL remains valid SQLite.
    public const string DefaultSchema = "main";

    private readonly IDbConnectionFactory _connectionFactory;

    public SqliteMetadataService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<IReadOnlyList<TableInfo>> GetTablesAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        // First read the table names, then count rows per table. Row counts need a
        // separate COUNT(*) because SQLite keeps no cached row-count metadata.
        var names = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
                ORDER BY name
                """;

            using var reader = command.ExecuteReader();
            while (reader.Read())
                names.Add(reader.GetString(0));
        }

        var tables = new List<TableInfo>();
        foreach (var name in names)
        {
            long rowCount = 0;
            using (var countCommand = connection.CreateCommand())
            {
                countCommand.CommandText = $"SELECT COUNT(*) FROM {QuoteIdentifier(name)}";
                rowCount = Convert.ToInt64(countCommand.ExecuteScalar() ?? 0L);
            }

            tables.Add(new TableInfo
            {
                SchemaName = DefaultSchema,
                TableName = name,
                RowCount = rowCount
            });
        }

        return Task.FromResult<IReadOnlyList<TableInfo>>(tables);
    }

    public Task<IReadOnlyList<ColumnMetadata>> GetColumnsAsync(string schemaName, string tableName)
    {
        var columns = new List<ColumnMetadata>();

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        // PRAGMA statements can't be parameterized, so the identifier is quoted/escaped.
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)})";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // Columns: cid, name, type, notnull, dflt_value, pk
            var cid = reader.GetInt32(0);
            var columnName = reader.GetString(1);
            var rawType = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var notNull = reader.GetInt32(3) == 1;
            var defaultValue = reader.IsDBNull(4) ? null : reader.GetValue(4)?.ToString();

            columns.Add(new ColumnMetadata
            {
                ColumnName = columnName,
                DataType = BaseTypeName(rawType),
                MaxLength = ParseLength(rawType),
                IsNullable = !notNull,
                OrdinalPosition = cid + 1,
                ColumnDefault = defaultValue
            });
        }

        return Task.FromResult<IReadOnlyList<ColumnMetadata>>(columns);
    }

    public Task<string> GetDatabaseNameAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        // Columns: seq, name, file
        command.CommandText = "PRAGMA database_list";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(1);
            if (!string.Equals(name, "main", StringComparison.OrdinalIgnoreCase))
                continue;

            var file = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var result = string.IsNullOrEmpty(file)
                ? "main" // in-memory database has no backing file
                : Path.GetFileNameWithoutExtension(file);
            return Task.FromResult(string.IsNullOrEmpty(result) ? "main" : result);
        }

        return Task.FromResult("main");
    }

    public Task<IReadOnlyList<ForeignKeyInfo>> GetForeignKeysAsync(string schemaName, string tableName)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var results = new List<ForeignKeyInfo>();

        // Outgoing FKs: constraints defined on this table that reference other tables.
        results.AddRange(ReadForeignKeys(connection, tableName));

        // Incoming FKs: constraints on other tables that reference this table.
        // SQLite only reports a table's own FKs via PRAGMA foreign_key_list, so we
        // scan every other table to reproduce the SQL Server "OR referenced" behavior.
        foreach (var other in GetTableNames(connection))
        {
            if (string.Equals(other, tableName, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var fk in ReadForeignKeys(connection, other))
            {
                if (string.Equals(fk.ReferencedTable, tableName, StringComparison.OrdinalIgnoreCase))
                    results.Add(fk);
            }
        }

        return Task.FromResult<IReadOnlyList<ForeignKeyInfo>>(results);
    }

    private static List<ForeignKeyInfo> ReadForeignKeys(IDbConnection connection, string tableName)
    {
        var results = new List<ForeignKeyInfo>();

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list({QuoteIdentifier(tableName)})";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // Columns: id, seq, table, from, to, on_update, on_delete, match
            var id = reader.GetInt32(0);
            var referencedTable = reader.GetString(2);
            var fromColumn = reader.GetString(3);
            var toColumn = reader.IsDBNull(4) ? null : reader.GetString(4);

            // "to" is null when the FK targets the referenced table's primary key
            // implicitly; resolve it so downstream consumers get a real column name.
            toColumn ??= ResolvePrimaryKeyColumn(connection, referencedTable);

            results.Add(new ForeignKeyInfo
            {
                FkSchema = DefaultSchema,
                FkTable = tableName,
                FkColumn = fromColumn,
                ReferencedSchema = DefaultSchema,
                ReferencedTable = referencedTable,
                ReferencedColumn = toColumn ?? "",
                ConstraintName = $"FK_{tableName}_{referencedTable}_{id}"
            });
        }

        return results;
    }

    private static string? ResolvePrimaryKeyColumn(IDbConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)})";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // pk > 0 marks the column's position within the primary key.
            if (reader.GetInt32(5) > 0)
                return reader.GetString(1);
        }

        return null;
    }

    private static List<string> GetTableNames(IDbConnection connection)
    {
        var names = new List<string>();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
            ORDER BY name
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
            names.Add(reader.GetString(0));

        return names;
    }

    // SQLite type affinity strings can carry a size, e.g. "VARCHAR(50)".
    private static string BaseTypeName(string rawType)
    {
        var idx = rawType.IndexOf('(');
        return (idx >= 0 ? rawType[..idx] : rawType).Trim();
    }

    private static int? ParseLength(string rawType)
    {
        var match = LengthRegex().Match(rawType);
        return match.Success && int.TryParse(match.Groups[1].Value, out var length) ? length : null;
    }

    // Quote an identifier for interpolation into PRAGMA / non-parameterizable SQL.
    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";

    [GeneratedRegex(@"\(\s*(\d+)")]
    private static partial Regex LengthRegex();
}
