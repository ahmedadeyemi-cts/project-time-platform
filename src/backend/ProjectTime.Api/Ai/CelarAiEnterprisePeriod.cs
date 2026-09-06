using System.Globalization;
using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

public sealed record CelarAiEnterprisePeriod(DateOnly Start, DateOnly End)
{
    // Exact ISO dates or closed calendar phrases only. Unrecognized periods
    // must never silently become the current week.
    public static CelarAiEnterprisePeriod? Parse(string question, string? timeZone, DateTimeOffset? now = null)
    {
        try { return ParseCore(question,timeZone,now); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static CelarAiEnterprisePeriod? ParseCore(string question, string? timeZone, DateTimeOffset? now)
    {
        var instant = now ?? DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(timeZone))
        {
            try { instant = TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(timeZone)); }
            catch (TimeZoneNotFoundException) { return null; }
            catch (InvalidTimeZoneException) { return null; }
        }
        var today = DateOnly.FromDateTime(instant.DateTime);
        var text = question.ToLowerInvariant();
        var dates = Regex.Matches(text, @"\b\d{4}-\d{2}-\d{2}\b");
        var relativePeriods = Regex.Matches(text,@"\b(?:today|yesterday|(?:this|last) (?:week|month|quarter|year))\b");
        if (relativePeriods.Count>1 || (relativePeriods.Count>0 && dates.Count>0)) return null;
        DateOnly start, end;
        if (dates.Count > 0)
        {
            if (dates.Count > 2 || !DateOnly.TryParseExact(dates[0].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start)) return null;
            end = start;
            if (dates.Count == 2 && !DateOnly.TryParseExact(dates[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out end)) return null;
            if (dates.Count == 1 && text.Contains("week")) { start = start.AddDays(-(int)start.DayOfWeek); end = start.AddDays(6); }
        }
        else if (text.Contains("yesterday")) start = end = today.AddDays(-1);
        else if (text.Contains("today")) start = end = today;
        else if (text.Contains("last month") || text.Contains("this month"))
        {
            start = new DateOnly(today.Year, today.Month, 1);
            if (text.Contains("last month")) start = start.AddMonths(-1);
            end = start.AddMonths(1).AddDays(-1);
        }
        else if (text.Contains("last quarter") || text.Contains("this quarter"))
        {
            start = new DateOnly(today.Year, ((today.Month - 1) / 3) * 3 + 1, 1);
            if (text.Contains("last quarter")) start = start.AddMonths(-3);
            end = start.AddMonths(3).AddDays(-1);
        }
        else if (text.Contains("last year") || text.Contains("this year"))
        {
            start = new DateOnly(today.Year - (text.Contains("last year") ? 1 : 0), 1, 1);
            end = start.AddYears(1).AddDays(-1);
        }
        else if (text.Contains("this week") || text.Contains("last week"))
        {
            start = today.AddDays(-(int)today.DayOfWeek - (text.Contains("last week") ? 7 : 0));
            end = start.AddDays(6);
        }
        else return null;
        return end < start || end.DayNumber - start.DayNumber > 365 ? null : new(start, end);
    }
}
