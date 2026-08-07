using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace ProjectTime.Api.Modules;

internal static class ProjectFlowHiveArtifactRenderer
{
    private const string DraftLabel = "INTERNAL DRAFT — NOT A CUSTOMER BASELINE";
    private sealed record ArtifactTaskRow(
        string Wbs,
        string TaskName,
        DateOnly StartDate,
        DateOnly EndDate,
        int DurationInDays,
        decimal Progress,
        string Predecessor,
        string DependencyType,
        string Comments,
        string Notes,
        string AssignedIdentity);

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

        var taskRows = BuildArtifactTaskRows(request, schedule);
        var tasks = workbook.Worksheets.Add("Schedule");
        var taskHeaders = new[]
        {
            "WBS", "Task Name", "Start Date", "End Date", "Duration in Days",
            "Progress", "Predecessor", "Type", "Comments", "Notes", "Assigned Identity"
        };
        for (var column = 0; column < taskHeaders.Length; column++)
        {
            tasks.Cell(1, column + 1).Value = taskHeaders[column];
        }
        for (var index = 0; index < taskRows.Count; index++)
        {
            var task = taskRows[index];
            var row = index + 2;
            tasks.Cell(row, 1).Value = task.Wbs;
            tasks.Cell(row, 2).Value = task.TaskName;
            tasks.Cell(row, 3).Value = task.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            tasks.Cell(row, 4).Value = task.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            tasks.Cell(row, 5).Value = task.DurationInDays;
            tasks.Cell(row, 6).Value = $"{Math.Round(task.Progress, 0, MidpointRounding.AwayFromZero)}%";
            tasks.Cell(row, 7).Value = task.Predecessor;
            tasks.Cell(row, 8).Value = task.DependencyType;
            tasks.Cell(row, 9).Value = task.Comments;
            tasks.Cell(row, 10).Value = task.Notes;
            tasks.Cell(row, 11).Value = task.AssignedIdentity;
        }
        StyleTable(tasks, taskHeaders.Length, taskRows.Count + 1);
        tasks.Columns(9, 10).Style.Alignment.WrapText = true;
        tasks.Column(2).Width = Math.Max(tasks.Column(2).Width, 28d);
        tasks.Columns(9, 10).Width = 36d;
        tasks.Column(11).Width = Math.Max(tasks.Column(11).Width, 24d);

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
        const int rowsPerPage = 18;
        var taskPages = BuildArtifactTaskRows(request, schedule)
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
        IReadOnlyList<ArtifactTaskRow> tasks,
        int pageNumber,
        int pageCount)
    {
        var content = new StringBuilder();
        content.Append("q 86 0 0 57 36 520 cm /Im1 Do Q\n");
        PdfText(content, 135, 568, 18, "US Signal Project FlowHive", true, "0.04 0.17 0.29");
        PdfText(content, 135, 548, 10, DraftLabel, true, "0.71 0.14 0.09");
        PdfText(content, 36, 512, 13, request.ArtifactTitle ?? request.Plan?.PlanName ?? "Governed project plan", true, "0.04 0.17 0.29");
        PdfText(content, 36, 494, 8, $"Project: {Join(request.Plan?.ProjectCode, request.Plan?.ProjectName)}", false, "0.18 0.25 0.34");
        PdfText(content, 36, 480, 8, $"Customer: {request.Plan?.CustomerName ?? "Not specified"}", false, "0.18 0.25 0.34");
        PdfText(content, 560, 494, 8, $"Schedule: {FormatDate(schedule.ProjectStartDate)} - {FormatDate(schedule.ProjectFinishDate)}", false, "0.18 0.25 0.34");
        PdfText(content, 560, 480, 8, $"Tasks: {tasks.Count} on this page | Critical tasks: {schedule.CriticalTaskCount}", false, "0.18 0.25 0.34");

        content.Append("0.04 0.17 0.29 rg 36 444 936 24 re f\n");
        var headings = new[]
        {
            ("WBS", 42), ("TASK NAME", 78), ("START DATE", 220), ("END DATE", 278),
            ("DURATION IN DAYS", 336), ("PROGRESS", 410), ("PREDECESSOR", 466), ("TYPE", 536),
            ("COMMENTS", 570), ("NOTES", 690), ("ASSIGNED IDENTITY", 815)
        };
        foreach (var (label, x) in headings) PdfText(content, x, 453, 5.8, label, true, "1 1 1");

        var y = 425;
        for (var index = 0; index < tasks.Count; index++)
        {
            var task = tasks[index];
            if (index % 2 == 0) content.Append($"0.94 0.98 1 rg 36 {y - 5} 936 20 re f\n");
            PdfText(content, 42, y, 6.2, Truncate(task.Wbs, 7), true, "0.06 0.16 0.27");
            PdfText(content, 78, y, 6.2, Truncate(task.TaskName, 29), false, "0.06 0.16 0.27");
            PdfText(content, 220, y, 6.2, FormatDate(task.StartDate), false, "0.06 0.16 0.27");
            PdfText(content, 278, y, 6.2, FormatDate(task.EndDate), false, "0.06 0.16 0.27");
            PdfText(content, 336, y, 6.2, task.DurationInDays.ToString(CultureInfo.InvariantCulture), false, "0.06 0.16 0.27");
            PdfText(content, 410, y, 6.2, $"{Math.Round(task.Progress, 0, MidpointRounding.AwayFromZero)}%", false, "0.06 0.16 0.27");
            PdfText(content, 466, y, 6.2, Truncate(task.Predecessor, 12), false, "0.06 0.16 0.27");
            PdfText(content, 536, y, 6.2, Truncate(task.DependencyType, 4), false, "0.06 0.16 0.27");
            PdfText(content, 570, y, 6.2, Truncate(task.Comments, 22), false, "0.06 0.16 0.27");
            PdfText(content, 690, y, 6.2, Truncate(task.Notes, 22), false, "0.06 0.16 0.27");
            PdfText(content, 815, y, 6.2, Truncate(task.AssignedIdentity, 26), false, "0.06 0.16 0.27");
            y -= 20;
        }

        content.Append("0.68 0.77 0.84 RG 36 53 m 972 53 l S\n");
        PdfText(content, 36, 35, 7, $"Logo SHA-256 {ProjectFlowHiveBrandAssets.LogoSha256}", false, "0.34 0.42 0.50");
        PdfText(content, 915, 35, 7, $"Page {pageNumber} of {pageCount}", false, "0.34 0.42 0.50");
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
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1008 612] " +
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

    private static IReadOnlyList<ArtifactTaskRow> BuildArtifactTaskRows(
        ProjectFlowHiveArtifactRequest request,
        ProjectFlowHiveScheduleResult schedule)
    {
        var planTasks = (request.Plan?.Tasks ?? [])
            .Where(task => !string.IsNullOrWhiteSpace(task.WbsNumber))
            .GroupBy(task => task.WbsNumber!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var dependencies = (request.Plan?.Dependencies ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.SuccessorWbs))
            .GroupBy(item => item.SuccessorWbs!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var assignments = (request.Plan?.Assignments ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.TaskWbs))
            .GroupBy(item => item.TaskWbs!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return schedule.Tasks.Select(scheduled =>
        {
            planTasks.TryGetValue(scheduled.WbsNumber, out var planTask);
            dependencies.TryGetValue(scheduled.WbsNumber, out var dependency);
            assignments.TryGetValue(scheduled.WbsNumber, out var assignment);
            var assignedIdentity = scheduled.IsSummary
                ? "Phase summary"
                : !string.IsNullOrWhiteSpace(assignment?.ResourceDisplayName)
                    ? assignment.ResourceDisplayName!.Trim()
                    : assignment?.ResourceUserId is not null
                        ? "Assigned identity"
                        : "Unassigned";
            return new ArtifactTaskRow(
                scheduled.WbsNumber,
                scheduled.Name,
                scheduled.StartDate,
                scheduled.EndDate,
                scheduled.DurationWorkingDays,
                scheduled.PercentComplete,
                dependency?.PredecessorWbs?.Trim() ?? string.Empty,
                string.IsNullOrWhiteSpace(dependency?.PredecessorWbs)
                    ? string.Empty
                    : string.IsNullOrWhiteSpace(dependency?.Type) ? "FS" : dependency.Type!.Trim().ToUpperInvariant(),
                planTask?.Comments?.Trim() ?? string.Empty,
                request.ExcludeNotes ? string.Empty : planTask?.Notes?.Trim() ?? string.Empty,
                assignedIdentity);
        }).ToArray();
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
