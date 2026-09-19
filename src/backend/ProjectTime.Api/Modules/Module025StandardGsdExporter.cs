using ClosedXML.Excel;

namespace ProjectTime.Api.Modules;

// Uses the customer's standard workbook layout. Export is deterministic mapping,
// not a model operation. Missing commercial/task allocation facts stay unknown.
internal static class Module025StandardGsdExporter
{
    internal static byte[] Create(Module025DocumentModel model, bool draft = false)
    {
        using var template = typeof(Module025StandardGsdExporter).Assembly.GetManifestResourceStream(
            "ProjectTime.Api.Assets.Templates.Module025StandardGsd.xlsx")
            ?? throw new InvalidOperationException("The standard GSD export template is missing.");
        using var book = new XLWorkbook(template);
        var e = model.Engagement;
        var summary = book.Worksheet("Summary");
        if (draft) summary.Cell("E1").Value = "DRAFT - General Services Delivery Worksheet";
        summary.Cell("C11").Value = e.CustomerName;
        summary.Cell("C12").Value = e.ProjectName ?? "";
        summary.Cell("C13").Value = e.CommercialModel == "fixed" ? "FP" : "T&M";
        summary.Cell("C14").Value = e.AccountExecutiveName;
        summary.Cell("C15").Value = e.OwnerDisplayName;
        summary.Cell("C16").Value = e.ResaleName;
        summary.Cell("B24").Value = e.ServiceOverview;
        summary.Cell("B24").Style.Alignment.WrapText = true;
        summary.Cell("M1").Value = $"Revision {e.Revision}";
        summary.Cell("B23").Value = "Project Context";
        using var logo = new MemoryStream(InvoiceBrandingAssets.LoadPng());
        summary.AddPicture(logo, "US Signal").MoveTo(summary.Cell("B1")).WithSize(79, 70);
        // Do not add template-default project management, reserve or travel hours
        // to the Solution Architect's reviewed phase totals.
        summary.Cell("F4").FormulaA1 = "IF(COUNT('Totals Sheet'!C15:C19)=5,SUM('Totals Sheet'!C15:C19),\"\")";
        summary.Cell("F7").FormulaA1 = "IF(ISNUMBER(F4),F4,\"\")";
        summary.Cell("E7").Value = "Reviewed Phase Hours";
        summary.Cell("F9").Clear(XLClearOptions.Contents);
        summary.Cell("M4").Value = "Rates pending";
        summary.Cell("N6").Value = "Not set";
        summary.Cell("M14").Value = "Overtime requires review";
        summary.Cell("M14").Style.Font.FontColor = XLColor.Black;
        var phases = new[] { ("plan", "Plan"), ("design", "Design"), ("implement", "Implement"), ("validate", "Validate"), ("release", "Release") };
        var totals = book.Worksheet("Totals Sheet");
        var breakdown = book.Worksheet("Phase Breakdown");
        var notes = new List<(string, string)> { ("Estimate basis", "Reviewed task hours roll up to phase totals. Legacy phase-only estimates are identified separately. Blank task hours, engineering roles, overtime, reserve, travel and prices are unknown. Prices require approved commercial inputs.") };
        var risks = new List<(string, string)>();
        var assumptions = new List<(string, string)>();
        for (var index = 0; index < phases.Length; index++)
        {
            var (code, name) = phases[index];
            var sheet = book.Worksheet(name);
            var phase = model.Phases.SingleOrDefault(p => p.PhaseCode == code);
            sheet.Cell("A2").Value = name + " tasks";
            sheet.Cell("G2").Value = phase?.Tasks is { Count: > 0 } ? "Task hours saved and reviewed in Pulse." : "Phase-only estimate; task allocations require review.";
            sheet.Cell("A3").Value = "Task breakdown below; phase hours appear once.";
            sheet.Cell("A4").Value = name + " reviewed phase total";
            var lastTask = 4;
            if (phase != null)
            {
                sheet.Cell("B4").Value = phase.FinalHours;
                sheet.Cell("G4").Value = phase.LoeRationale;
                var activities = phase.DetailedActivities.Concat(phase.TechnicalTasks)
                    .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var additionalRows = Math.Max(0, activities.Length - 95);
                if (additionalRows > 0) sheet.Row(100).InsertRowsAbove(additionalRows);
                for (var i = 0; i < activities.Length; i++) sheet.Cell(i + 5, 1).Value = activities[i];
                lastTask = Math.Max(4, activities.Length + 4);
                var totalRow = 100 + additionalRows;
                sheet.Cell(totalRow, 1).Value = "Reviewed phase hours";
                sheet.Cell(totalRow, 2).FormulaA1 = $"SUM(B4:B{totalRow - 1})";
                if (phase.Tasks is { Count: > 0 } tasks)
                {
                    sheet.Range(4, 1, Math.Max(99, lastTask), 7).Clear(XLClearOptions.Contents);
                    var extraTaskRows = Math.Max(0, tasks.Count - 96);
                    if (extraTaskRows > additionalRows) sheet.Row(totalRow).InsertRowsAbove(extraTaskRows - additionalRows);
                    totalRow = 100 + Math.Max(extraTaskRows, additionalRows);
                    lastTask = 3 + tasks.Count;
                    for (var t = 0; t < tasks.Count; t++)
                    {
                        sheet.Cell(t + 4, 1).Value = tasks[t].Description;
                        if (tasks[t].Hours.HasValue) sheet.Cell(t + 4, 2).Value = tasks[t].Hours!.Value;
                        sheet.Cell(t + 4, 7).Value = tasks[t].Notes ?? "";
                    }
                    sheet.Cell(totalRow, 1).Value = "Reviewed phase hours";
                    sheet.Cell(totalRow, 2).FormulaA1 = $"IF(COUNT(B4:B{lastTask})={tasks.Count},SUM(B4:B{lastTask}),\"\")";
                    sheet.Cell("A3").Value = "Task hours roll up to the phase total.";
                    if (!Module025TaskEstimates.Reconciled(phase)) sheet.Cell("G2").Value = "DRAFT - Complete and reconcile all task hours before confirmation.";
                }
                totals.Cell(15 + index, 3).FormulaA1 = $"IF(ISNUMBER('{name}'!B{totalRow}),'{name}'!B{totalRow},\"\")";
                var baseRow = index switch { 0 or 1 => 8, 2 or 3 => 28, _ => 48 };
                var col = index is 1 or 3 ? 11 : 2;
                breakdown.Cell(baseRow, col).Value = "Role allocation pending";
                breakdown.Cell(baseRow, col + 1).FormulaA1 = $"IF(ISNUMBER('{name}'!B{totalRow}),'{name}'!B{totalRow},\"\")";
                breakdown.Cell(baseRow + 5, col + 1).FormulaA1 = $"IF(ISNUMBER('{name}'!B{totalRow}),'{name}'!B{totalRow},\"\")";
                notes.Add((name + " objective", phase.Objective));
                notes.Add((name + " estimate rationale", phase.LoeRationale));
                Add(notes, name + " technical detail", phase.TechnicalTasks);
                Add(notes, name + " deliverable", phase.Deliverables);
                Add(notes, name + " acceptance", phase.AcceptanceCriteria);
                Add(notes, name + " validation", phase.ValidationSteps);
                Add(notes, name + " prerequisite", phase.Prerequisites);
                Add(risks, name + " risk", phase.Risks);
                Add(risks, name + " open question", phase.OpenQuestions);
                Add(assumptions, name + " assumption", phase.Assumptions);
                Add(assumptions, name + " dependency", phase.Dependencies);
                Add(assumptions, name + " US Signal responsibility", phase.UsSignalResponsibilities);
                Add(assumptions, name + " customer responsibility", phase.CustomerResponsibilities);
            }
            else sheet.Cell("G4").Value = "Phase estimate not available.";
            sheet.Style.Font.FontSize = 11;
            sheet.Style.Font.FontColor = XLColor.Black;
            sheet.ConditionalFormats.RemoveAll();
            sheet.Column(1).Width = 60;
            sheet.Columns(2, 4).Width = 12;
            sheet.Column(5).Width = 22;
            sheet.Column(6).Width = 18;
            sheet.Column(7).Width = 50;
            sheet.Columns(2, 4).Style.NumberFormat.Format = "0.##";
            sheet.Range(1, 1, Math.Max(100, lastTask), 7).Style.Alignment.WrapText = true;
            for (var r = 2; r <= lastTask; r++)
            {
                var lines = Math.Max(Math.Ceiling(sheet.Cell(r, 1).GetString().Length / 65d), Math.Ceiling(sheet.Cell(r, 7).GetString().Length / 60d));
                sheet.Row(r).Height = Math.Min(409, Math.Max(30, 16 * (lines + 1)));
            }
            sheet.SheetView.FreezeRows(3);
            sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            sheet.PageSetup.FitToPages(1, 0);
            sheet.PageSetup.SetRowsToRepeatAtTop(1, 3);
            sheet.PageSetup.PrintAreas.Clear();
            sheet.PageSetup.PrintAreas.Add(1, 1, lastTask, 7);
        }
        totals.Cell("B11").Value = "Reviewed phase hours";
        totals.Cell("C11").FormulaA1 = "IF(COUNT(C15:C19)=5,SUM(C15:C19),\"\")";
        totals.Cell("C24").FormulaA1 = "IF(COUNT(C15:C16,C19)=3,SUM(C15:C16,C19),\"\")";
        totals.Cell("C25").FormulaA1 = "IF(COUNT(C17:C18)=2,SUM(C17:C18),\"\")";
        totals.Cell("B3").Value = "RESOURCE ALLOCATION REQUIRES REVIEW";
        totals.Cell("B5").Value = "BA/Dev/Arch";
        totals.Cell("B6").Value = "SME";
        totals.Cell("B7").Value = "Consulting Eng";
        totals.Cell("B8").Value = "Associate Eng";
        totals.Cell("B29").Value = "PRICING REQUIRES APPROVED RATES";
        AddSections(e.SowSections, assumptions, "assumptions", "Assumption");
        AddSections(e.SowSections, assumptions, "dependencies", "Dependency");
        AddSections(e.SowSections, assumptions, "outOfScope", "Exclusion");
        AddSections(e.SowSections, assumptions, "customerResponsibilities", "Customer responsibility");
        AddSections(e.SowSections, notes, "deliverables", "Deliverable");
        FillNotes(book.Worksheet("Architect Notes"), "Architect Notes", notes);
        FillNotes(book.Worksheet("Gotcha Items"), "Risks and Open Questions", risks);
        FillNotes(book.Worksheet("Assumptions Responsibilities"), "Assumptions and Responsibilities", assumptions);
        foreach (var sheet in book.Worksheets)
        {
            sheet.Style.Font.FontName = "Arial";
            sheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        }
        ConfigurePrint(summary, "B1:N38", XLPageOrientation.Landscape);
        summary.Column(2).Width = 30;
        summary.Column(3).Width = 42;
        summary.Range("B4:C21").Style.Alignment.WrapText = true;
        summary.Range("M4:N14").Style.Alignment.WrapText = true;
        summary.Rows(4, 21).Height = 24;
        summary.Range("C11:C16").Style.Alignment.WrapText = true;
        summary.Rows(11, 16).Height = 30;
        ConfigurePrint(breakdown, "B2:R64", XLPageOrientation.Landscape);
        breakdown.PageSetup.FitToPages(1, 1);
        breakdown.Range("B2:R64").Style.Alignment.WrapText = true;
        foreach (var address in new[] { "C7", "L7", "C27", "L27", "C47" }) breakdown.Cell(address).Value = "Reviewed";
        breakdown.Range("C8:N53").Style.NumberFormat.Format = "0.##";
        ConfigurePrint(totals, "B1:G34", XLPageOrientation.Landscape);
        totals.Column(2).Width = 44;
        totals.Columns(3, 7).Width = 22;
        totals.Range("B1:G34").Style.Alignment.WrapText = true;
        totals.Rows(4, 4).Height = 35;
        totals.Range("C5:D25").Style.NumberFormat.Format = "0.##";
        var skus = book.Worksheet("SELL SKUs");
        ConfigurePrint(skus, "A1:I8", XLPageOrientation.Landscape);
        skus.Range("D4:I4").Merge();
        skus.Range("D5:I6").Merge();
        skus.Cell("D5").Value = "No SKU, rate or price is inferred from the reference workbook.";
        skus.Range("D4:I6").Style.Font.FontColor = XLColor.Black;
        skus.Range("D4:I6").Style.Alignment.WrapText = true;
        foreach (var name in new[] { "Architect Notes", "Gotcha Items", "Assumptions Responsibilities" })
        {
            var sheet = book.Worksheet(name);
            ConfigurePrint(sheet, $"A1:B{Math.Max(4, sheet.LastRowUsed()!.RowNumber())}", XLPageOrientation.Landscape);
        }
        book.Properties.Title = $"{e.CustomerName} - {e.ProjectName} - GSD";
        using var output = new MemoryStream();
        book.RecalculateAllFormulas();
        book.SaveAs(output, new SaveOptions { EvaluateFormulasBeforeSaving = true });
        return output.ToArray();
    }

    private static void ConfigurePrint(IXLWorksheet sheet, string area, XLPageOrientation orientation)
    {
        sheet.PageSetup.PrintAreas.Clear();
        sheet.PageSetup.PrintAreas.Add(area);
        sheet.PageSetup.PageOrientation = orientation;
        sheet.PageSetup.FitToPages(1, 0);
    }

    private static void Add(List<(string, string)> rows, string label, IEnumerable<string> values) =>
        rows.AddRange(values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => (label, v)));

    private static void AddSections(System.Text.Json.JsonElement root, List<(string, string)> rows, string key, string label)
    {
        if (root.ValueKind == System.Text.Json.JsonValueKind.Object && root.TryGetProperty(key, out var values)
            && values.ValueKind == System.Text.Json.JsonValueKind.Array)
            Add(rows, label, values.EnumerateArray().Where(v => v.ValueKind == System.Text.Json.JsonValueKind.String).Select(v => v.GetString()!));
    }

    private static void FillNotes(IXLWorksheet sheet, string title, IEnumerable<(string Label, string Text)> values)
    {
        sheet.Cell("A1").Value = title;
        sheet.Cell("A1").Style.Font.Bold = true;
        var row = 3;
        foreach (var (label, text) in values.Where(v => !string.IsNullOrWhiteSpace(v.Text)).Distinct())
        {
            sheet.Cell(row, 1).Value = label;
            sheet.Cell(row, 2).Value = text;
            sheet.Row(row).Height = Math.Max(30, 15 * (Math.Ceiling(text.Length / 100d) + 1));
            row++;
        }
        sheet.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        sheet.Column(1).Width = 35;
        sheet.Column(2).Width = 100;
        sheet.Column(2).Style.Alignment.WrapText = true;
        sheet.PageSetup.FitToPages(1, 0);
    }
}
