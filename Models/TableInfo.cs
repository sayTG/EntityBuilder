namespace EntityBuilder.Models;

public class TableInfo
{
    public string SchemaName { get; set; } = "dbo";
    public string TableName { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public string FullName => $"{SchemaName}.{TableName}";
}
