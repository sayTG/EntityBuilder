namespace EntityBuilder.Models;

public class ForeignKeyInfo
{
    public string FkSchema { get; set; } = string.Empty;
    public string FkTable { get; set; } = string.Empty;
    public string FkColumn { get; set; } = string.Empty;
    public string ReferencedSchema { get; set; } = string.Empty;
    public string ReferencedTable { get; set; } = string.Empty;
    public string ReferencedColumn { get; set; } = string.Empty;
    public string ConstraintName { get; set; } = string.Empty;
}
