using EntityBuilder.Models;

namespace EntityBuilder.Interfaces;

public interface IReportEmailService
{
    Task<ApiResponse<object>> SendReportAsync(ReportEmailRequest request);
}
