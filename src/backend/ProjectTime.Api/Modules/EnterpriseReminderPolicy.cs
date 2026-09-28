using System.Globalization;
using System.Text.Json;

namespace ProjectTime.Api.Modules;

internal static class EnterpriseReminderPolicy
{
    internal static readonly string[] Codes = ["TIME_NOT_SUBMITTED", "TIME_NOT_SUBMITTED_ESCALATION", "COMPANY_HOLIDAY_UPCOMING", "PM_MONTH_END_REMINDER"];
    internal static DateTime LocalTime(DateTimeOffset now, JsonElement configuration) =>
        TimeZoneInfo.ConvertTime(now,global::ProjectTime.Api.SafeTimeZones.FindSystemTimeZoneById(Text(configuration,"timezone","America/Chicago"))).DateTime;
    internal static bool DueToday(string code, DateTime local, JsonElement configuration)
    {
        if (!TimeOnly.TryParse(Text(configuration,"localTime","06:00"),CultureInfo.InvariantCulture,DateTimeStyles.None,out var time)) return false;
        if (TimeOnly.FromDateTime(local)<time) return false;
        var day = Number(configuration,"dayOfWeek",code=="PM_MONTH_END_REMINDER" ? 5 : 1);
        if (code is "TIME_NOT_SUBMITTED" or "TIME_NOT_SUBMITTED_ESCALATION") return (int)local.DayOfWeek==day;
        if (code=="PM_MONTH_END_REMINDER") return (int)local.DayOfWeek==day && local.AddDays(7).Month!=local.Month;
        return code=="COMPANY_HOLIDAY_UPCOMING";
    }
    internal static DateOnly CompletedWeek(DateOnly today) => today.AddDays(-(int)today.DayOfWeek-7);
    internal static bool IsSubmitted(string? status) => status is "submitted" or "manager_approved" or "pm_approved" or "accounting_ready" or "reconciled" or "locked";
    internal static int[] HolidayOffsets(JsonElement config) => config.TryGetProperty("offsetDays",out var values) && values.ValueKind==JsonValueKind.Array
        ? values.EnumerateArray().Where(x=>x.TryGetInt32(out var d)&&d is >=1 and <=30).Select(x=>x.GetInt32()).Distinct().ToArray() : [7,1];
    internal static string Text(JsonElement config,string key,string fallback) => config.ValueKind==JsonValueKind.Object && config.TryGetProperty(key,out var value)
        && value.ValueKind==JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : fallback;
    private static int Number(JsonElement config,string key,int fallback) => config.ValueKind==JsonValueKind.Object && config.TryGetProperty(key,out var value)
        && value.TryGetInt32(out var result) && result is >=0 and <=6 ? result : fallback;
}
