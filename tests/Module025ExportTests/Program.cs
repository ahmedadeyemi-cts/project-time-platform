using ClosedXML.Excel;
if (args.Length == 3 && args[0] == "--prepare-template")
{
    using var book = new XLWorkbook(args[1]);
    var summary = book.Worksheet("Summary");
    foreach (var address in new[] { "B1", "C4", "C5", "C6", "C7", "C8", "C9", "C10", "C11", "C12", "C13", "C14", "C15", "C16", "C17", "C18", "C19", "C20", "C21", "B24", "F11", "F13", "F15" })
        summary.Cell(address).Clear(XLClearOptions.Contents);
    summary.Range("W4:W7").Clear(XLClearOptions.Contents);
    summary.Range("Y4:Y7").Clear(XLClearOptions.Contents);
    summary.Range("G20:G25").Clear(XLClearOptions.Contents);
    summary.Range("N6:N13").Clear(XLClearOptions.Contents);
    summary.Range("W16:W19").Clear(XLClearOptions.Contents);
    summary.Cell("B18").Value = "US Signal MS Client";
    summary.Cell("B17").Value = "ConnectWise SELL Quote #";
    summary.Cell("I4").Value = "Review the customer, project and assigned team. Phase tabs contain saved scope and reviewed phase hours. Task hours, engineering roles, overtime, reserve, travel and billing rates require explicit review; blank values are unknown. Prices and SKUs are not inferred from the example workbook.";
    foreach (var name in new[] { "Plan", "Design", "Implement", "Validate", "Release" })
    {
        var sheet = book.Worksheet(name);
        sheet.Range("A2:G99").Clear(XLClearOptions.Contents);
        sheet.Range("B102:D119").Clear(XLClearOptions.Contents);
    }
    foreach (var name in new[] { "Architect Notes", "Gotcha Items", "Assumptions Responsibilities" })
        book.Worksheet(name).CellsUsed().Clear(XLClearOptions.Contents);
    var breakdown = book.Worksheet("Phase Breakdown");
    foreach (var cell in breakdown.CellsUsed().ToArray())
        if (cell.HasFormula || cell.DataType == XLDataType.Number) cell.Clear(XLClearOptions.Contents);
    var totals = book.Worksheet("Totals Sheet");
    foreach (var cell in totals.CellsUsed().ToArray())
        if (cell.HasFormula || cell.DataType == XLDataType.Number) cell.Clear(XLClearOptions.Contents);
    var skus = book.Worksheet("SELL SKUs");
    skus.Range("A4:I32").Clear(XLClearOptions.Contents);
    skus.Cell("A1").Value = "ConnectWise SELL SKUs";
    skus.Cell("D4").Value = "Select approved SKUs in ConnectWise SELL";
    skus.Cell("E5").Value = "No SKU, rate or price is inferred from the reference workbook.";
    book.Properties.Author = "US Signal";
    book.Properties.LastModifiedBy = "US Signal";
    book.Properties.Title = "Standard General Services Delivery Worksheet";
    book.Properties.Subject = "Module 025 standard export template";
    book.Properties.Company = "US Signal";
    book.Properties.Manager = "";
    book.Properties.Comments = "";
    book.Properties.Keywords = "";
    foreach (var prop in book.CustomProperties.ToArray()) book.CustomProperties.Delete(prop.Name);
    Directory.CreateDirectory(Path.GetDirectoryName(args[2])!);
    book.SaveAs(args[2]);
    Console.WriteLine("SANITIZED_TEMPLATE_CREATED");
    return;
}

RunExportTests(args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "module025-exports"));

static void RunExportTests(string output)
{
    using var json = System.Text.Json.JsonDocument.Parse("{}");
    var phases = new[] { "plan", "design", "implement", "validate", "release" }.Select((code, i) =>
        new ProjectTime.Api.Modules.Module025PhaseRow(code, i, 8, 10.5m + i, "Deliver the reviewed " + code + " scope.",
            new[] { "Review the agreed requirements", "Complete the approved " + code + " activities" },
            new[] { "Record the configuration decisions" }, new[] { "Reviewed delivery record" },
            new[] { "Provide access and review results" }, new[] { "Complete the agreed services" },
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            new[] { "Customer reviews the documented results" }, Array.Empty<string>(), Array.Empty<string>(),
            "Reviewed phase estimate; task allocation pending.", Array.Empty<int>(), false, DateTimeOffset.UnixEpoch)).ToArray();
    var e = new ProjectTime.Api.Modules.Module025EngagementRow(Guid.NewGuid(), "SOW-SYNTHETIC", Guid.NewGuid(), "Example SA", "Architecture", "Delivery", null,
        "Example Customer", "manual", "fixed", "standard", "standard_gsd", null, "Example AE", null, "Example Inside Sales",
        "Deliver the agreed project scope using the five delivery phases.", "Example Platform Project", json.RootElement.Clone(), json.RootElement.Clone(),
        "review_ready", true, 2, null, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, phases);
    var model = new ProjectTime.Api.Modules.Module025DocumentModel(e, phases, 40, phases.Sum(p => p.FinalHours));
    var bytes = ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateGsdXlsx(model);
    using var book = new XLWorkbook(new MemoryStream(bytes));
    Check(book.Worksheets.Select(s => s.Name).SequenceEqual(new[] { "Summary", "Phase Breakdown", "Totals Sheet", "SELL SKUs", "Plan", "Design", "Implement", "Validate", "Release", "Architect Notes", "Gotcha Items", "Assumptions Responsibilities" }), "standard 12-sheet layout");
    var summary = book.Worksheet("Summary");
    foreach (var (cell, value) in new[] { ("C11", "Example Customer"), ("C12", "Example Platform Project"), ("C13", "FP"), ("C14", "Example AE"), ("C15", "Example SA"), ("C16", "Example Inside Sales") })
        Check(summary.Cell(cell).GetString() == value, "metadata " + cell);
    Check(summary.Cell("F4").GetDouble() == (double)model.FinalHours, "reviewed hours reconcile without template surcharges");
    Check(book.Worksheet("Plan").Cell("B4").GetDouble() == 10.5, "fractional phase hours preserved");
    Check(book.Worksheet("Plan").Cell("B5").IsEmpty(), "no invented task allocations");
    Check(book.Worksheet("Plan").Cell("E4").IsEmpty(), "no invented engineering role");
    Check(summary.Cell("F15").IsEmpty() && summary.Cell("W4").IsEmpty(), "no inherited prices or rates");
    Check(summary.Pictures.Count == 1, "US Signal workbook logo");
    var leakText = string.Join(" ", book.Worksheets.SelectMany(s => s.CellsUsed()).Where(c => !c.HasFormula).Select(c => c.GetString()));
    foreach (var secret in new[] { "Poudre", "Stephanie", "McDonald", "Shaffer", "206140", "Dashboard 1 migration", "OneNeck" })
        Check(!leakText.Contains(secret, StringComparison.OrdinalIgnoreCase), "reference customer data removed: " + secret);
    var longPhases = phases.Select((p, i) => i == 0 ? p with { DetailedActivities = Enumerable.Range(0, 125).Select(n => "Task " + n).ToArray(), TechnicalTasks = Array.Empty<string>() } : p).ToArray();
    using var large = new XLWorkbook(new MemoryStream(ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateGsdXlsx(model with { Phases = longPhases })));
    Check(large.Worksheet("Plan").Cell("A129").GetString() == "Task 124", "all tasks retained beyond template capacity");
    Check(large.Worksheet("Summary").Cell("F4").GetDouble() == (double)model.FinalHours, "expanded phase totals reconcile");
    Check(large.Worksheet("Plan").PageSetup.PrintAreas.Single().RangeAddress.LastAddress.RowNumber == 129, "expanded print area covers every task");
    using var safe = new XLWorkbook(new MemoryStream(ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateGsdXlsx(model with { Engagement = e with { CustomerName = "=HYPERLINK(\"https://example.invalid\")", CommercialModel = "time_materials", AccountExecutiveName = "", ResaleName = "" } })));
    Check(!safe.Worksheet("Summary").Cell("C11").HasFormula, "formula-looking customer is literal text");
    Check(safe.Worksheet("Summary").Cell("C13").GetString() == "T&M", "time and materials contract mapping");
    Check(safe.Worksheet("Summary").Cell("C14").IsEmpty() && safe.Worksheet("Summary").Cell("C16").IsEmpty(), "missing contacts stay blank");
    using var special = new XLWorkbook(new MemoryStream(ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateGsdXlsx(model with { Engagement = e with { GsdTemplateKey = ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.HaeaGsdTemplateKey } })));
    Check(special.Worksheet(1).Name == "HAEA GSD", "existing HAEA profile preserved");
    var docx = ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateSowDocx(model);
    using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(docx));
    foreach (var part in zip.Entries.Where(p => p.FullName.EndsWith(".xml") || p.FullName.EndsWith(".rels")))
        using (var stream = part.Open()) System.Xml.Linq.XDocument.Load(stream);
    Check(zip.GetEntry("word/media/us-signal.png") != null, "embedded SOW logo");
    using var stylesReader = new StreamReader(zip.GetEntry("word/styles.xml")!.Open());
    var styles = stylesReader.ReadToEnd();
    Check(styles.Contains("Arial") && styles.Contains("000000"), "consistent black SOW typography");
    Directory.CreateDirectory(output);
    File.WriteAllBytes(Path.Combine(output, "Standard-GSD.xlsx"), bytes);
    File.WriteAllBytes(Path.Combine(output, "US-Signal-SOW.docx"), docx);
    Console.WriteLine("MODULE025_EXPORT_TESTS=PASS modelCalls=0");
}

static void Check(bool result, string message)
{
    if (!result) throw new Exception(message);
    Console.WriteLine("PASS " + message);
}
