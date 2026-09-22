using EntityBuilder.Interfaces;
using EntityBuilder.Models;
using EntityBuilder.Utilities;

namespace EntityBuilder.Services;

public class ReportScheduleWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReportScheduleWorker> _logger;

    public ReportScheduleWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ReportScheduleWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReportScheduleWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueReportsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ReportScheduleWorker loop.");
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }

    private async Task ProcessDueReportsAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var scheduleService = scope.ServiceProvider.GetRequiredService<IReportScheduleService>();
        var emailService = scope.ServiceProvider.GetRequiredService<IReportEmailService>();

        var dueReports = await scheduleService.GetDueReportsAsync();
        if (dueReports.Count == 0) return;

        _logger.LogInformation("Found {Count} due report(s) to process.", dueReports.Count);

        foreach (var report in dueReports)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                var request = new ReportEmailRequest
                {
                    Sql = report.Sql,
                    RecipientEmail = report.RecipientEmail,
                    DisplayName = report.DisplayName,
                    Subject = report.Subject,
                    DapperTemplateValues = report.DapperTemplateValues
                };

                var result = await emailService.SendReportAsync(request);

                if (result.Code == 1)
                {
                    report.LastRun = DateTime.UtcNow;
                    if (report.Frequency == ReportFrequency.Once)
                    {
                        report.Status = ReportStatus.Sent;
                    }
                    else
                    {
                        report.NextRun = ScheduleNextRunCalculator.Compute(report, DateTime.UtcNow);
                    }
                    _logger.LogInformation("Report {Id} sent successfully.", report.Id);
                }
                else
                {
                    report.Status = ReportStatus.Failed;
                    _logger.LogWarning("Report {Id} failed: {Reason}", report.Id, result.ShortDescription);
                }
            }
            catch (Exception ex)
            {
                report.Status = ReportStatus.Failed;
                _logger.LogError(ex, "Exception sending report {Id}.", report.Id);
            }

            await scheduleService.UpdateReportAsync(report);
        }
    }

}
