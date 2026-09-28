using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectTime.Api.Modules;

public static class BackupConfigurationSafety
{
    public static bool Valid(JsonElement request)
    {
        if (request.ValueKind != JsonValueKind.Object) return false;
        foreach (var item in request.EnumerateObject())
            if (item.Value.ValueKind == JsonValueKind.String &&
                (item.Value.GetString() is not { Length: <= 8192 } text || text.Any(char.IsControl))) return false;
        string Text(string name, string fallback = "") => request.TryGetProperty(name, out var value)
            ? value.ToString() : fallback;
        if (!bool.TryParse(Text("sftpEnabled"), out var enabled) || !enabled) return true;
        return Regex.IsMatch(Text("sftpHost"), @"\A[A-Za-z0-9][A-Za-z0-9.-]{0,252}\z", RegexOptions.CultureInvariant)
            && Regex.IsMatch(Text("sftpUser"), @"\A[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}\z", RegexOptions.CultureInvariant)
            && int.TryParse(Text("sftpPort", "22"), out var port) && port is > 0 and <= 65535
            && Text("sftpAuthMode", "private_key") is "private_key" or "password"
            && Text("sftpRemotePath") is { Length: > 0 and <= 1024 } path && path.StartsWith('/')
            && !path.Split('/').Contains("..");
    }

    public static async Task WritePrivateLinesAsync(string path, IEnumerable<string> lines)
    {
        var validated = lines.ToArray();
        if (validated.Any(line => line.Any(char.IsControl)))
            throw new ArgumentException("Backup settings cannot contain control characters.", nameof(lines));
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write,
                Share = FileShare.None, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(temporary, options))
            await using (var writer = new StreamWriter(stream))
                foreach (var line in validated) await writer.WriteLineAsync(line);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
