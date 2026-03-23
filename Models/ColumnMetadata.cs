namespace EntityBuilder.Models;

public class ColumnMetadata
{
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public int? MaxLength { get; set; }
    public bool IsNullable { get; set; }
    public int OrdinalPosition { get; set; }
    public string? ColumnDefault { get; set; }
}
