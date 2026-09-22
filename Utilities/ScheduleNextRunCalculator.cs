using EntityBuilder.Models;

namespace EntityBuilder.Utilities;

// Shared next-run computation used by initial scheduling, edits, and the recurring worker
// so all three paths honour the same rules (frequency + time + day + user's UTC offset).
public static class ScheduleNextRunCalculator
{
    public static DateTime Compute(ScheduledReport report, DateTime nowUtc)
    {
        var timeParts = (string.IsNullOrWhiteSpace(report.ScheduledTime) ? "08:00" : report.ScheduledTime).Split(':');
        var hour = int.Parse(timeParts[0]);
        var minute = timeParts.Length > 1 ? int.Parse(timeParts[1]) : 0;
        var offset = report.UtcOffsetMinutes;

        switch (report.Frequency)
        {
            case ReportFrequency.Once:
                if (!string.IsNullOrEmpty(report.ScheduledDate) && DateOnly.TryParse(report.ScheduledDate, out var date))
                    return date.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc).AddMinutes(offset);
                return nowUtc.Date.AddDays(1).AddHours(hour).AddMinutes(minute).AddMinutes(offset);

            case ReportFrequency.Daily:
                var nextDaily = nowUtc.Date.AddHours(hour).AddMinutes(minute).AddMinutes(offset);
                if (nextDaily <= nowUtc) nextDaily = nextDaily.AddDays(1);
                return nextDaily;

            case ReportFrequency.Weekly:
                var targetDow = report.DayOfWeek ?? 1;
                var nextWeekly = nowUtc.Date.AddHours(hour).AddMinutes(minute).AddMinutes(offset);
                while ((int)nextWeekly.DayOfWeek != targetDow || nextWeekly <= nowUtc)
                    nextWeekly = nextWeekly.AddDays(1);
                return nextWeekly;

            case ReportFrequency.Monthly:
                var targetDom = report.DayOfMonth ?? 1;
                var nextMonthly = new DateTime(nowUtc.Year, nowUtc.Month,
                    Math.Min(targetDom, DateTime.DaysInMonth(nowUtc.Year, nowUtc.Month)),
                    hour, minute, 0, DateTimeKind.Utc).AddMinutes(offset);
                if (nextMonthly <= nowUtc) nextMonthly = nextMonthly.AddMonths(1);
                return nextMonthly;

            default:
                return nowUtc.AddDays(1);
        }
    }
}
