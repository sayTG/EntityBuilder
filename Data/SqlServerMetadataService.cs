using System.Data;
using EntityBuilder.Interfaces;
using EntityBuilder.Models;

namespace EntityBuilder.Data;

public class SqlServerMetadataService : IDatabaseMetadataService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqlServerMetadataService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<TableInfo>> GetTablesAsync()
    {
        const string sql = """
            SELECT
                t.TABLE_SCHEMA,
                t.TABLE_NAME,
                ISNULL(SUM(p.rows), 0) AS [RowCount]
            FROM INFORMATION_SCHEMA.TABLES t
            LEFT JOIN sys.tables st
                ON st.name = t.TABLE_NAME
                AND SCHEMA_NAME(st.schema_id) = t.TABLE_SCHEMA
            LEFT JOIN sys.partitions p
                ON p.object_id = st.object_id
                AND p.index_id IN (0, 1)
            WHERE t.TABLE_TYPE = 'BASE TABLE'
            GROUP BY t.TABLE_SCHEMA, t.TABLE_NAME
            ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME
            """;

        var tables = new List<TableInfo>();
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            tables.Add(new TableInfo
            {
                SchemaName = reader.GetString(0),
                TableName = reader.GetString(1),
                RowCount = reader.GetInt64(2)
            });
        }

        return tables;
    }

    public async Task<IReadOnlyList<ColumnMetadata>> GetColumnsAsync(string schemaName, string tableName)
    {
        const string sql = """
            SELECT
                COLUMN_NAME,
                DATA_TYPE,
                CHARACTER_MAXIMUM_LENGTH,
                CASE WHEN IS_NULLABLE = 'YES' THEN 1 ELSE 0 END,
                ORDINAL_POSITION,
                COLUMN_DEFAULT
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @Table
            ORDER BY ORDINAL_POSITION
            """;

        var columns = new List<ColumnMetadata>();
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var schemaParam = command.CreateParameter();
        schemaParam.ParameterName = "@Schema";
        schemaParam.Value = schemaName;
        command.Parameters.Add(schemaParam);

        var tableParam = command.CreateParameter();
        tableParam.ParameterName = "@Table";
        tableParam.Value = tableName;
        command.Parameters.Add(tableParam);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(new ColumnMetadata
            {
                ColumnName = reader.GetString(0),
                DataType = reader.GetString(1),
                MaxLength = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                IsNullable = reader.GetInt32(3) == 1,
                OrdinalPosition = reader.GetInt32(4),
                ColumnDefault = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }

        return columns;
    }

    public async Task<string> GetDatabaseNameAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DB_NAME()";

        var result = command.ExecuteScalar();
        return result?.ToString() ?? "Unknown";
    }
}
