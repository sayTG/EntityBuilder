using EntityBuilder.Models;

namespace EntityBuilder.ViewModels;

public class TableListViewModel
{
    public IReadOnlyList<TableInfo> Tables { get; set; } = Array.Empty<TableInfo>();
    public string DatabaseName { get; set; } = string.Empty;
}
