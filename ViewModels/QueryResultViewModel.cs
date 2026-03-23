using EntityBuilder.Models;

namespace EntityBuilder.ViewModels;

public class QueryResultViewModel
{
    public string Sql { get; set; } = string.Empty;
    public QueryResultSet? Result { get; set; }
}
