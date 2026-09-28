using System.Globalization;

namespace ProjectTime.Api;

public static class SafeExportText
{
    public static string Csv(string? value)
    {
        var text = (value ?? string.Empty).Replace('\0', ' ');
        int first = 0;
        while (first < text.Length && (char.IsWhiteSpace(text[first]) || text[first] == '\uFEFF')) first++;
        var start = text[first..];
        if (start.Length > 0 && "=+-@".Contains(start[0]) &&
            !decimal.TryParse(start, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) text = "'" + text;
        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    public static bool IsInternalNavigation(string? target) =>
        !string.IsNullOrWhiteSpace(target) && target.Length <= 500 &&
        !target.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)) && !target.Contains('\\') &&
        (target.StartsWith('#') || target.StartsWith('/') && !target.StartsWith("//"));
}
