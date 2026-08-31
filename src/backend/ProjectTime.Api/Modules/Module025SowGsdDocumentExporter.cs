using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;

namespace ProjectTime.Api.Modules;

internal static class Module025SowGsdDocumentExporter
{
    internal const string StandardGsdTemplateKey = "standard_gsd";
    internal const string HaeaGsdTemplateKey = "haea_staff_aug_gsd_kus_uvo_telematics_1";
    internal const string HaeaGsdDisplayName = "HAEA Staff Aug GSD KUS UVO Telematics 1";

    internal static byte[] CreateSowDocx(Module025DocumentModel model)
    {
        var engagement = model.Engagement;
        var body = new StringBuilder();
        BodyParagraph(body, "STATEMENT OF WORK", "Title");
        BodyParagraph(body, engagement.EngagementNumber, "Subtitle");
        BodyParagraph(body, engagement.CustomerName.Length > 0 ? engagement.CustomerName : "Customer to be confirmed", "Subtitle");

        AppendMetadataTable(body, new[]
        {
            ("SOW/GSD ID", engagement.EngagementNumber),
            ("Commercial Model", CommercialLabel(engagement.CommercialModel)),
            ("Solution Architect", engagement.OwnerDisplayName),
            ("Account Executive", EmptyAsTbd(engagement.AccountExecutiveName)),
            ("Resale", EmptyAsTbd(engagement.ResaleName)),
            ("GSD Profile", GsdProfileLabel(engagement.GsdTemplateKey)),
            ("Revision", engagement.Revision.ToString(CultureInfo.InvariantCulture))
        });

        BodyHeading(body, "Services Overview", 1);
        BodyParagraph(body, EmptyAsTbd(engagement.ServiceOverview));

        BodyHeading(body, "Services Description", 1);
        BodyParagraph(body,
            "The services below are organized into the Plan, Design, Implement, Validate, and Release delivery lifecycle. " +
            "Each phase describes the work expected to be performed, the associated deliverables and responsibilities, " +
            "and the review evidence required before the Solution Architect confirms the final scope.");

        foreach (var phase in model.Phases.OrderBy(item => item.SortOrder))
        {
            BodyHeading(body, PhaseLabel(phase.PhaseCode), 1);
            BodyParagraph(body, EmptyAsTbd(phase.Objective));
            BodyParagraph(body, $"Reviewed level of effort: {phase.FinalHours:0.##} hour(s). AI suggestion: {phase.SuggestedHours:0.##} hour(s).", "Strong");
            if (!string.IsNullOrWhiteSpace(phase.LoeRationale))
                BodyParagraph(body, $"Level-of-effort rationale: {phase.LoeRationale}");

            AppendDetailedSection(body, "Detailed Activities", phase.DetailedActivities);
            AppendDetailedSection(body, "Technical Tasks / Configuration", phase.TechnicalTasks);
            AppendDetailedSection(body, "Deliverables", phase.Deliverables);
            AppendDetailedSection(body, "US Signal Responsibilities", phase.UsSignalResponsibilities);
            AppendDetailedSection(body, "Customer Responsibilities", phase.CustomerResponsibilities);
            AppendDetailedSection(body, "Prerequisites", phase.Prerequisites);
            AppendDetailedSection(body, "Dependencies", phase.Dependencies);
            AppendDetailedSection(body, "Assumptions", phase.Assumptions);
            AppendDetailedSection(body, "Open Questions", phase.OpenQuestions);
            AppendDetailedSection(body, "Acceptance Criteria", phase.AcceptanceCriteria);
            AppendDetailedSection(body, "Validation Steps", phase.ValidationSteps);
            AppendDetailedSection(body, "Risks / Considerations", phase.Risks);
        }

        BodyHeading(body, "Deliverables", 1);
        AppendBullets(body, SectionArray(engagement.SowSections, "deliverables", model.Phases.SelectMany(phase => phase.Deliverables)));

        BodyHeading(body, "Exclusions", 1);
        AppendBullets(body, SectionArray(engagement.SowSections, "outOfScope", Array.Empty<string>()),
            "Anything not expressly included in the reviewed scope is excluded unless added through approved change control.");

        BodyHeading(body, "Client Involvement", 1);
        AppendBullets(body, SectionArray(engagement.SowSections, "customerResponsibilities", model.Phases.SelectMany(phase => phase.CustomerResponsibilities)),
            "Customer responsibilities and prerequisites must be reviewed before execution begins.");

        BodyHeading(body, "Assumptions and Dependencies", 1);
        AppendBullets(body, SectionArray(engagement.SowSections, "assumptions", model.Phases.SelectMany(phase => phase.Assumptions)));
        AppendBullets(body, SectionArray(engagement.SowSections, "dependencies", model.Phases.SelectMany(phase => phase.Dependencies)));

        BodyHeading(body, "Review and Commercial Basis", 1);
        BodyParagraph(body,
            engagement.CommercialModel == "fixed"
                ? "This scope is configured as Fixed Price. Any work outside the confirmed scope, assumptions, dependencies, or acceptance basis requires review through the applicable change-control process."
                : "This scope is configured as Time & Materials. Actual billable effort is governed by the executed commercial agreement and approved work performed against this scope.");
        BodyParagraph(body,
            "This generated document remains a review artifact until the Solution Architect confirms the engagement. " +
            "Commercial, legal, security, technical, and customer approvals remain authoritative where applicable.");

        var documentXml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                {body}
                <w:sectPr>
                  <w:pgSz w:w="12240" w:h="15840"/>
                  <w:pgMar w:top="1080" w:right="1080" w:bottom="1080" w:left="1080"/>
                </w:sectPr>
              </w:body>
            </w:document>
            """;

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
                </Types>
                """);
            WriteZipEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            WriteZipEntry(archive, "word/_rels/document.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """);
            WriteZipEntry(archive, "word/styles.xml", StylesXml());
            WriteZipEntry(archive, "word/document.xml", documentXml);
        }
        return output.ToArray();
    }

    internal static byte[] CreateGsdXlsx(Module025DocumentModel model)
    {
        using var workbook = new XLWorkbook();
        var engagement = model.Engagement;
        var special = engagement.GsdTemplateKey == HaeaGsdTemplateKey;

        var summary = workbook.AddWorksheet(special ? "HAEA GSD" : "GSD Summary");
        summary.Cell("A1").Value = special ? HaeaGsdDisplayName : "General Solution Design / Level of Effort";
        summary.Cell("A2").Value = "Generated from Module 025 SOW & GSD Workspace";
        summary.Range("A1:E1").Merge();
        summary.Range("A2:E2").Merge();
        summary.Cell("A4").Value = "SOW/GSD ID";
        summary.Cell("B4").Value = engagement.EngagementNumber;
        summary.Cell("A5").Value = "Customer";
        summary.Cell("B5").Value = engagement.CustomerName;
        summary.Cell("A6").Value = "Commercial Model";
        summary.Cell("B6").Value = CommercialLabel(engagement.CommercialModel);
        summary.Cell("A7").Value = "Solution Architect";
        summary.Cell("B7").Value = engagement.OwnerDisplayName;
        summary.Cell("A8").Value = "Account Executive";
        summary.Cell("B8").Value = engagement.AccountExecutiveName;
        summary.Cell("A9").Value = "Resale";
        summary.Cell("B9").Value = engagement.ResaleName;
        summary.Cell("A10").Value = "GSD Template Profile";
        summary.Cell("B10").Value = GsdProfileLabel(engagement.GsdTemplateKey);
        summary.Cell("A11").Value = "Customer Program";
        summary.Cell("B11").Value = engagement.CustomerProgram.Equals("standard", StringComparison.OrdinalIgnoreCase)
            ? "Standard"
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(engagement.CustomerProgram);

        summary.Cell("A13").Value = "Phase";
        summary.Cell("B13").Value = "AI Suggested Hours";
        summary.Cell("C13").Value = "SA Final Hours";
        summary.Cell("D13").Value = "Variance";
        summary.Cell("E13").Value = "Level-of-Effort Rationale";
        var row = 14;
        foreach (var phase in model.Phases.OrderBy(item => item.SortOrder))
        {
            summary.Cell(row, 1).Value = PhaseLabel(phase.PhaseCode);
            summary.Cell(row, 2).Value = phase.SuggestedHours;
            summary.Cell(row, 3).Value = phase.FinalHours;
            summary.Cell(row, 4).FormulaA1 = $"=C{row}-B{row}";
            summary.Cell(row, 5).Value = phase.LoeRationale;
            row += 1;
        }
        summary.Cell(row, 1).Value = "Total";
        summary.Cell(row, 2).FormulaA1 = $"=SUM(B14:B{row - 1})";
        summary.Cell(row, 3).FormulaA1 = $"=SUM(C14:C{row - 1})";
        summary.Cell(row, 4).FormulaA1 = $"=C{row}-B{row}";
        summary.Range(13, 1, row, 5).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        summary.Range(13, 1, row, 5).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        summary.Row(13).Style.Font.Bold = true;
        summary.Row(row).Style.Font.Bold = true;
        summary.Column(1).Width = 22;
        summary.Column(2).Width = 20;
        summary.Column(3).Width = 18;
        summary.Column(4).Width = 14;
        summary.Column(5).Width = 70;
        summary.Column(5).Style.Alignment.WrapText = true;
        summary.Range("A1:E2").Style.Font.Bold = true;
        summary.Cell("A1").Style.Font.FontSize = 16;

        var details = workbook.AddWorksheet("P-D-I-V-R Detail");
        details.Cell("A1").Value = "Detailed Plan / Design / Implement / Validate / Release Scope";
        details.Range("A1:D1").Merge();
        details.Cell("A3").Value = "Phase";
        details.Cell("B3").Value = "Category";
        details.Cell("C3").Value = "Detailed Requirement / Activity";
        details.Cell("D3").Value = "Reviewed Hours";
        details.Row(3).Style.Font.Bold = true;
        var detailRow = 4;
        foreach (var phase in model.Phases.OrderBy(item => item.SortOrder))
        {
            var firstRowForPhase = detailRow;
            AppendDetailRows(details, ref detailRow, phase, "Objective", NonEmpty(phase.Objective));
            AppendDetailRows(details, ref detailRow, phase, "Detailed Activities", phase.DetailedActivities);
            AppendDetailRows(details, ref detailRow, phase, "Technical Tasks / Configuration", phase.TechnicalTasks);
            AppendDetailRows(details, ref detailRow, phase, "Deliverables", phase.Deliverables);
            AppendDetailRows(details, ref detailRow, phase, "US Signal Responsibilities", phase.UsSignalResponsibilities);
            AppendDetailRows(details, ref detailRow, phase, "Customer Responsibilities", phase.CustomerResponsibilities);
            AppendDetailRows(details, ref detailRow, phase, "Prerequisites", phase.Prerequisites);
            AppendDetailRows(details, ref detailRow, phase, "Dependencies", phase.Dependencies);
            AppendDetailRows(details, ref detailRow, phase, "Assumptions", phase.Assumptions);
            AppendDetailRows(details, ref detailRow, phase, "Open Questions", phase.OpenQuestions);
            AppendDetailRows(details, ref detailRow, phase, "Acceptance Criteria", phase.AcceptanceCriteria);
            AppendDetailRows(details, ref detailRow, phase, "Validation Steps", phase.ValidationSteps);
            AppendDetailRows(details, ref detailRow, phase, "Risks / Considerations", phase.Risks);
            if (detailRow == firstRowForPhase)
            {
                details.Cell(detailRow, 1).Value = PhaseLabel(phase.PhaseCode);
                details.Cell(detailRow, 2).Value = "Scope requires review";
                details.Cell(detailRow, 3).Value = "No detailed AI-supported activity was available for this phase. The Solution Architect must define the scope before confirmation.";
                details.Cell(detailRow, 4).Value = phase.FinalHours;
                detailRow += 1;
            }
        }
        details.Range(3, 1, Math.Max(3, detailRow - 1), 4).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        details.Range(3, 1, Math.Max(3, detailRow - 1), 4).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        details.Column(1).Width = 18;
        details.Column(2).Width = 30;
        details.Column(3).Width = 95;
        details.Column(4).Width = 16;
        details.Column(3).Style.Alignment.WrapText = true;
        details.SheetView.FreezeRows(3);

        var scope = workbook.AddWorksheet("Scope & Assumptions");
        scope.Cell("A1").Value = "Service Overview";
        scope.Cell("B1").Value = engagement.ServiceOverview;
        scope.Cell("A3").Value = "Deliverables";
        scope.Cell("B3").Value = string.Join("\n", SectionArray(engagement.SowSections, "deliverables", model.Phases.SelectMany(phase => phase.Deliverables)));
        scope.Cell("A4").Value = "Exclusions";
        scope.Cell("B4").Value = string.Join("\n", SectionArray(engagement.SowSections, "outOfScope", Array.Empty<string>()));
        scope.Cell("A5").Value = "Customer Responsibilities";
        scope.Cell("B5").Value = string.Join("\n", SectionArray(engagement.SowSections, "customerResponsibilities", model.Phases.SelectMany(phase => phase.CustomerResponsibilities)));
        scope.Cell("A6").Value = "Assumptions";
        scope.Cell("B6").Value = string.Join("\n", SectionArray(engagement.SowSections, "assumptions", model.Phases.SelectMany(phase => phase.Assumptions)));
        scope.Cell("A7").Value = "Dependencies";
        scope.Cell("B7").Value = string.Join("\n", SectionArray(engagement.SowSections, "dependencies", model.Phases.SelectMany(phase => phase.Dependencies)));
        scope.Column(1).Width = 28;
        scope.Column(2).Width = 110;
        scope.Column(2).Style.Alignment.WrapText = true;

        foreach (var worksheet in workbook.Worksheets)
        {
            worksheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            worksheet.Rows().AdjustToContents(1, 70);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void AppendDetailRows(IXLWorksheet sheet, ref int row, Module025PhaseRow phase, string category, IEnumerable<string> values)
    {
        foreach (var value in DistinctNonEmpty(values))
        {
            sheet.Cell(row, 1).Value = PhaseLabel(phase.PhaseCode);
            sheet.Cell(row, 2).Value = category;
            sheet.Cell(row, 3).Value = value;
            sheet.Cell(row, 4).Value = phase.FinalHours;
            row += 1;
        }
    }

    private static void AppendDetailedSection(StringBuilder body, string heading, IEnumerable<string> values)
    {
        var rows = DistinctNonEmpty(values).ToArray();
        if (rows.Length == 0) return;
        BodyHeading(body, heading, 2);
        AppendBullets(body, rows);
    }

    private static void AppendBullets(StringBuilder body, IEnumerable<string> values, string? fallback = null)
    {
        var rows = DistinctNonEmpty(values).ToArray();
        if (rows.Length == 0 && !string.IsNullOrWhiteSpace(fallback)) rows = new[] { fallback };
        foreach (var value in rows) BodyParagraph(body, value, "Bullet");
    }

    private static void AppendMetadataTable(StringBuilder body, IEnumerable<(string Label, string Value)> rows)
    {
        body.Append("<w:tbl><w:tblPr><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\"/><w:left w:val=\"single\" w:sz=\"4\"/><w:bottom w:val=\"single\" w:sz=\"4\"/><w:right w:val=\"single\" w:sz=\"4\"/><w:insideH w:val=\"single\" w:sz=\"4\"/><w:insideV w:val=\"single\" w:sz=\"4\"/></w:tblBorders></w:tblPr>");
        foreach (var row in rows)
        {
            body.Append("<w:tr>");
            body.Append($"<w:tc><w:p><w:r><w:rPr><w:b/></w:rPr><w:t>{Xml(row.Label)}</w:t></w:r></w:p></w:tc>");
            body.Append($"<w:tc><w:p><w:r><w:t xml:space=\"preserve\">{Xml(row.Value)}</w:t></w:r></w:p></w:tc>");
            body.Append("</w:tr>");
        }
        body.Append("</w:tbl>");
    }

    private static void BodyHeading(StringBuilder body, string text, int level) =>
        BodyParagraph(body, text, level <= 1 ? "Heading1" : "Heading2");

    private static void BodyParagraph(StringBuilder body, string text, string style = "Normal")
    {
        var bold = style == "Strong";
        var paragraphStyle = bold ? "Normal" : style;
        body.Append("<w:p><w:pPr>");
        if (paragraphStyle.Length > 0) body.Append($"<w:pStyle w:val=\"{paragraphStyle}\"/>");
        body.Append("</w:pPr><w:r>");
        if (bold) body.Append("<w:rPr><w:b/></w:rPr>");
        body.Append($"<w:t xml:space=\"preserve\">{Xml(text)}</w:t></w:r></w:p>");
    }

    private static string StylesXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:rPr><w:sz w:val="22"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:rPr><w:b/><w:sz w:val="38"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Subtitle"><w:name w:val="Subtitle"/><w:basedOn w:val="Normal"/><w:rPr><w:sz w:val="26"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:rPr><w:b/><w:sz w:val="30"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:rPr><w:b/><w:sz w:val="24"/></w:rPr></w:style>
          <w:style w:type="paragraph" w:styleId="Bullet"><w:name w:val="Bullet"/><w:basedOn w:val="Normal"/><w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr></w:style>
        </w:styles>
        """;

    private static IReadOnlyList<string> SectionArray(JsonElement root, string propertyName, IEnumerable<string> fallback)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array)
        {
            var values = property.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString() ?? string.Empty);
            var rows = DistinctNonEmpty(values).ToArray();
            if (rows.Length > 0) return rows;
        }
        return DistinctNonEmpty(fallback).ToArray();
    }

    private static IEnumerable<string> DistinctNonEmpty(IEnumerable<string>? values) =>
        (values ?? Array.Empty<string>())
            .Select(value => value?.Trim() ?? string.Empty)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> NonEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? Array.Empty<string>() : new[] { value.Trim() };

    private static string PhaseLabel(string phaseCode) => phaseCode switch
    {
        "plan" => "Plan",
        "design" => "Design",
        "implement" => "Implement",
        "validate" => "Validate",
        "release" => "Release",
        _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(phaseCode)
    };

    private static string CommercialLabel(string model) =>
        model == "fixed" ? "Fixed Price" : "Time & Materials";

    private static string GsdProfileLabel(string key) =>
        key == HaeaGsdTemplateKey ? HaeaGsdDisplayName : "Standard GSD";

    private static string EmptyAsTbd(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "To be confirmed" : value.Trim();

    private static string Xml(string? value) =>
        SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;

    private static void WriteZipEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content.TrimStart());
    }
}
