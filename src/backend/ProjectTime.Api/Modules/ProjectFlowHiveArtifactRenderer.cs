using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace ProjectTime.Api.Modules;

internal static class ProjectFlowHiveArtifactRenderer
{
    private const string DraftLabel = "INTERNAL DRAFT — NOT A CUSTOMER BASELINE";

    public static byte[] BuildExcel(
        ProjectFlowHiveArtifactRequest request,
        ProjectFlowHiveScheduleResult schedule)
    {
        using var workbook = new XLWorkbook();
        using var logoStream = new MemoryStream(ProjectFlowHiveBrandAssets.LogoJpeg, writable: false);
        var summary = workbook.Worksheets.Add("Plan Summary");
        var picture = summary.AddPicture(logoStream);
        picture.Name = "US Signal logo";
        picture.MoveTo(summary.Cell("A1"));
        picture.Width = 100;
        picture.Height = 67;

        summary.Cell("C1").Value = "US Signal Project FlowHive";
        summary.Cell("C1").Style.Font.Bold = true;
        summary.Cell("C1").Style.Font.FontSize = 18;
        summary.Cell("C1").Style.Font.FontColor = XLColor.FromHtml("#0B2B4B");
        summary.Cell("C2").Value = DraftLabel;
        summary.Cell("C2").Style.Font.Bold = true;
        summary.Cell("C2").Style.Font.FontColor = XLColor.FromHtml("#B42318");
        summary.Cell("A5").Value = "Plan";
        summary.Cell("B5").Value = request.Plan?.PlanName ?? "Project FlowHive plan";
        summary.Cell("A6").Value = "Project";
        summary.Cell("B6").Value = Join(request.Plan?.ProjectCode, request.Plan?.ProjectName);
        summary.Cell("A7").Value = "Customer";
        summary.Cell("B7").Value = request.Plan?.CustomerName ?? "Not specified";
        summary.Cell("A8").Value = "Revision";
        summary.Cell("B8").Value = request.Plan?.RevisionLabel ?? "Unversioned draft";
        summary.Cell("A9").Value = "Schedule";
        summary.Cell("B9").Value = $"{FormatDate(schedule.ProjectStartDate)} through {FormatDate(schedule.ProjectFinishDate)}";
        summary.Cell("A10").Value = "Critical tasks";
        summary.Cell("B10").Value = schedule.CriticalTaskCount;
        summary.Cell("A11").Value = "Logo checksum";
        summary.Cell("B11").Value = ProjectFlowHiveBrandAssets.LogoSha256;
        summary.Range("A5:A11").Style.Font.Bold = true;
        summary.Columns("A:D").AdjustToContents();
        summary.SheetView.FreezeRows(4);

        var tasks = workbook.Worksheets.Add("Schedule");
        var taskHeaders = new[]
        {
            "WBS", "Parent WBS", "Task", "Start", "Finish", "Duration",
            "Percent Complete", "Remaining Effort", "Total Float", "Free Float",
            "Critical", "Status"
        };
        for (var column = 0; column < taskHeaders.Length; column++)
        {
            tasks.Cell(1, column + 1).Value = taskHeaders[column];
        }
        for (var index = 0; index < schedule.Tasks.Count; index++)
        {
            var task = schedule.Tasks[index];
            var row = index + 2;
            tasks.Cell(row, 1).Value = task.WbsNumber;
            tasks.Cell(row, 2).Value = task.ParentWbsNumber ?? string.Empty;
            tasks.Cell(row, 3).Value = task.Name;
            tasks.Cell(row, 4).Value = task.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            tasks.Cell(row, 5).Value = task.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            tasks.Cell(row, 6).Value = task.DurationWorkingDays;
            tasks.Cell(row, 7).Value = task.PercentComplete;
            tasks.Cell(row, 8).Value = task.RemainingEffortHours;
            tasks.Cell(row, 9).Value = task.TotalFloatWorkingDays;
            tasks.Cell(row, 10).Value = task.FreeFloatWorkingDays;
            tasks.Cell(row, 11).Value = task.IsCritical ? "Yes" : "No";
            tasks.Cell(row, 12).Value = task.Status;
        }
        StyleTable(tasks, taskHeaders.Length, schedule.Tasks.Count + 1);

        var dependencies = workbook.Worksheets.Add("Dependencies");
        var dependencyHeaders = new[] { "Predecessor", "Successor", "Type", "Lead / lag working days" };
        for (var column = 0; column < dependencyHeaders.Length; column++)
        {
            dependencies.Cell(1, column + 1).Value = dependencyHeaders[column];
        }
        var dependencyRows = request.Plan?.Dependencies ?? [];
        for (var index = 0; index < dependencyRows.Count; index++)
        {
            var item = dependencyRows[index];
            dependencies.Cell(index + 2, 1).Value = item.PredecessorWbs ?? string.Empty;
            dependencies.Cell(index + 2, 2).Value = item.SuccessorWbs ?? string.Empty;
            dependencies.Cell(index + 2, 3).Value = item.Type ?? "FS";
            dependencies.Cell(index + 2, 4).Value = item.LagWorkingDays;
        }
        StyleTable(dependencies, dependencyHeaders.Length, dependencyRows.Count + 1);

        var audit = workbook.Worksheets.Add("Artifact Control");
        audit.Cell("A1").Value = "US Signal Project FlowHive artifact control";
        audit.Cell("A1").Style.Font.Bold = true;
        audit.Cell("A3").Value = "Status";
        audit.Cell("B3").Value = DraftLabel;
        audit.Cell("A4").Value = "Generated at UTC";
        audit.Cell("B4").Value = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        audit.Cell("A5").Value = "Contract version";
        audit.Cell("B5").Value = schedule.ContractVersion;
        audit.Cell("A6").Value = "Calendar mode";
        audit.Cell("B6").Value = schedule.CalendarMode;
        audit.Cell("A7").Value = "Customer sharing";
        audit.Cell("B7").Value = "Disabled";
        audit.Cell("A8").Value = "External link";
        audit.Cell("B8").Value = "Not created";
        audit.Cell("A9").Value = "US Signal logo SHA-256";
        audit.Cell("B9").Value = ProjectFlowHiveBrandAssets.LogoSha256;
        audit.Columns("A:B").AdjustToContents();

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    public static byte[] BuildPdf(
        ProjectFlowHiveArtifactRequest request,
        ProjectFlowHiveScheduleResult schedule)
    {
        const int rowsPerPage = 28;
        var taskPages = schedule.Tasks
            .Chunk(rowsPerPage)
            .Select(chunk => chunk.ToArray())
            .ToList();
        if (taskPages.Count == 0) taskPages.Add([]);

        var pageContents = taskPages.Select((tasks, index) =>
            BuildPdfPage(request, schedule, tasks, index + 1, taskPages.Count)).ToArray();
        return BuildPdfDocument(pageContents, ProjectFlowHiveBrandAssets.LogoJpeg);
    }

    private static string BuildPdfPage(
        ProjectFlowHiveArtifactRequest request,
        ProjectFlowHiveScheduleResult schedule,
        IReadOnlyList<ProjectFlowHiveScheduledTask> tasks,
        int pageNumber,
        int pageCount)
    {
        var content = new StringBuilder();
        content.Append("q 100 0 0 67 36 690 cm /Im1 Do Q\n");
        PdfText(content, 155, 744, 18, "US Signal Project FlowHive", true, "0.04 0.17 0.29");
        PdfText(content, 155, 723, 10, DraftLabel, true, "0.71 0.14 0.09");
        PdfText(content, 36, 680, 13, request.ArtifactTitle ?? request.Plan?.PlanName ?? "Governed project plan", true, "0.04 0.17 0.29");
        PdfText(content, 36, 661, 9, $"Project: {Join(request.Plan?.ProjectCode, request.Plan?.ProjectName)}", false, "0.18 0.25 0.34");
        PdfText(content, 36, 646, 9, $"Customer: {request.Plan?.CustomerName ?? "Not specified"}", false, "0.18 0.25 0.34");
        PdfText(content, 330, 661, 9, $"Schedule: {FormatDate(schedule.ProjectStartDate)} - {FormatDate(schedule.ProjectFinishDate)}", false, "0.18 0.25 0.34");
        PdfText(content, 330, 646, 9, $"Critical tasks: {schedule.CriticalTaskCount}", false, "0.18 0.25 0.34");

        content.Append("0.04 0.17 0.29 rg 36 608 540 25 re f\n");
        var headings = new[] { ("WBS", 42), ("TASK", 95), ("START", 345), ("FINISH", 410), ("FLOAT", 475), ("STATUS", 520) };
        foreach (var (label, x) in headings) PdfText(content, x, 617, 7, label, true, "1 1 1");

        var y = 588;
        for (var index = 0; index < tasks.Count; index++)
        {
            var task = tasks[index];
            if (index % 2 == 0) content.Append($"0.94 0.98 1 rg 36 {y - 5} 540 20 re f\n");
            PdfText(content, 42, y, 7.3, task.WbsNumber, true, "0.06 0.16 0.27");
            PdfText(content, 95, y, 7.3, Truncate(task.Name, 48), false, "0.06 0.16 0.27");
            PdfText(content, 345, y, 7.3, FormatDate(task.StartDate), false, "0.06 0.16 0.27");
            PdfText(content, 410, y, 7.3, FormatDate(task.EndDate), false, "0.06 0.16 0.27");
            PdfText(content, 475, y, 7.3, task.TotalFloatWorkingDays.ToString(CultureInfo.InvariantCulture), false, "0.06 0.16 0.27");
            PdfText(content, 520, y, 7.3, task.IsCritical ? "Critical" : Truncate(task.Status, 10), task.IsCritical, task.IsCritical ? "0.71 0.14 0.09" : "0.06 0.16 0.27");
            y -= 20;
        }

        content.Append("0.68 0.77 0.84 RG 36 53 m 576 53 l S\n");
        PdfText(content, 36, 35, 7, $"Logo SHA-256 {ProjectFlowHiveBrandAssets.LogoSha256}", false, "0.34 0.42 0.50");
        PdfText(content, 495, 35, 7, $"Page {pageNumber} of {pageCount}", false, "0.34 0.42 0.50");
        return content.ToString();
    }

    private static byte[] BuildPdfDocument(IReadOnlyList<string> pageContents, byte[] logo)
    {
        var pageIds = pageContents.Select((_, index) => 7 + index * 2).ToArray();
        var contentIds = pageContents.Select((_, index) => 6 + index * 2).ToArray();
        var objects = new SortedDictionary<int, byte[]>();
        objects[1] = Ascii("<< /Type /Catalog /Pages 2 0 R >>");
        objects[2] = Ascii($"<< /Type /Pages /Kids [{string.Join(' ', pageIds.Select(id => $"{id} 0 R"))}] /Count {pageIds.Length} >>");
        objects[3] = Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        objects[4] = Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>");
        objects[5] = StreamObject(
            $"/Type /XObject /Subtype /Image /Width 222 /Height 148 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {logo.Length}",
            logo);

        for (var index = 0; index < pageContents.Count; index++)
        {
            var bytes = Ascii(pageContents[index]);
            objects[contentIds[index]] = StreamObject($"/Length {bytes.Length}", bytes);
            objects[pageIds[index]] = Ascii(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
                $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> /XObject << /Im1 5 0 R >> >> " +
                $"/Contents {contentIds[index]} 0 R >>");
        }

        using var output = new MemoryStream();
        WriteAscii(output, "%PDF-1.7\n%USSignal\n");
        var offsets = new Dictionary<int, long>();
        foreach (var pair in objects)
        {
            offsets[pair.Key] = output.Position;
            WriteAscii(output, $"{pair.Key} 0 obj\n");
            output.Write(pair.Value);
            WriteAscii(output, "\nendobj\n");
        }
        var xref = output.Position;
        var maxId = objects.Keys.Max();
        WriteAscii(output, $"xref\n0 {maxId + 1}\n0000000000 65535 f \n");
        for (var id = 1; id <= maxId; id++)
        {
            WriteAscii(output, $"{offsets[id]:D10} 00000 n \n");
        }
        WriteAscii(output, $"trailer\n<< /Size {maxId + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        return output.ToArray();
    }

    private static byte[] StreamObject(string dictionary, byte[] content)
    {
        using var output = new MemoryStream();
        WriteAscii(output, $"<< {dictionary} >>\nstream\n");
        output.Write(content);
        WriteAscii(output, "\nendstream");
        return output.ToArray();
    }

    private static void StyleTable(IXLWorksheet worksheet, int columnCount, int rowCount)
    {
        var header = worksheet.Range(1, 1, 1, columnCount);
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#0B2B4B");
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Font.Bold = true;
        if (rowCount > 1)
        {
            worksheet.Range(1, 1, rowCount, columnCount).CreateTable();
        }
        worksheet.SheetView.FreezeRows(1);
        worksheet.Columns(1, columnCount).AdjustToContents(4d, 48d);
    }

    private static void PdfText(StringBuilder builder, double x, double y, double size, string value, bool bold, string color)
    {
        builder.Append(color).Append(" rg BT /").Append(bold ? "F2" : "F1").Append(' ')
            .Append(size.ToString("0.##", CultureInfo.InvariantCulture)).Append(" Tf ")
            .Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
            .Append(y.ToString("0.##", CultureInfo.InvariantCulture)).Append(" Td (")
            .Append(EscapePdf(value)).Append(") Tj ET\n");
    }

    private static string EscapePdf(string? value) => (value ?? string.Empty)
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("(", "\\(", StringComparison.Ordinal)
        .Replace(")", "\\)", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);

    private static string Truncate(string? value, int length)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= length ? clean : $"{clean[..Math.Max(1, length - 1)]}…";
    }

    private static string Join(string? code, string? name) =>
        string.Join(" — ", new[] { code, name }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string FormatDate(DateOnly? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Not scheduled";

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
    private static void WriteAscii(Stream stream, string value) => stream.Write(Ascii(value));
}
