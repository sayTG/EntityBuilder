namespace EntityBuilder.Models;

public class QueryResultSet
{
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public int TotalRowsReturned { get; set; }
    public long ExecutionTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? GeneratedSql { get; set; }
    public bool IsSuccess => ErrorMessage is null;

    public int TotalRows { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalRows / PageSize) : 0;
}
