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
    // ClosedXML preserves some unrecognized workbook extension metadata.
    // Remove the source document's SharePoint location and coauthor revision IDs.
    using (var archive = System.IO.Compression.ZipFile.Open(args[2], System.IO.Compression.ZipArchiveMode.Update))
    {
        var entry = archive.GetEntry("xl/workbook.xml")!;
        System.Xml.Linq.XDocument xml;
        using (var input = entry.Open()) xml = System.Xml.Linq.XDocument.Load(input);
        foreach (var node in xml.Root!.Elements().Where(n => n.Name.LocalName is "AlternateContent" or "revisionPtr").ToArray()) node.Remove();
        entry.Delete();
        using var destination = archive.CreateEntry("xl/workbook.xml").Open();
        xml.Save(destination);
    }
    Console.WriteLine("SANITIZED_TEMPLATE_CREATED");
    return;
}

if (args.Length >= 1 && args[0] == "--emit-samples")
{
    // Golden reference samples produced by the LIVE exporters offline (modelCalls=0)
    // from a fixed, non-customer fixture engagement. Deterministic ids/dates; Office
    // package metadata (timestamps) is normalized so a second run reproduces the samples.
    var dir = args.Length >= 2 ? args[1] : Path.Combine("docs", "samples", "module025");
    Directory.CreateDirectory(dir);
    var sample = BuildSampleModel();
    var haea = sample with { Engagement = sample.Engagement with { GsdTemplateKey = ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.HaeaGsdTemplateKey } };
    WriteSample(Path.Combine(dir, "sample-sow.docx"), ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateSowDocx(sample));
    WriteSample(Path.Combine(dir, "sample-gsd.xlsx"), ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateGsdXlsx(sample));
    WriteSample(Path.Combine(dir, "sample-gsd-haea.xlsx"), ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateGsdXlsx(haea));
    Console.WriteLine("MODULE025_SAMPLES_WRITTEN dir=" + dir + " modelCalls=0");
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
    using (var archive = new System.IO.Compression.ZipArchive(new MemoryStream(bytes)))
    {
        var xml = string.Join(" ", archive.Entries.Where(p => p.FullName.EndsWith(".xml") || p.FullName.EndsWith(".rels")).Select(p => { using var reader = new StreamReader(p.Open()); return reader.ReadToEnd(); }));
        foreach (var forbidden in new[] { "sharepoint.com", "Pourdre", "Poudre", "Stephanie", "McDonald", "Shaffer", "206140" })
            Check(!xml.Contains(forbidden, StringComparison.OrdinalIgnoreCase), "package metadata sanitized: " + forbidden);
    }
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
    var allocatedPhases = phases.Select(p => p with { Tasks = new[] {
        new ProjectTime.Api.Modules.Module025TaskEstimate(Guid.NewGuid().ToString(), "Reviewed discovery task", 0.1m, "INTERNAL NOTE ONLY"),
        new ProjectTime.Api.Modules.Module025TaskEstimate(Guid.NewGuid().ToString(), "Reviewed delivery task", p.FinalHours - 0.1m)
    } }).ToArray();
    var allocated = model with { Phases = allocatedPhases, Engagement = e with { Phases = allocatedPhases, AccountExecutiveUserId = Guid.NewGuid(), ResaleUserId = Guid.NewGuid() } };
    Check(ProjectTime.Api.Modules.Module025TaskEstimates.Readiness(allocated.Engagement) is null, "complete reviewed task estimate is confirmable");
    Check(ProjectTime.Api.Modules.Module025TaskEstimates.Readiness(allocated.Engagement with { ProjectName = "" }) is not null, "missing project blocks confirmation");
    var unknown = allocatedPhases[0].Tasks![0] with { Hours = null };
    Check(!ProjectTime.Api.Modules.Module025TaskEstimates.Complete(new[] { unknown }), "unknown hours remain incomplete");
    Check(ProjectTime.Api.Modules.Module025TaskEstimates.Complete(new[] { unknown with { Hours = 0 } }), "explicit zero hours are valid");
    Check(ProjectTime.Api.Modules.Module025TaskEstimates.Validate(new[] { unknown with { Hours = -1 } }) is not null, "negative hours rejected");
    Check(ProjectTime.Api.Modules.Module025TaskEstimates.Validate(new[] { unknown with { Hours = 0.001m } }) is not null, "fractional precision bounded");
    Check(ProjectTime.Api.Modules.Module025TaskEstimates.Validate(new[] { unknown, unknown }) is not null, "duplicate task ids rejected");
    var sections = System.Text.Json.JsonSerializer.SerializeToElement(new { reviewedTasks = new Dictionary<string, object> { ["plan"] = allocatedPhases[0].Tasks! } }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    Check(ProjectTime.Api.Modules.Module025TaskEstimates.Read(sections, "plan").SequenceEqual(allocatedPhases[0].Tasks!), "task JSON round trip preserves ids hours and notes");
    Check(ProjectTime.Api.Modules.Module025TaskEstimates.Read(json.RootElement, "plan").Count == 0, "legacy records load without invented allocations");
    var allocatedBytes = ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateGsdXlsx(allocated);
    using var allocatedBook = new XLWorkbook(new MemoryStream(allocatedBytes));
    Check(allocatedBook.Worksheet("Plan").Cell("A4").GetString() == "Reviewed discovery task", "GSD task descriptions mapped");
    Check(allocatedBook.Worksheet("Plan").Cell("B4").GetDouble() == 0.1, "GSD task hours mapped");
    Check(allocatedBook.Worksheet("Plan").Cell("G4").GetString() == "INTERNAL NOTE ONLY", "GSD notes retained internally");
    Check(allocatedBook.Worksheet("Summary").Cell("F4").GetDouble() == (double)model.FinalHours, "task totals roll up without duplicated phase allowance");
    var partialPhases = allocatedPhases.Select((p, i) => i == 0 ? p with { Tasks = new[] { unknown } } : p).ToArray();
    Check(ProjectTime.Api.Modules.Module025TaskEstimates.Readiness(allocated.Engagement with { Phases = partialPhases }) is not null, "partial allocations block final confirmation");
    using var draftBook = new XLWorkbook(new MemoryStream(ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateGsdXlsx(allocated with { Phases = partialPhases }, draft: true)));
    Check(draftBook.Worksheet("Summary").Cell("E1").GetString().StartsWith("DRAFT"), "GSD draft marked");
    Check(draftBook.Worksheet("Summary").Cell("F4").GetString() == "", "partial task totals do not become a misleading project total");
    var cleanDocx = ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateSowDocx(allocated);
    using (var cleanZip = new System.IO.Compression.ZipArchive(new MemoryStream(cleanDocx)))
    {
        using var reader = new StreamReader(cleanZip.GetEntry("word/document.xml")!.Open());
        var content = reader.ReadToEnd();
        Check(content.Contains("Execution Approach") && content.Contains("Review the agreed requirements"), "SOW contains high-level execution scope");
        Check(!content.Contains("Reviewed discovery task") && !content.Contains("Record the configuration decisions"), "detailed task estimates and technical instructions stay in the GSD");
        Check(!content.Contains("AI suggestion:") && !content.Contains("Level-of-effort rationale:") && !content.Contains("INTERNAL NOTE ONLY"), "customer SOW excludes internal estimating commentary");
    }
    var draftDocx = ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateSowDocx(allocated, draft: true);
    using (var draftZip = new System.IO.Compression.ZipArchive(new MemoryStream(draftDocx)))
    {
        using var reader = new StreamReader(draftZip.GetEntry("word/document.xml")!.Open());
        Check(reader.ReadToEnd().Contains("DRAFT - Not approved"), "SOW draft marked");
    }
    using var packageJson = System.Text.Json.JsonDocument.Parse("""
        [{"Name":"Production cutover", "Description":"Transition the customer service", "EstimatedHours":10,
          "EstimatedDurationDays":5,"DetailedSteps":["Prepare the change", "Perform production cutover", "Validate service"]}]
        """);
    var proposed = ProjectTime.Api.Modules.Module025TaskDrafts.FromWorkPackages(packageJson.RootElement.EnumerateArray(), "implement");
    Check(proposed.Count == 3 && proposed.Sum(task => task.Hours) == 10m, "generation materializes detailed tasks without another model call and preserves package effort");
    Check(proposed.Select(task => task.Hours).SequenceEqual(new decimal?[] { 3.34m, 3.33m, 3.33m }), "rounded task allocations reconcile exactly");
    Check(proposed.All(task => task.Reviewed == false && task.EstimateBasis!.Contains("equal allocation")), "proposed estimates never masquerade as SA-reviewed hours");
    Check(!ProjectTime.Api.Modules.Module025TaskEstimates.Complete(proposed), "generated proposal requires explicit SA review");
    Check(proposed.Any(task => task.AfterHoursSuggested) && proposed.All(task => !task.AfterHoursRequired && task.AfterHours == 0), "disruptive work suggests after-hours without inventing approved allocation or premium rate");
    Check(proposed.SequenceEqual(ProjectTime.Api.Modules.Module025TaskDrafts.FromWorkPackages(packageJson.RootElement.EnumerateArray(), "implement")), "repeated materialization produces stable task identities");
    var preserved = ProjectTime.Api.Modules.Module025TaskDrafts.PreserveExisting(allocatedPhases[0].Tasks, proposed);
    Check(preserved.SequenceEqual(allocatedPhases[0].Tasks!), "regeneration preserves all existing SA task edits");
    var editedProposal = new[] { proposed[0] with { Description = "SA has edited this unreviewed proposal" } };
    Check(ProjectTime.Api.Modules.Module025TaskDrafts.PreserveExisting(editedProposal, proposed).SequenceEqual(editedProposal), "even unreviewed SA edits survive regeneration");
    using var explicitJson = System.Text.Json.JsonDocument.Parse("""
        [{"name":"Prepare change","estimatedHours":7,"detailedSteps":[{"description":"Back up configuration","estimatedHours":2},{"description":"Test rollback","estimatedHours":5}]}]
        """);
    var explicitTasks = ProjectTime.Api.Modules.Module025TaskDrafts.FromWorkPackages(explicitJson.RootElement.EnumerateArray(), "plan");
    Check(explicitTasks.Select(task => task.Hours).SequenceEqual(new decimal?[] { 2m, 5m }), "explicit detailed-step effort is preserved");
    using var unknownJson = System.Text.Json.JsonDocument.Parse("""
        [{"Name":"Gather evidence","EstimatedDurationDays":3,"DetailedSteps":["Review inventory"]}]
        """);
    Check(ProjectTime.Api.Modules.Module025TaskDrafts.FromWorkPackages(unknownJson.RootElement.EnumerateArray(), "plan").Single().Hours is null, "elapsed duration never becomes invented labor hours");
    using var conflictJson = System.Text.Json.JsonDocument.Parse("""
        [{"name":"Prepare change","estimatedHours":9,"detailedSteps":[{"description":"Back up configuration","estimatedHours":2},{"description":"Test rollback","estimatedHours":5}]}]
        """);
    Check(ProjectTime.Api.Modules.Module025TaskDrafts.FromWorkPackages(conflictJson.RootElement.EnumerateArray(), "plan").All(task => task.Hours is null), "inconsistent explicit task estimates require review instead of silently changing effort");
    var splitTask = new ProjectTime.Api.Modules.Module025TaskEstimate(Guid.NewGuid().ToString(), "Perform approved production cutover", 6m,
        RegularHours: 2m, AfterHours: 4m, AfterHoursRequired: true, Reviewed: true);
    Check(ProjectTime.Api.Modules.Module025TaskEstimates.Validate(new[] { splitTask }) is null, "reviewed regular and after-hours split accepted");
    Check(ProjectTime.Api.Modules.Module025TaskEstimates.Validate(new[] { splitTask with { Hours = 7m } }) is not null, "mismatched split rejected");
    Check(ProjectTime.Api.Modules.Module025TaskEstimates.Validate(new[] { splitTask with { AfterHoursRequired = false } }) is not null, "after-hours allocation requires explicit designation");
    Check(!ProjectTime.Api.Modules.Module025TaskEstimates.Complete(new[] { splitTask with { RegularHours = 6m, AfterHours = 0m } }), "after-hours designation with no allocation blocks confirmation");
    var afterPhases = allocatedPhases.Select((phase, index) => index == 0 ? phase with { Tasks = new[] { splitTask }, FinalHours = 6m } : phase).ToArray();
    var afterModel = allocated with { Phases = afterPhases, Engagement = allocated.Engagement with { Phases = afterPhases } };
    using var afterBook = new XLWorkbook(new MemoryStream(ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateGsdXlsx(afterModel)));
    Check(afterBook.Worksheet("Plan").Cell("B4").GetDouble() == 2 && afterBook.Worksheet("Plan").Cell("C4").GetDouble() == 4, "standard GSD separates regular and after-hours effort");
    Check(afterBook.Worksheet("Summary").Cell("F4").GetDouble() == (double)afterPhases.Sum(phase => phase.FinalHours), "after-hours included exactly once in project total");
    using var afterSpecial = new XLWorkbook(new MemoryStream(ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateGsdXlsx(afterModel with { Engagement = afterModel.Engagement with { GsdTemplateKey = ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.HaeaGsdTemplateKey } })));
    Check(afterSpecial.Worksheet("Task Estimates").Cell("D2").GetDouble() == 2 && afterSpecial.Worksheet("Task Estimates").Cell("E2").GetDouble() == 4, "Toyota Hyundai GSD retains detailed task split");
    var proposalPhase = afterPhases[0] with { Tasks = proposed, FinalHours = 10m };
    Check(ProjectTime.Api.Modules.Module025TaskEstimates.PhaseReadiness(proposalPhase)!.Contains("task 1"), "confirmation identifies the exact task needing review");
    Check(ProjectTime.Api.Modules.Module025TaskEstimates.Readiness(afterModel.Engagement with {
        GsdTemplateKey = ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.HaeaGsdTemplateKey,
        Phases = afterPhases.Select((phase, index) => index == 0 ? proposalPhase : phase).ToArray()
    }) is not null, "Toyota Hyundai proposals require SA review too");
    var proposalModel = allocated with { Phases = allocatedPhases.Select((phase, index) => index == 0 ? proposalPhase : phase).ToArray() };
    using var proposalBook = new XLWorkbook(new MemoryStream(ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateGsdXlsx(proposalModel, draft: true)));
    Check(proposalBook.Worksheet("Plan").Cell("G2").GetString().Contains("Review and accept"), "draft workbook exposes pending SA review");
    Check(proposalBook.Worksheet("Summary").Cell("E7").GetString() == "Working Phase Hours", "proposed project total is not labeled as reviewed");
    Check(proposalBook.Worksheet("Plan").Cell("G4").GetString().Contains("PROPOSED") && proposalBook.Worksheet("Plan").Cell("G4").GetString().Contains("equal allocation"), "draft workbook carries honest estimate provenance");
    Directory.CreateDirectory(output);
    File.WriteAllBytes(Path.Combine(output, "Task-GSD.xlsx"), allocatedBytes);
    File.WriteAllBytes(Path.Combine(output, "Task-SOW.docx"), cleanDocx);
    File.WriteAllBytes(Path.Combine(output, "Draft-SOW.docx"), draftDocx);
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
    // Real typed serialization and real exporters for the Python installed-gate
    // contract regression. These files are offline synthetic fixtures, not UAT evidence.
    var gatePhases = phases.Select(phase => phase with {
        SuggestedHours = 0.3m, FinalHours = 0.3m,
        Objective = "Synthetic reviewed " + phase.PhaseCode + " scope",
        DetailedActivities = new[] { "Synthetic " + phase.PhaseCode + " execution approach" },
        TechnicalTasks = new[] { "INTERNAL_EXPORT_TECHNICAL_TASK " + phase.PhaseCode },
        Deliverables = new[] { "Synthetic " + phase.PhaseCode + " deliverable" },
        AcceptanceCriteria = new[] { "Synthetic " + phase.PhaseCode + " acceptance" },
        Tasks = new[] {
            new ProjectTime.Api.Modules.Module025TaskEstimate(Guid.NewGuid().ToString(), "Synthetic reviewed " + phase.PhaseCode + " task", 0.1m,
                "INTERNAL_EXPORT_TEST_NOTE", RegularHours: 0.05m, AfterHours: 0.05m, AfterHoursRequired: true,
                AfterHoursReason: "Synthetic after-hours designation for export acceptance.", Reviewed: true,
                EstimateBasis: "Synthetic reviewed labor allocation."),
            new ProjectTime.Api.Modules.Module025TaskEstimate(Guid.NewGuid().ToString(), "Synthetic second " + phase.PhaseCode + " task", 0.2m, "")
        }
    }).ToArray();
    var gateModel = model with { Phases = gatePhases, FinalHours = 1.5m, SuggestedHours = 1.5m,
        Engagement = e with { Phases = gatePhases } };
    File.WriteAllBytes(Path.Combine(output, "Acceptance-Contract-SOW.docx"), ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateSowDocx(gateModel, draft: true));
    File.WriteAllBytes(Path.Combine(output, "Acceptance-Contract-GSD.xlsx"), ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateGsdXlsx(gateModel, draft: true));
    File.WriteAllText(Path.Combine(output, "Acceptance-Contract-Tasks.json"), System.Text.Json.JsonSerializer.Serialize(gatePhases,
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));

    // ===================================================================================
    // Standards-spec assertions — one per stable id in
    // docs/module025-sow-gsd-output-standards.md. Each is checked against FRESHLY generated
    // live-exporter output from the golden fixture (the same fixture the samples use). Drift
    // in any guarantee fails this run. Each assertion is tagged with its id (e.g. [SOW-FONT-01]).
    // ===================================================================================
    var std = BuildSampleModel();
    var stdSow = ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateSowDocx(std);
    using (var sowZip = new System.IO.Compression.ZipArchive(new MemoryStream(stdSow)))
    {
        string SowPart(string name) { using var reader = new StreamReader(sowZip.GetEntry(name)!.Open()); return reader.ReadToEnd(); }
        var stylesXml = SowPart("word/styles.xml");
        // [SOW-FONT-01]
        Check(stylesXml.Contains("w:ascii=\"Arial\"") && stylesXml.Contains("w:color w:val=\"000000\"") && stylesXml.Contains("w:sz w:val=\"22\""),
            "[SOW-FONT-01] SOW body is Arial, black 000000, 11pt (w:sz=22)");
        // [SOW-FONT-01]
        Check(stylesXml.Contains("w:styleId=\"Title\"") && stylesXml.Contains("w:sz w:val=\"38\"") && stylesXml.Contains("w:sz w:val=\"26\"")
            && stylesXml.Contains("w:sz w:val=\"30\"") && stylesXml.Contains("w:sz w:val=\"24\""),
            "[SOW-FONT-01] SOW heading sizes Title 19 / Subtitle 13 / H1 15 / H2 12 pt");
        // [SOW-LETTERHEAD-01]
        Check(sowZip.GetEntry("word/media/us-signal.png") != null
            && SowPart("word/header1.xml").Contains("rIdLogo")
            && SowPart("word/_rels/header1.xml.rels").Contains("media/us-signal.png"),
            "[SOW-LETTERHEAD-01] SOW header embeds the US Signal PNG letterhead");
        var footerXml = SowPart("word/footer1.xml");
        // [SOW-FOOTER-01]
        Check(footerXml.Contains("US Signal | Statement of Work | Page") && footerXml.Contains("w:jc w:val=\"right\"") && footerXml.Contains("w:instr=\"PAGE\""),
            "[SOW-FOOTER-01] right-aligned footer 'US Signal | Statement of Work | Page N'");
        var documentXml = SowPart("word/document.xml");
        // [SOW-PAGE-01]
        Check(documentXml.Contains("w:w=\"12240\"") && documentXml.Contains("w:h=\"15840\""),
            "[SOW-PAGE-01] US Letter page size (12240 x 15840 twips)");
        int PhaseAt(string label) => documentXml.IndexOf("xml:space=\"preserve\">" + label + "</w:t>", StringComparison.Ordinal);
        var order = new[] { PhaseAt("Plan"), PhaseAt("Design"), PhaseAt("Implement"), PhaseAt("Validate"), PhaseAt("Release") };
        // [SOW-PHASES-01]
        Check(order.All(index => index > 0) && order.Zip(order.Skip(1), (a, b) => a < b).All(ok => ok),
            "[SOW-PHASES-01] phase Heading1 order Plan -> Design -> Implement -> Validate -> Release");
        // [SOW-PHASES-01]
        Check(new[] { "Execution Approach", "Deliverables", "US Signal Responsibilities", "Customer Responsibilities",
                "Prerequisites", "Dependencies", "Assumptions", "Acceptance Criteria", "Validation Steps", "Risks / Considerations" }
            .All(sub => documentXml.Contains(sub)),
            "[SOW-PHASES-01] each phase carries the standard subsections");
        // [SOW-DRAFT-01]
        Check(!documentXml.Contains("DRAFT - Not approved"), "[SOW-DRAFT-01] confirmed SOW carries no draft marker");
    }
    using (var sowDraftZip = new System.IO.Compression.ZipArchive(new MemoryStream(ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateSowDocx(std, draft: true))))
    {
        using var reader = new StreamReader(sowDraftZip.GetEntry("word/document.xml")!.Open());
        // [SOW-DRAFT-01]
        Check(reader.ReadToEnd().Contains("DRAFT - Not approved"), "[SOW-DRAFT-01] draft SOW shows 'DRAFT - Not approved' marker");
    }
    var expectedSheets = new[] { "Summary", "Phase Breakdown", "Totals Sheet", "SELL SKUs", "Plan", "Design", "Implement", "Validate", "Release", "Architect Notes", "Gotcha Items", "Assumptions Responsibilities" };
    using (var stdGsd = new XLWorkbook(new MemoryStream(ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateGsdXlsx(std))))
    {
        // [GSD-SHEETS-01]
        Check(stdGsd.Worksheets.Select(s => s.Name).SequenceEqual(expectedSheets), "[GSD-SHEETS-01] standard 12-sheet template layout verbatim");
        // [GSD-FONT-01]
        Check(stdGsd.Worksheets.All(s => s.Style.Font.FontName == "Arial"), "[GSD-FONT-01] whole-workbook Arial font");
        var totalsSheet = stdGsd.Worksheet("Totals Sheet");
        // [GSD-GUARDS-01]
        Check(totalsSheet.Cell("B3").GetString() == "RESOURCE ALLOCATION REQUIRES REVIEW"
            && totalsSheet.Cell("B29").GetString() == "PRICING REQUIRES APPROVED RATES"
            && stdGsd.Worksheet("SELL SKUs").Cell("D5").GetString().Contains("No SKU, rate or price is inferred")
            && stdGsd.Worksheet("Phase Breakdown").Cell("B8").GetString() == "Role allocation pending",
            "[GSD-GUARDS-01] review/rate guard cells present, no invented allocations or rates");
        // [GSD-RECONCILE-01]
        Check(stdGsd.Worksheet("Summary").Cell("F4").GetDouble() == (double)std.FinalHours,
            "[GSD-RECONCILE-01] reviewed hours reconcile without template surcharges");
    }
    using (var haeaGsd = new XLWorkbook(new MemoryStream(ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateGsdXlsx(
        std with { Engagement = std.Engagement with { GsdTemplateKey = ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.HaeaGsdTemplateKey } }))))
    {
        // [GSD-HAEA-01]
        Check(haeaGsd.Worksheet(1).Name == "HAEA GSD", "[GSD-HAEA-01] HAEA program generates from-scratch layout");
    }
    var partialPhasesStd = std.Phases.Select((phase, index) => index == 0
        ? phase with { Tasks = new[] { new ProjectTime.Api.Modules.Module025TaskEstimate(new Guid("00000000-0000-0000-0000-0000000000c3").ToString(), "Pending task allocation", null) } }
        : phase).ToArray();
    var partialStd = std with { Phases = partialPhasesStd, Engagement = std.Engagement with { Phases = partialPhasesStd } };
    using (var draftGsd = new XLWorkbook(new MemoryStream(ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateGsdXlsx(partialStd, draft: true))))
    {
        // [GSD-DRAFT-01]
        Check(draftGsd.Worksheet("Summary").Cell("E1").GetString().StartsWith("DRAFT")
            && draftGsd.Worksheet("Summary").Cell("F4").GetString() == "",
            "[GSD-DRAFT-01] draft marks Summary!E1 DRAFT and blanks partial totals");
    }
    using (var injGsd = new XLWorkbook(new MemoryStream(ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateGsdXlsx(
        std with { Engagement = std.Engagement with { CustomerName = "=HYPERLINK(\"https://example.invalid\")" } }))))
    {
        // [SAFETY-INJECTION-01]
        Check(!injGsd.Worksheet("Summary").Cell("C11").HasFormula, "[SAFETY-INJECTION-01] formula-looking customer text stored as a literal");
    }
    var stdGsdBytes = ProjectTime.Api.Modules.Module025SowGsdDocumentExporter.CreateGsdXlsx(std);
    using (var leakArchive = new System.IO.Compression.ZipArchive(new MemoryStream(stdGsdBytes)))
    using (var leakBook = new XLWorkbook(new MemoryStream(stdGsdBytes)))
    {
        var packageXml = string.Join(" ", leakArchive.Entries.Where(p => p.FullName.EndsWith(".xml") || p.FullName.EndsWith(".rels")).Select(p => { using var reader = new StreamReader(p.Open()); return reader.ReadToEnd(); }));
        var cellText = string.Join(" ", leakBook.Worksheets.SelectMany(s => s.CellsUsed()).Where(c => !c.HasFormula).Select(c => c.GetString()));
        // [SAFETY-LEAK-01]
        Check(new[] { "sharepoint.com", "Poudre", "Stephanie", "McDonald", "Shaffer", "206140", "OneNeck", "Dashboard 1 migration" }
            .All(marker => !packageXml.Contains(marker, StringComparison.OrdinalIgnoreCase) && !cellText.Contains(marker, StringComparison.OrdinalIgnoreCase)),
            "[SAFETY-LEAK-01] no reference/customer data leakage in package or cells");
    }

    Console.WriteLine("MODULE025_EXPORT_TESTS=PASS modelCalls=0");
}

static ProjectTime.Api.Modules.Module025DocumentModel BuildSampleModel()
{
    using var json = System.Text.Json.JsonDocument.Parse("{}");
    var empty = json.RootElement.Clone();
    string[] codes = { "plan", "design", "implement", "validate", "release" };
    var phases = codes.Select((code, i) => new ProjectTime.Api.Modules.Module025PhaseRow(
        code, i, 8m, 12m + i,
        "Deliver the reviewed " + code + " scope for the sample engagement.",
        new[] { "Review the agreed " + code + " requirements", "Complete the approved " + code + " activities" },
        new[] { "Record the " + code + " configuration decisions" },
        new[] { "Reviewed " + code + " delivery record" },
        new[] { "Provide the agreed " + code + " services and review the results" },
        new[] { "Provide access and confirm the " + code + " outcomes" },
        new[] { "Confirm the " + code + " prerequisites are in place" },
        new[] { "Depends on completion of the prior-phase deliverables" },
        new[] { "Assumes the reviewed " + code + " scope is unchanged" },
        Array.Empty<string>(),
        new[] { "Customer reviews the documented " + code + " results" },
        new[] { "Validate the " + code + " outcome against the acceptance criteria" },
        new[] { "Schedule risk if the " + code + " prerequisites are delayed" },
        "Reviewed phase estimate; task allocation pending.",
        Array.Empty<int>(), false, DateTimeOffset.UnixEpoch)).ToArray();
    var e = new ProjectTime.Api.Modules.Module025EngagementRow(
        new Guid("00000000-0000-0000-0000-0000000000a1"), "SOW-SAMPLE-0001",
        new Guid("00000000-0000-0000-0000-0000000000b2"), "Sample Solution Architect", "Architecture", "Delivery", null,
        "Sample Engagement Customer", "manual", "fixed", "standard", "standard_gsd", null,
        "Sample Account Executive", null, "Sample Inside Sales",
        "Deliver the agreed project scope using the five delivery phases.",
        "Sample Platform Modernization", empty, empty,
        "review_ready", true, 2, null, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, phases);
    return new ProjectTime.Api.Modules.Module025DocumentModel(e, phases, 40m, phases.Sum(p => p.FinalHours));
}

static void WriteSample(string path, byte[] bytes)
{
    File.WriteAllBytes(path, bytes);
    NormalizeOfficeZip(path);
}

// OpenXML/ClosedXML embed nondeterministic package metadata: creation/modification
// timestamps, per-entry zip mod times, and — for the from-scratch package writer — a
// random-GUID ".psmdcp" core-properties part (referenced from _rels/.rels). Normalize
// all of those to fixed values so a second sample run reproduces the committed bytes;
// the exporters' own document content is already deterministic for a fixed fixture.
static void NormalizeOfficeZip(string path)
{
    var fixedTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    const string psmdcpFixed = "package/services/metadata/core-properties/core.psmdcp";
    var entries = new List<(string Name, byte[] Data)>();
    string? psmdcpOriginal = null;
    using (var source = System.IO.Compression.ZipFile.OpenRead(path))
        foreach (var entry in source.Entries)
        {
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            if (entry.FullName.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase)) psmdcpOriginal = entry.FullName;
            entries.Add((entry.FullName, memory.ToArray()));
        }
    System.Xml.Linq.XNamespace dcterms = "http://purl.org/dc/terms/";
    using var output = new FileStream(path, FileMode.Create);
    using var archive = new System.IO.Compression.ZipArchive(output, System.IO.Compression.ZipArchiveMode.Create);
    foreach (var (originalName, originalData) in entries)
    {
        var name = originalName;
        var data = originalData;
        // Fix the timestamps in either core-properties part (docProps/core.xml or the .psmdcp).
        if (name == "docProps/core.xml" || name.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase))
        {
            System.Xml.Linq.XDocument xml;
            using (var input = new MemoryStream(data)) xml = System.Xml.Linq.XDocument.Load(input);
            foreach (var node in xml.Descendants(dcterms + "created").Concat(xml.Descendants(dcterms + "modified")))
                node.Value = "1980-01-01T00:00:00Z";
            using var buffer = new MemoryStream();
            xml.Save(buffer);
            data = buffer.ToArray();
        }
        // Give the random-GUID core-properties part a stable name and repoint its relationship.
        if (psmdcpOriginal != null && name == psmdcpOriginal) name = psmdcpFixed;
        // The root package rels of a from-scratch workbook carry random psmdcp names and
        // random Relationship ids that nothing references by id (they resolve by Type).
        // Stabilize both so the sample is reproducible; worksheet rels are left untouched.
        if (name == "_rels/.rels")
        {
            System.Xml.Linq.XDocument xml;
            using (var input = new MemoryStream(data)) xml = System.Xml.Linq.XDocument.Load(input);
            var index = 1;
            foreach (var relationship in xml.Descendants().Where(node => node.Name.LocalName == "Relationship"))
            {
                var target = relationship.Attribute("Target");
                if (target != null && target.Value.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase))
                    target.Value = "/" + psmdcpFixed;
                relationship.SetAttributeValue("Id", "rId" + index++);
            }
            using var buffer = new MemoryStream();
            xml.Save(buffer);
            data = buffer.ToArray();
        }
        var entry = archive.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
        entry.LastWriteTime = fixedTime;
        using var stream = entry.Open();
        stream.Write(data);
    }
}

static void Check(bool result, string message)
{
    if (!result) throw new Exception(message);
    Console.WriteLine("PASS " + message);
}
