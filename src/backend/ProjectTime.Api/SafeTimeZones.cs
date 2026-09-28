namespace ProjectTime.Api;

/// <summary>Resolve user-supplied identifiers only from the installed time-zone catalog.
/// FindSystemTimeZoneById may read arbitrary paths on Unix; never pass request data to it.</summary>
public static class SafeTimeZones
{
    private static readonly IReadOnlyDictionary<string, TimeZoneInfo> Zones =
        TimeZoneInfo.GetSystemTimeZones().Append(TimeZoneInfo.Utc)
            .GroupBy(zone => zone.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

    public static TimeZoneInfo FindSystemTimeZoneById(string id)
    {
        if (id is not null && id.Length <= 128 && !id.Any(char.IsControl))
        {
            if (Zones.TryGetValue(id, out var zone)) return zone;
            // Conversion is a catalog mapping, not a filesystem lookup. Preserve
            // Windows aliases used by existing clients on Linux deployments.
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) &&
                Zones.TryGetValue(iana, out zone)) return zone;
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windows) &&
                Zones.TryGetValue(windows, out zone)) return zone;
        }
        throw new TimeZoneNotFoundException("The time-zone identifier is not in the installed catalog.");
    }

    public static DateTime ConvertTimeBySystemTimeZoneId(DateTime dateTime, string id) =>
        TimeZoneInfo.ConvertTime(dateTime, FindSystemTimeZoneById(id));
}
