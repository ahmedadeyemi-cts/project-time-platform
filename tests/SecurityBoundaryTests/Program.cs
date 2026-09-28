using System.Diagnostics;
using System.Text.Json;
using ProjectTime.Api;
using ProjectTime.Api.Modules;

int count = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); count++; }
Check(SafeTimeZones.FindSystemTimeZoneById("UTC").BaseUtcOffset == TimeSpan.Zero, "UTC accepted");
Check(SafeTimeZones.FindSystemTimeZoneById("America/Los_Angeles").BaseUtcOffset == TimeSpan.FromHours(-8), "IANA accepted");
Check(SafeTimeZones.FindSystemTimeZoneById("Pacific Standard Time").BaseUtcOffset == TimeSpan.FromHours(-8), "Windows alias accepted");
Check(SafeTimeZones.ConvertTimeBySystemTimeZoneId(new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc), "America/Los_Angeles").Hour == 5, "DST preserved");
var watch = Stopwatch.StartNew();
foreach (var id in new[] { "/dev/zero", "/dev/random", "../../../../dev/zero", "/etc/passwd", "UTC\0", "UTC\n", new string('a', 129), "", "not/a/timezone" })
{
    try { SafeTimeZones.FindSystemTimeZoneById(id); throw new Exception("Unsafe timezone accepted"); }
    catch (TimeZoneNotFoundException) { count++; }
}
Check(watch.Elapsed < TimeSpan.FromSeconds(2), "Invalid paths rejected without blocking IO");
bool Valid(object data) => BackupConfigurationSafety.Valid(JsonSerializer.SerializeToElement(data));
Check(Valid(new { sftpEnabled = true, sftpHost = "backup.example.invalid", sftpUser = "backup", sftpPort = "22", sftpRemotePath = "/Project backups/September", sftpAuthMode = "private_key" }), "Normal backup settings accepted");
foreach (var path in new[] { "/\n!touch /tmp/unsafe", "/\r!id", "/\0", "/../etc", "!id" })
    Check(!Valid(new { sftpEnabled = true, sftpHost = "backup.example.invalid", sftpUser = "backup", sftpRemotePath = path }), "Unsafe backup path rejected");
Check(!Valid(new { sftpEnabled = true, sftpHost = "-oProxyCommand=touch", sftpUser = "backup", sftpRemotePath = "/" }), "Host options rejected");
Check(!Valid(new { sftpEnabled = false, sftpPassword = "secret\nexport BAD=1" }), "Disabled settings still reject controls");
Check(Valid(new { sftpEnabled = false, sftpPassword = "a'b= $(literal) `literal`" }), "Literal secrets preserved");
var directory = Path.Combine(Path.GetTempPath(), "security-boundary-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var file = Path.Combine(directory, "settings.env");
    var target = Path.Combine(directory, "unrelated");
    await File.WriteAllTextAsync(target, "unchanged");
    File.CreateSymbolicLink(file, target);
    await BackupConfigurationSafety.WritePrivateLinesAsync(file, new[] { "KEY='secret'" });
    Check(await File.ReadAllTextAsync(target) == "unchanged", "Symlink target not overwritten");
    Check((await File.ReadAllTextAsync(file)).Trim() == "KEY='secret'", "Atomic literal write");
    if (!OperatingSystem.IsWindows())
        Check(File.GetUnixFileMode(file) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "Secret file mode is 0600");
    try { await BackupConfigurationSafety.WritePrivateLinesAsync(file, new[] { "KEY='bad\nline'" }); throw new Exception("Multiline write allowed"); }
    catch (ArgumentException) { count++; }
    Check((await File.ReadAllTextAsync(file)).Trim() == "KEY='secret'", "Rejected update preserves previous settings");
}
finally { Directory.Delete(directory, true); }
Console.WriteLine($"SECURITY_BOUNDARY_TESTS=PASS assertions={count}");
