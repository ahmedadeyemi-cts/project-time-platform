using System.Diagnostics;
using System.Text.Json;
using System.IO.Compression;
using System.Text;
using System.Xml;
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
foreach (var formula in new[] { "=1+1", " +SUM(A1:A2)", "\t@SUM(A1)", "-cmd|' /C test'!A0", "\uFEFF=2+2", "\uFEFF \uFEFF=2+2" })
    Check(SafeExportText.Csv(formula).TrimStart('"').StartsWith("'"), "CSV formulas neutralized");
Check(SafeExportText.Csv("-12.50") == "-12.50", "Negative financial amounts preserved");
Check(SafeExportText.Csv("ordinary") == "ordinary", "Plain CSV text preserved");
Check(SafeExportText.Csv("a,\"b\"\r\nc") == "\"a,\"\"b\"\"\r\nc\"", "CSV quoting round trip");
foreach (var target in new[] { "javascript:alert(1)", "data:text/html,bad", "https://other.invalid", "//other.invalid", "/\\other.invalid", "\n/projects", "" })
    Check(!SafeExportText.IsInternalNavigation(target), "External and executable navigation denied");
foreach (var target in new[] { "#module066", "/projects/123", "/projects?tab=billing" })
    Check(SafeExportText.IsInternalNavigation(target), "Internal navigation preserved");
var routeId = Guid.NewGuid();
foreach (var format in new[] { "N", "D", "B", "P" })
    Check(CanonicalApiPaths.Normalize($"/api/admin/users/{routeId.ToString(format)}/") == $"/api/admin/users/{routeId:D}", "Equivalent GUID route spelling canonicalized");
Check(CanonicalApiPaths.Normalize("/api/auth/microsoft/callback///") == "/api/auth/microsoft/callback", "Trailing slashes canonicalized");
Check(CanonicalApiPaths.Normalize("/assets/resource/") == "/assets/resource/", "Non-API paths preserved");

MemoryStream Office(string name, string xml, int extraEntries = 0)
{
    var output = new MemoryStream();
    using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
    {
        using (var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8)) writer.Write(xml);
        for (int i = 0; i < extraEntries; i++) zip.CreateEntry($"extra/{i}");
    }
    output.Position = 0;
    return output;
}
void RejectOffice(string name, string xml, string label, int extra = 0)
{
    using var input = Office(name, xml, extra);
    try { BoundedOfficeInput.Validate(input); throw new Exception("Unsafe Office input accepted: " + label); }
    catch (Exception e) when (e is InvalidDataException or XmlException) { count++; }
    Check(input.Position == 0, "Rejected Office input restores stream position");
}
using (var input = Office("xl/worksheets/sheet1.xml", "<worksheet><dimension ref='A1:C10'/><sheetData><row r='1'><c r='A1'><v>12</v></c></row></sheetData></worksheet>"))
{
    BoundedOfficeInput.Validate(input);
    Check(input.CanRead && input.Position == 0, "Ordinary workbook admitted without consuming or closing stream");
}
RejectOffice("xl/worksheets/sheet1.xml", "<worksheet><dimension ref='A1:XFD1048576'/></worksheet>", "sparse maximum rectangle");
RejectOffice("xl/worksheets/sheet1.xml", "<worksheet><c r='ZZ9999'/></worksheet>", "far cell without dimension");
RejectOffice("xl/worksheets/sheet1.xml", "<worksheet><row r='10001'/></worksheet>", "oversized row");
RejectOffice("word/document.xml", string.Concat(Enumerable.Repeat("<p>", 66)) + string.Concat(Enumerable.Repeat("</p>", 66)), "nested document");
foreach (var name in new[] { "word/document.xml", "xl/_rels/workbook.xml.rels" })
    RejectOffice(name, "<!DOCTYPE x [<!ENTITY ext SYSTEM 'file:///etc/passwd'>]><x>&ext;</x>", "external entity");
RejectOffice("word/document.xml", "<p/>", "entry count", 2048);
RejectOffice("word/document.xml", "<p>" + new string('a', 2 * 1024 * 1024) + "</p>", "compressed expansion");
Console.WriteLine($"SECURITY_BOUNDARY_TESTS=PASS assertions={count}");
