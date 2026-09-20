using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ProjectTime.Api.Modules;

var assembly = typeof(Module025SowGsdModule).Assembly;
var packageType = assembly.GetType("ProjectTime.Api.Modules.Module025TemplatePackage")!;
var validate = packageType.GetMethod("Validate", BindingFlags.NonPublic | BindingFlags.Static)!;
var previewMethod = packageType.GetMethod("Preview", BindingFlags.NonPublic | BindingFlags.Static)!;
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
JsonElement Preview(string kind, string name, byte[] bytes, string? sheetId = null) => JsonSerializer.SerializeToElement(
    previewMethod.Invoke(null, [kind, name, bytes, sheetId]), new JsonSerializerOptions(JsonSerializerDefaults.Web));
byte[] Package(string kind, Action<ZipArchive>? alter = null, string? mainXml = null, string? firstWorksheetXml = null)
{
    using var memory = new MemoryStream();
    using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
    {
        var mainPart = kind == "sow" ? "word/document.xml" : "xl/workbook.xml";
        var contentType = kind == "sow" ? "wordprocessingml.document" : "spreadsheetml.sheet";
        Add(archive, "[Content_Types].xml", $"<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Override PartName='/{mainPart}' ContentType='application/vnd.openxmlformats-officedocument.{contentType}.main+xml'/></Types>");
        Add(archive, "_rels/.rels", $"<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='{mainPart}'/></Relationships>");
        Add(archive, mainPart, mainXml ?? (kind == "sow" ? "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body/></w:document>" : "<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Summary' sheetId='1' r:id='rId1'/></sheets></workbook>"));
        if (kind == "gsd")
        {
            Add(archive, "xl/_rels/workbook.xml.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet' Target='worksheets/sheet1.xml'/><Relationship Id='rId2' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet' Target='worksheets/sheet2.xml'/></Relationships>");
            Add(archive, "xl/worksheets/sheet1.xml", firstWorksheetXml ?? "<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row r='1'><c r='A1'><v>4</v></c><c r='B1'><f>SUM(A1:A2)</f><v>4</v></c></row></sheetData></worksheet>");
        }
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
Check(!Validate("gsd", "external.xlsx", Package("gsd", archive => Add(archive, "xl/worksheets/_rels/sheet1.xml.rels", "<Relationships><Relationship TargetMode='External' Target='https://example.com/data'/></Relationships>"))).Valid, "external relationships are refused");
Check(!Validate("gsd", "hidden-external.xlsx", Package("gsd", archive => Add(archive, "xl/worksheets/_rels/sheet1.xml.rels", "<Relationships><Relationship Target='https://example.com/data'/></Relationships>"))).Valid, "absolute external target is refused even without TargetMode");
Check(!Validate("gsd", "dtd.xlsx", Package("gsd", archive => Add(archive, "docProps/custom.xml", "<!DOCTYPE test [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><test>&x;</test>"))).Valid, "XML entity expansion and DTDs are refused");
Check(!Validate("gsd", "expanded.xlsx", Package("gsd", archive => Add(archive, "large.xml", new string('x', 8 * 1024 * 1024 + 1)))).Valid, "over-expanded zip member is refused before parsing");

var workbookPreview = Preview("gsd", "Standard GSD.xlsx", original);
Check(workbookPreview.GetProperty("valid").GetBoolean() && workbookPreview.GetProperty("selectedSheetId").GetString() == "1", "preview selects the workbook's declared first visible sheet");
Check(workbookPreview.GetProperty("sheets")[0].GetProperty("name").GetString() == "Summary", "preview exposes the original sheet name");
var formulaCell = workbookPreview.GetProperty("cells")[1];
Check(formulaCell.GetProperty("address").GetString() == "B1" && formulaCell.GetProperty("formula").GetString() == "SUM(A1:A2)"
    && formulaCell.GetProperty("value").GetString() == "4", "formula expression and saved cached value remain distinct");
Check(SHA256.HashData(original).SequenceEqual(originalHash), "preview never mutates or recalculates the original workbook");
Check(!Preview("gsd", "Standard GSD.xlsx", original, "unknown").GetProperty("valid").GetBoolean(), "unknown sheet identifier never falls back to an unrelated worksheet");
Check(!Preview("gsd", "bad.xlsx", [1, 2, 3]).GetProperty("valid").GetBoolean(), "preview reuses package validation before reading content");
var multiSheet = Package("gsd", archive => Add(archive, "xl/worksheets/sheet2.xml", "<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row r='2'><c r='C2' t='inlineStr'><is><t>Visible plan</t></is></c></row></sheetData></worksheet>"),
    mainXml: "<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Hidden formulas' state='hidden' sheetId='1' r:id='rId1'/><sheet name='Plan' sheetId='2' r:id='rId2'/></sheets></workbook>");
var selectedVisible = Preview("gsd", "multi.xlsx", multiSheet);
Check(selectedVisible.GetProperty("selectedSheetId").GetString() == "2" && selectedVisible.GetProperty("cells")[0].GetProperty("value").GetString() == "Visible plan", "sheet relationships resolve the first visible sheet rather than ZIP entry order");
Check(Preview("gsd", "multi.xlsx", multiSheet, "1").GetProperty("sheets")[0].GetProperty("visibility").GetString() == "hidden", "hidden sheets remain explicitly labeled and selectable for formula review");
var stringWorkbook = Package("gsd", archive => Add(archive, "xl/sharedStrings.xml", "<sst xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><si><r><t>Customer </t></r><r><t>&lt;script&gt;alert(1)&lt;/script&gt;</t></r></si></sst>"), firstWorksheetXml:
    "<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row r='1'><c r='A1' t='s'><v>0</v></c><c r='B1' t='inlineStr'><is><t>Inline note</t></is></c><c r='C1' t='b'><v>1</v></c><c r='D1'><f t='shared' si='7'/><v>2</v></c><c r='E1'/></row></sheetData></worksheet>");
var strings = Preview("gsd", "strings.xlsx", stringWorkbook).GetProperty("cells");
Check(strings[0].GetProperty("value").GetString() == "Customer <script>alert(1)</script>" && strings[1].GetProperty("value").GetString() == "Inline note", "rich/shared and inline strings are returned as plain text without HTML interpretation");
Check(strings[2].GetProperty("value").GetString() == "TRUE" && strings[3].GetProperty("formula").GetString() == "Shared formula group 7", "boolean values and shared formula followers are explicit without inventing calculations");
Check(strings.GetArrayLength() == 4, "empty formatted cells do not crowd out meaningful template content");
var oversizedCells = Package("gsd", firstWorksheetXml: "<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row>" + string.Concat(Enumerable.Range(1, 301).Select(index => $"<c r='A{index}'><v>{index}</v></c>")) + "</row></sheetData></worksheet>");
var boundedCells = Preview("gsd", "bounded.xlsx", oversizedCells);
Check(boundedCells.GetProperty("cells").GetArrayLength() == 300 && boundedCells.GetProperty("truncated").GetBoolean(), "worksheet preview output is bounded with an explicit truncation notice");
var document = Package("sow", mainXml: "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:pPr><w:pStyle w:val='Heading1'/></w:pPr><w:r><w:t>Scope &amp; approach</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>Plan</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Review requirements</w:t></w:r></w:p></w:tc></w:tr></w:tbl><w:sdt><w:sdtContent><w:p><w:r><w:t>&lt;img src=x onerror=alert(1)&gt;</w:t></w:r></w:p></w:sdtContent></w:sdt></w:body></w:document>");
var documentHash = SHA256.HashData(document);
var blocks = Preview("sow", "SOW.docx", document).GetProperty("blocks");
Check(blocks.GetArrayLength() == 3 && blocks[0].GetProperty("style").GetString() == "Heading1" && blocks[0].GetProperty("text").GetString() == "Scope & approach", "Word paragraphs, styles, tables, and content controls retain document order");
Check(blocks[1].GetProperty("rows")[0][1].GetString() == "Review requirements" && blocks[2].GetProperty("text").GetString() == "<img src=x onerror=alert(1)>", "Word table cell text and HTML-looking text remain inert strings");
Check(SHA256.HashData(document).SequenceEqual(documentHash), "Word content preview preserves the exact original bytes");
var longDocument = Package("sow", mainXml: "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body>" + string.Concat(Enumerable.Range(1, 201).Select(index => $"<w:p><w:r><w:t>Paragraph {index}</w:t></w:r></w:p>")) + "</w:body></w:document>");
var boundedDocument = Preview("sow", "Long.docx", longDocument);
Check(boundedDocument.GetProperty("blocks").GetArrayLength() == 200 && boundedDocument.GetProperty("truncated").GetBoolean(), "Word preview stops at its documented block bound");
var unicodeDocument = Package("sow", mainXml: "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:r><w:t>" + new string('a', 2499) + "😀 trailing text</w:t></w:r></w:p></w:body></w:document>");
var unicodePreview = Preview("sow", "Unicode.docx", unicodeDocument);
var clippedText = unicodePreview.GetProperty("blocks")[0].GetProperty("text").GetString()!;
Check(unicodePreview.GetProperty("truncated").GetBoolean() && clippedText.Length == 2499 && !char.IsHighSurrogate(clippedText[^1]), "preview truncation never splits a Unicode surrogate pair");
var textBudgetDocument = Package("sow", mainXml: "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body>" + string.Concat(Enumerable.Repeat("<w:p><w:r><w:t>" + new string('a', 2000) + "</w:t></w:r></w:p>", 100)) + "</w:body></w:document>");
var budgetPreview = Preview("sow", "Text budget.docx", textBudgetDocument);
Check(budgetPreview.GetProperty("truncated").GetBoolean() && budgetPreview.GetProperty("blocks").EnumerateArray().Sum(block => block.GetProperty("text").GetString()!.Length) <= 80000, "combined document preview text has a global response budget");

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
