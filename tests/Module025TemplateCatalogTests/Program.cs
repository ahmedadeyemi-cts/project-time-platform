using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using ProjectTime.Api.Modules;

var assembly = typeof(Module025SowGsdModule).Assembly;
var packageType = assembly.GetType("ProjectTime.Api.Modules.Module025TemplatePackage")!;
var validate = packageType.GetMethod("Validate", BindingFlags.NonPublic | BindingFlags.Static)!;
var checks = 0;
void Check(bool value, string label)
{
    if (!value) throw new InvalidOperationException("FAILED: " + label);
    checks++;
    Console.WriteLine("PASS: " + label);
}
(bool Valid, int Formulas, int Worksheets) Validate(string kind, string name, byte[] bytes)
{
    var result = validate.Invoke(null, [kind, name, bytes])!;
    return ((bool)result.GetType().GetProperty("Valid")!.GetValue(result)!,
        (int)result.GetType().GetProperty("FormulaCount")!.GetValue(result)!,
        (int)result.GetType().GetProperty("WorksheetCount")!.GetValue(result)!);
}
byte[] Package(string kind, Action<ZipArchive>? alter = null)
{
    using var memory = new MemoryStream();
    using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
    {
        var mainPart = kind == "sow" ? "word/document.xml" : "xl/workbook.xml";
        var contentType = kind == "sow" ? "wordprocessingml.document" : "spreadsheetml.sheet";
        Add(archive, "[Content_Types].xml", $"<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Override PartName='/{mainPart}' ContentType='application/vnd.openxmlformats-officedocument.{contentType}.main+xml'/></Types>");
        Add(archive, "_rels/.rels", $"<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='{mainPart}'/></Relationships>");
        Add(archive, mainPart, kind == "sow" ? "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body/></w:document>" : "<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheets/></workbook>");
        if (kind == "gsd") Add(archive, "xl/worksheets/sheet1.xml", "<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row r='1'><c r='A1'><v>4</v></c><c r='B1'><f>SUM(A1:A2)</f><v>4</v></c></row></sheetData></worksheet>");
        alter?.Invoke(archive);
    }
    return memory.ToArray();
}
void Add(ZipArchive archive, string name, string contents)
{
    using var writer = new StreamWriter(archive.CreateEntry(name).Open());
    writer.Write(contents);
}

var original = Package("gsd");
var originalHash = SHA256.HashData(original);
var result = Validate("gsd", "Standard GSD.xlsx", original);
Check(result.Valid && result.Formulas == 1 && result.Worksheets == 1, "workbook structure and existing formula cells are recognized");
Check(SHA256.HashData(original).SequenceEqual(originalHash), "validation preserves the exact original file bytes");
Check(Validate("sow", "Standard SOW.docx", Package("sow")).Valid, "Word document candidate is accepted");
Check(!Validate("sow", "fake.docx", original).Valid, "renaming an Excel file does not make it a Word template");
Check(!Validate("gsd", "macros.xlsm", original).Valid, "macro-enabled extension is refused");
Check(!Validate("gsd", "../outside.xlsx", original).Valid, "path in download filename is refused");
Check(!Validate("gsd", "broken.xlsx", [1, 2, 3]).Valid, "invalid zip is refused");
Check(!Validate("gsd", "empty.xlsx", []).Valid, "empty upload is refused");
Check(!Validate("gsd", "large.xlsx", new byte[4 * 1024 * 1024 + 1]).Valid, "compressed upload size is bounded");
Check(!Validate("gsd", "embedded.xlsx", Package("gsd", archive => Add(archive, "xl/embeddings/item1.bin", "object"))).Valid, "embedded objects are refused");
Check(!Validate("gsd", "macro.xlsx", Package("gsd", archive => Add(archive, "xl/vbaProject.bin", "macro"))).Valid, "macro payload with xlsx extension is refused");
Check(!Validate("gsd", "duplicate.xlsx", Package("gsd", archive => Add(archive, "xl/workbook.xml", "<workbook/>"))).Valid, "ambiguous duplicate package entries are refused");
Check(!Validate("gsd", "traversal.xlsx", Package("gsd", archive => Add(archive, "../elsewhere.xml", "<x/>"))).Valid, "package traversal path is refused");
Check(!Validate("gsd", "external.xlsx", Package("gsd", archive => Add(archive, "xl/_rels/workbook.xml.rels", "<Relationships><Relationship TargetMode='External' Target='https://example.com/data'/></Relationships>"))).Valid, "external relationships are refused");
Check(!Validate("gsd", "hidden-external.xlsx", Package("gsd", archive => Add(archive, "xl/_rels/workbook.xml.rels", "<Relationships><Relationship Target='https://example.com/data'/></Relationships>"))).Valid, "absolute external target is refused even without TargetMode");
Check(!Validate("gsd", "dtd.xlsx", Package("gsd", archive => Add(archive, "docProps/custom.xml", "<!DOCTYPE test [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><test>&x;</test>"))).Valid, "XML entity expansion and DTDs are refused");
Check(!Validate("gsd", "expanded.xlsx", Package("gsd", archive => Add(archive, "large.xml", new string('x', 8 * 1024 * 1024 + 1)))).Valid, "over-expanded zip member is refused before parsing");

var accessType = assembly.GetType("ProjectTime.Api.Modules.Module025AccessContext")!;
var canStage = typeof(Module025SowGsdModule).GetMethod("CanStageTemplate", BindingFlags.NonPublic | BindingFlags.Static)!;
var user = Guid.NewGuid();
bool CanStage(bool manager = false, bool admin = false, bool viewAs = false, bool hasReports = false)
{
    var constructor = accessType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Single(info => info.GetParameters().Length > 1);
    var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["ActualUserId"] = user, ["EffectiveUserId"] = user, ["DisplayName"] = "Test User", ["Email"] = "test@example.invalid",
        ["DepartmentName"] = "Engineering", ["TeamName"] = "Systems", ["Roles"] = new HashSet<string>(),
        ["IsViewAs"] = viewAs, ["IsAdministrator"] = admin, ["IsSolutionArchitect"] = true, ["IsProtectedTestUatRoleFixture"] = false,
        ["IsManager"] = manager, ["VisibleSolutionArchitectIds"] = hasReports ? new HashSet<Guid> { user, Guid.NewGuid() } : new HashSet<Guid> { user }
    };
    var access = constructor.Invoke(constructor.GetParameters().Select(parameter => values[parameter.Name!]).ToArray());
    return (bool)canStage.Invoke(null, [access])!;
}
Check(!CanStage(), "individual SA cannot stage shared templates");
Check(!CanStage(manager: true), "manager role without SA reporting scope cannot stage templates");
Check(CanStage(manager: true, hasReports: true), "manager with current SA reports can stage team templates");
Check(CanStage(admin: true), "administrator can stage organizational templates");
Check(!CanStage(manager: true, hasReports: true, viewAs: true) && !CanStage(admin: true, viewAs: true), "View As always denies staging for managers and administrators");
Console.WriteLine($"{checks} template catalog checks passed.");
