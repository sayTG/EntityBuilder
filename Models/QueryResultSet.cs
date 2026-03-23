namespace EntityBuilder.Models;

public class QueryResultSet
{
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public int TotalRowsReturned { get; set; }
    public long ExecutionTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsSuccess => ErrorMessage is null;
}
