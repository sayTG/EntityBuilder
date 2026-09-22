using System.ComponentModel.DataAnnotations;

namespace EntityBuilder.Models;

public class QueryBuilderRequest
{
    [Required]
    public string Schema { get; set; } = string.Empty;

    [Required]
    public string Table { get; set; } = string.Empty;

    public List<JoinDefinition> Joins { get; set; } = new();
    public List<WhereCondition> WhereConditions { get; set; } = new();
    public List<ColumnSelection> SelectedColumns { get; set; } = new();
    public List<GroupByColumn> GroupByColumns { get; set; } = new();
    public List<AggregateColumn> AggregateColumns { get; set; } = new();
    public List<OrderByColumn> OrderByColumns { get; set; } = new();

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class JoinDefinition
{
    [Required]
    public string JoinType { get; set; } = "INNER JOIN";

    [Required]
    public string Schema { get; set; } = string.Empty;

    [Required]
    public string Table { get; set; } = string.Empty;

    [Required]
    public string LeftColumn { get; set; } = string.Empty;

    [Required]
    public string RightColumn { get; set; } = string.Empty;
}

public class WhereCondition
{
    [Required]
    public string Column { get; set; } = string.Empty;

    [Required]
    public string Operator { get; set; } = "=";

    public string? Value { get; set; }

    public string Connector { get; set; } = "AND";

    // Kind of value to use at execution time.
    // "Literal" (default) uses Value as a parameter; "Now" inlines the DB's current-datetime function
    // so scheduled reports pick fresh time on every run instead of freezing it at build time.
    public string ValueKind { get; set; } = "Literal";
}

public class ColumnSelection
{
    public string Schema { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
    public string Column { get; set; } = string.Empty;
}

public class GroupByColumn
{
    public string Schema { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
    public string Column { get; set; } = string.Empty;
}

public class AggregateColumn
{
    public string Function { get; set; } = "COUNT";
    public string Schema { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
    public string Column { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
}

public class OrderByColumn
{
    public string Column { get; set; } = string.Empty;
    public string Direction { get; set; } = "ASC";
}
