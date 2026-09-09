using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using SkiaSharp;

namespace ProjectTime.Api.Modules;

internal sealed record ProjectFlowHivePsaArtifactTable(
    string ArtifactKind,
    string Title,
    string ProjectCode,
    string ProjectName,
    string CustomerName,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    IReadOnlyList<string> Notes);

internal static class ProjectFlowHivePsaArtifactRenderer
{
    private const string ControlLabel = "US SIGNAL PROJECT DELIVERY ARTIFACT";
    private const string PdfFontResourceName = "ProjectTime.Api.Assets.Fonts.NotoSansCJKsc-Regular.otf";
    private const float PdfWidth = 1008f;
    private const float PdfHeight = 612f;
    private const float PdfLeft = 36f;
    private const float PdfRight = 972f;
    private const float PdfBodySize = 6.4f;
    private static readonly Lazy<byte[]> PdfFontBytes = new(() =>
    {
        using var stream = typeof(ProjectFlowHivePsaArtifactRenderer).Assembly
            .GetManifestResourceStream(PdfFontResourceName)
            ?? throw new InvalidOperationException($"Missing embedded PDF font resource: {PdfFontResourceName}");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    });

    internal static byte[] BuildExcel(ProjectFlowHivePsaArtifactTable artifact)
    {
        using var workbook = new XLWorkbook();
        var summary = workbook.Worksheets.Add("Artifact Summary");
        using var logoStream = new MemoryStream(ProjectFlowHiveBrandAssets.LogoJpeg, writable: false);
        var picture = summary.AddPicture(logoStream);
        picture.Name = "US Signal logo";
        picture.MoveTo(summary.Cell("A1"));
        picture.Width = 100;
        picture.Height = 67;

        summary.Cell("C1").Value = "US Signal Project FlowHive";
        summary.Cell("C1").Style.Font.Bold = true;
        summary.Cell("C1").Style.Font.FontSize = 18;
        summary.Cell("C1").Style.Font.FontColor = XLColor.FromHtml("#0B2B4B");
        summary.Cell("C2").Value = ControlLabel;
        summary.Cell("C2").Style.Font.Bold = true;
        summary.Cell("C2").Style.Font.FontColor = XLColor.FromHtml("#0B6E99");
        var summaryRows = new[]
        {
            ("Artifact", artifact.Title),
            ("Type", artifact.ArtifactKind),
            ("Project", Join(artifact.ProjectCode, artifact.ProjectName)),
            ("Customer", artifact.CustomerName),
            ("Generated UTC", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
            ("Rows", artifact.Rows.Count.ToString(CultureInfo.InvariantCulture)),
            ("Brand checksum", ProjectFlowHiveBrandAssets.LogoSha256)
        };
        for (var index = 0; index < summaryRows.Length; index++)
        {
            SetSpreadsheetText(summary.Cell(index + 5, 1), summaryRows[index].Item1);
            SetSpreadsheetText(summary.Cell(index + 5, 2), summaryRows[index].Item2);
            summary.Cell(index + 5, 1).Style.Font.Bold = true;
        }
        var noteRow = summaryRows.Length + 6;
        SetSpreadsheetText(summary.Cell(noteRow, 1), "Notes");
        summary.Cell(noteRow, 1).Style.Font.Bold = true;
        SetSpreadsheetText(summary.Cell(noteRow, 2), string.Join("\n", artifact.Notes));
        summary.Cell(noteRow, 2).Style.Alignment.WrapText = true;
        summary.Column(1).Width = 22;
        summary.Column(2).Width = 85;
        summary.Row(noteRow).Height = Math.Max(36, artifact.Notes.Count * 18);

        var sheet = workbook.Worksheets.Add(SafeSheetName(artifact.ArtifactKind));
        for (var column = 0; column < artifact.Columns.Count; column++)
        {
            SetSpreadsheetText(sheet.Cell(1, column + 1), artifact.Columns[column]);
        }
        for (var row = 0; row < artifact.Rows.Count; row++)
        {
            for (var column = 0; column < artifact.Columns.Count; column++)
            {
                SetSpreadsheetValue(sheet.Cell(row + 2, column + 1), column < artifact.Rows[row].Count
                    ? artifact.Rows[row][column]
                    : null);
            }
        }
        if (artifact.Columns.Count > 0)
        {
            var header = sheet.Range(1, 1, 1, artifact.Columns.Count);
            header.Style.Font.Bold = true;
            header.Style.Font.FontColor = XLColor.White;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#0B2B4B");
            sheet.SheetView.FreezeRows(1);
            sheet.Range(1, 1, Math.Max(2, artifact.Rows.Count + 1), artifact.Columns.Count).SetAutoFilter();
            sheet.Columns().AdjustToContents();
            foreach (var column in sheet.ColumnsUsed())
            {
                if (column.Width < 8) column.Width = 8;
                if (column.Width > 48) column.Width = 48;
            }
            sheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            sheet.Style.Alignment.WrapText = true;
        }

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    internal static byte[] BuildPdf(ProjectFlowHivePsaArtifactTable artifact)
    {
        using var fontData = SKData.CreateCopy(PdfFontBytes.Value);
        using var typeface = SKTypeface.FromData(fontData)
            ?? throw new InvalidOperationException("The embedded PDF font could not be loaded.");
        using var regularFont = new SKFont(typeface, PdfBodySize);
        using var boldFont = new SKFont(typeface, 8.2f);
        using var textPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill };
        using var strokePaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 0.7f, IsAntialias = true };
        using var logo = SKBitmap.Decode(ProjectFlowHiveBrandAssets.LogoJpeg)
            ?? throw new InvalidOperationException("The embedded US Signal logo could not be decoded.");

        using var output = new MemoryStream();
        using var document = SKDocument.CreatePdf(output);
        if (artifact.ArtifactKind.Equals("gantt", StringComparison.OrdinalIgnoreCase)
            && TryBuildScheduleRows(artifact, isMonthlyCalendar: false, out var ganttRows))
        {
            var scheduledRows = ganttRows.Where(row => row.IsScheduled).ToArray();
            var graphPages = scheduledRows.Length == 0
                ? []
                : scheduledRows.Chunk(10).Select(rows => rows.ToArray()).ToArray();
            var detailPages = PaginateScheduleDetails(ganttRows, regularFont, textPaint);
            var totalPages = graphPages.Length + detailPages.Count;
            if (scheduledRows.Length > 0)
            {
                var firstDate = scheduledRows.Min(row => row.Start!.Value);
                var lastDate = scheduledRows.Max(row => row.End!.Value);
                for (var index = 0; index < graphPages.Length; index++)
                {
                    using var canvas = document.BeginPage(PdfWidth, PdfHeight);
                    DrawGanttPage(canvas, artifact, graphPages[index], firstDate, lastDate,
                        index + 1, totalPages, regularFont, boldFont, textPaint, fillPaint, strokePaint, logo);
                    document.EndPage();
                }
            }
            for (var index = 0; index < detailPages.Count; index++)
            {
                using var canvas = document.BeginPage(PdfWidth, PdfHeight);
                DrawScheduleDetailsPage(canvas, artifact, detailPages[index],
                    graphPages.Length + index + 1, totalPages, regularFont, boldFont, textPaint,
                    fillPaint, strokePaint, logo);
                document.EndPage();
            }
        }
        else if (artifact.ArtifactKind.Equals("monthly-calendar", StringComparison.OrdinalIgnoreCase)
            && TryBuildScheduleRows(artifact, isMonthlyCalendar: true, out var calendarRows))
        {
            var scheduledRows = calendarRows.Where(row => row.IsScheduled).ToArray();
            var graphPages = scheduledRows.Length == 0
                ? []
                : EnumerateMonths(scheduledRows.Min(row => row.Start!.Value), scheduledRows.Max(row => row.End!.Value));
            var detailPages = PaginateScheduleDetails(calendarRows, regularFont, textPaint);
            var totalPages = graphPages.Count + detailPages.Count;
            for (var index = 0; index < graphPages.Count; index++)
            {
                using var canvas = document.BeginPage(PdfWidth, PdfHeight);
                DrawMonthlyCalendarPage(canvas, artifact, scheduledRows, graphPages[index],
                    index + 1, totalPages, regularFont, boldFont, textPaint, fillPaint, strokePaint, logo);
                document.EndPage();
            }
            for (var index = 0; index < detailPages.Count; index++)
            {
                using var canvas = document.BeginPage(PdfWidth, PdfHeight);
                DrawScheduleDetailsPage(canvas, artifact, detailPages[index],
                    graphPages.Count + index + 1, totalPages, regularFont, boldFont, textPaint,
                    fillPaint, strokePaint, logo);
                document.EndPage();
            }
        }
        else
        {
            var columnBands = artifact.Columns.Count == 0
                ? new[] { Array.Empty<(string name, int index)>() }
                : artifact.Columns.Select((name, index) => (name, index)).Chunk(8).ToArray();
            var pages = new List<(IReadOnlyList<IReadOnlyList<object?>> Rows, IReadOnlyList<string> Columns)>();
            foreach (var band in columnBands)
            {
                var columns = band.Select(item => item.name).ToArray();
                var bandRows = artifact.Rows
                    .Select(row => (IReadOnlyList<object?>)band
                        .Select(item => item.index < row.Count ? row[item.index] : null)
                        .ToArray())
                    .ToArray();
                pages.AddRange(PaginateRows(bandRows, columns, regularFont, textPaint)
                    .Select(page => (page, (IReadOnlyList<string>)columns)));
            }
            for (var index = 0; index < pages.Count; index++)
            {
                using var canvas = document.BeginPage(PdfWidth, PdfHeight);
                DrawPdfPage(canvas, artifact, pages[index].Rows, pages[index].Columns, index + 1, pages.Count,
                    regularFont, boldFont, textPaint, fillPaint, strokePaint, logo);
                document.EndPage();
            }
        }
        document.Close();
        return output.ToArray();
    }

    private static List<IReadOnlyList<IReadOnlyList<object?>>> PaginateRows(
        IReadOnlyList<IReadOnlyList<object?>> sourceRows,
        IReadOnlyList<string> visibleColumns,
        SKFont font,
        SKPaint paint)
    {
        var pages = new List<IReadOnlyList<IReadOnlyList<object?>>>();
        var current = new List<IReadOnlyList<object?>>();
        var remaining = 350f;
        foreach (var row in sourceRows)
        {
            var height = PdfRowHeight(row, visibleColumns, font, paint);
            if (current.Count > 0 && height > remaining)
            {
                pages.Add(current.ToArray());
                current = new List<IReadOnlyList<object?>>();
                remaining = 350f;
            }
            current.Add(row);
            remaining -= Math.Min(height, 350f);
        }
        if (current.Count > 0 || pages.Count == 0) pages.Add(current.ToArray());
        return pages;
    }

    private static float PdfRowHeight(
        IReadOnlyList<object?> row,
        IReadOnlyList<string> visibleColumns,
        SKFont font,
        SKPaint paint)
    {
        var width = PdfWidth - (PdfLeft * 2);
        var columnWidth = width / Math.Max(1, visibleColumns.Count);
        var lines = 1;
        for (var column = 0; column < visibleColumns.Count; column++)
        {
            var value = column < row.Count ? DisplayValue(row[column]) : string.Empty;
            lines = Math.Max(lines, WrapText(value, font, paint, columnWidth - 8f).Count);
        }
        return 18f + ((lines - 1) * 7.2f);
    }

    private static void DrawPdfPage(
        SKCanvas canvas,
        ProjectFlowHivePsaArtifactTable artifact,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        IReadOnlyList<string> visibleColumns,
        int pageNumber,
        int pageCount,
        SKFont regularFont,
        SKFont boldFont,
        SKPaint textPaint,
        SKPaint fillPaint,
        SKPaint strokePaint,
        SKBitmap logo)
    {
        var navy = new SKColor(0x0B, 0x2B, 0x4B);
        var cyan = new SKColor(0x0B, 0x6E, 0x99);
        var body = new SKColor(0x0F, 0x29, 0x44);
        var muted = new SKColor(0x57, 0x6A, 0x7B);
        var pale = new SKColor(0xF2, 0xF8, 0xFC);

        DrawPdfHeader(canvas, artifact, regularFont, boldFont, textPaint, logo, navy, cyan, body, muted);

        var left = PdfLeft;
        var tableWidth = PdfRight - PdfLeft;
        var columnWidth = tableWidth / Math.Max(1, visibleColumns.Count);
        const float headerBottom = 430f;
        const float headerTop = 454f;
        fillPaint.Color = navy;
        canvas.DrawRect(PdfRect(left, headerBottom, PdfRight, headerTop), fillPaint);
        for (var column = 0; column < visibleColumns.Count; column++)
        {
            DrawWrappedText(canvas, boldFont, textPaint, visibleColumns[column].ToUpperInvariant(), left + (column * columnWidth) + 4, 446, columnWidth - 8, SKColors.White, 2);
        }

        var top = 424f;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var rowHeight = PdfRowHeight(rows[rowIndex], visibleColumns, regularFont, textPaint);
            fillPaint.Color = rowIndex % 2 == 0 ? pale : SKColors.White;
            canvas.DrawRect(PdfRect(left, top - rowHeight, PdfRight, top), fillPaint);
            strokePaint.Color = new SKColor(0xD0, 0xDD, 0xE6);
            canvas.DrawRect(PdfRect(left, top - rowHeight, PdfRight, top), strokePaint);
            for (var column = 0; column < visibleColumns.Count; column++)
            {
                var value = column < rows[rowIndex].Count ? DisplayValue(rows[rowIndex][column]) : string.Empty;
                DrawWrappedText(canvas, regularFont, textPaint, value, left + (column * columnWidth) + 4, top - 11, columnWidth - 8, body, 20);
            }
            top -= rowHeight;
        }

        DrawPdfFooter(canvas, artifact, pageNumber, pageCount, regularFont, textPaint, strokePaint, muted);
    }

    private static void DrawPdfHeader(
        SKCanvas canvas,
        ProjectFlowHivePsaArtifactTable artifact,
        SKFont regularFont,
        SKFont boldFont,
        SKPaint textPaint,
        SKBitmap logo,
        SKColor navy,
        SKColor cyan,
        SKColor body,
        SKColor muted)
    {
        canvas.DrawBitmap(logo, PdfRect(36, 520, 122, 577), new SKSamplingOptions(SKFilterMode.Linear));
        DrawText(canvas, boldFont, textPaint, "US Signal Project FlowHive", 135, 568, navy);
        DrawText(canvas, boldFont, textPaint, ControlLabel, 135, 548, cyan);
        DrawWrappedText(canvas, boldFont, textPaint, artifact.Title, 36, 516, 936, navy, 1);
        DrawText(canvas, regularFont, textPaint, $"Project: {Join(artifact.ProjectCode, artifact.ProjectName)}", 36, 496, body);
        DrawText(canvas, regularFont, textPaint, $"Customer: {artifact.CustomerName}", 520, 496, body);
        DrawText(canvas, regularFont, textPaint, $"Generated UTC: {DateTimeOffset.UtcNow:O}", 36, 480, muted);
        DrawWrappedText(canvas, regularFont, textPaint, $"Notes: {string.Join(" - ", artifact.Notes)}", 36, 464, 936, muted, 2);
    }

    private static void DrawPdfFooter(
        SKCanvas canvas,
        ProjectFlowHivePsaArtifactTable artifact,
        int pageNumber,
        int pageCount,
        SKFont regularFont,
        SKPaint textPaint,
        SKPaint strokePaint,
        SKColor muted)
    {
        strokePaint.Color = new SKColor(0xAD, 0xC4, 0xD3);
        canvas.DrawLine(36, PdfScreenY(53), 972, PdfScreenY(53), strokePaint);
        DrawText(canvas, regularFont, textPaint, $"Artifact type: {artifact.ArtifactKind} · Logo SHA-256 {ProjectFlowHiveBrandAssets.LogoSha256}", 36, 35, muted);
        DrawText(canvas, regularFont, textPaint, $"Page {pageNumber} of {pageCount}", 915, 35, muted);
    }

    private sealed record PdfScheduleRow(
        string Id,
        string Label,
        string Secondary,
        string Status,
        bool Critical,
        DateOnly? Start,
        DateOnly? End,
        string StartText,
        string EndText,
        string Issue)
    {
        public bool IsScheduled => Start.HasValue && End.HasValue && string.IsNullOrEmpty(Issue);
    }

    private sealed record PdfScheduleDetailPage(IReadOnlyList<PdfScheduleRow> Rows);

    private static bool TryBuildScheduleRows(
        ProjectFlowHivePsaArtifactTable artifact,
        bool isMonthlyCalendar,
        out List<PdfScheduleRow> scheduleRows)
    {
        scheduleRows = [];
        var startColumn = isMonthlyCalendar ? 0 : 2;
        var endColumn = isMonthlyCalendar ? 1 : 3;
        var idColumn = isMonthlyCalendar ? 2 : 0;
        var labelColumn = isMonthlyCalendar ? 4 : 1;
        var secondaryColumn = isMonthlyCalendar ? 5 : 0;
        var statusColumn = 6;

        for (var rowIndex = 0; rowIndex < artifact.Rows.Count; rowIndex++)
        {
            var row = artifact.Rows[rowIndex];
            var id = row.Count > idColumn ? DisplayValue(row[idColumn]).Trim() : string.Empty;
            if (id.Length == 0) id = $"ROW-{rowIndex + 1:000}";
            var label = row.Count > labelColumn ? DisplayValue(row[labelColumn]).Trim() : string.Empty;
            if (label.Length == 0) label = "Unlabeled task";
            var secondary = row.Count > secondaryColumn ? DisplayValue(row[secondaryColumn]).Trim() : string.Empty;
            var status = row.Count > statusColumn ? DisplayValue(row[statusColumn]).Trim() : string.Empty;
            var startValue = row.Count > startColumn ? row[startColumn] : null;
            var endValue = row.Count > endColumn ? row[endColumn] : null;
            var startText = DisplayValue(startValue).Trim();
            var endText = DisplayValue(endValue).Trim();
            var startValid = TryReadPdfDate(startValue, out var start);
            var endValid = TryReadPdfDate(endValue, out var end);
            var issue = string.Empty;
            if (row.Count <= Math.Max(Math.Max(startColumn, endColumn), labelColumn))
                issue = "Unscheduled: required schedule fields are missing.";
            else if (string.IsNullOrWhiteSpace(startText) || string.IsNullOrWhiteSpace(endText))
                issue = "Unscheduled: start or end date is missing.";
            else if (!startValid || !endValid)
                issue = "Unscheduled: start or end date is invalid.";
            else if (end < start)
                issue = "Invalid schedule: end date precedes start date; dates were not swapped.";

            scheduleRows.Add(new PdfScheduleRow(
                id, label, secondary, status,
                !isMonthlyCalendar && row.Count > 6 && string.Equals(DisplayValue(row[6]), "Yes", StringComparison.OrdinalIgnoreCase),
                startValid ? start : null,
                endValid ? end : null,
                startText.Length == 0 ? "(missing)" : startText,
                endText.Length == 0 ? "(missing)" : endText,
                issue));
        }

        return scheduleRows.Count > 0;
    }

    private static List<PdfScheduleDetailPage> PaginateScheduleDetails(
        IReadOnlyList<PdfScheduleRow> rows,
        SKFont regularFont,
        SKPaint paint)
    {
        var pages = new List<PdfScheduleDetailPage>();
        var current = new List<PdfScheduleRow>();
        var remaining = 350f;
        foreach (var row in rows)
        {
            var height = ScheduleDetailRowHeight(row, regularFont, paint);
            if (current.Count > 0 && height > remaining)
            {
                pages.Add(new PdfScheduleDetailPage(current.ToArray()));
                current = [];
                remaining = 350f;
            }
            current.Add(row);
            remaining -= Math.Min(height, 350f);
        }
        if (current.Count > 0 || pages.Count == 0) pages.Add(new PdfScheduleDetailPage(current.ToArray()));
        return pages;
    }

    private static float ScheduleDetailRowHeight(PdfScheduleRow row, SKFont font, SKPaint paint)
    {
        var labelLines = WrapText(row.Label, font, paint, PdfRight - PdfLeft - 12).Count;
        var issueLines = WrapText(row.Issue, font, paint, PdfRight - PdfLeft - 12).Count;
        return 24f + ((labelLines + issueLines) * 7.2f);
    }

    private static void DrawScheduleDetailsPage(
        SKCanvas canvas,
        ProjectFlowHivePsaArtifactTable artifact,
        PdfScheduleDetailPage page,
        int pageNumber,
        int pageCount,
        SKFont regularFont,
        SKFont boldFont,
        SKPaint textPaint,
        SKPaint fillPaint,
        SKPaint strokePaint,
        SKBitmap logo)
    {
        var navy = new SKColor(0x0B, 0x2B, 0x4B);
        var body = new SKColor(0x0F, 0x29, 0x44);
        var muted = new SKColor(0x57, 0x6A, 0x7B);
        var pale = new SKColor(0xF2, 0xF8, 0xFC);
        DrawPdfHeader(canvas, artifact, regularFont, boldFont, textPaint, logo, navy,
            new SKColor(0x0B, 0x6E, 0x99), body, muted);
        DrawText(canvas, boldFont, textPaint, "Task details appendix", 36, 438, navy);
        DrawText(canvas, regularFont, textPaint,
            "Complete task identity and schedule evidence. Invalid or unscheduled rows are reported without invented dates.",
            36, 425, muted);

        var top = 405f;
        for (var index = 0; index < page.Rows.Count; index++)
        {
            var row = page.Rows[index];
            var rowHeight = ScheduleDetailRowHeight(row, regularFont, textPaint);
            var bottom = top - rowHeight;
            fillPaint.Color = index % 2 == 0 ? pale : SKColors.White;
            canvas.DrawRect(PdfRect(PdfLeft, bottom, PdfRight, top), fillPaint);
            strokePaint.Color = new SKColor(0xD0, 0xDD, 0xE6);
            canvas.DrawRect(PdfRect(PdfLeft, bottom, PdfRight, top), strokePaint);
            DrawText(canvas, boldFont, textPaint, $"ID: {row.Id} |", PdfLeft + 6, top - 10, navy);
            var labelLines = WrapText(row.Label, regularFont, textPaint, PdfRight - PdfLeft - 12);
            for (var line = 0; line < labelLines.Count; line++)
                DrawText(canvas, regularFont, textPaint, $"Task: {labelLines[line]}", PdfLeft + 6,
                    top - 18 - (line * 7.2f), body);
            var metadataY = top - 18 - (labelLines.Count * 7.2f);
            DrawText(canvas, regularFont, textPaint,
                $"Dates: {row.StartText} -> {row.EndText}   Owner/WBS: {row.Secondary}   Status: {row.Status}",
                PdfLeft + 6, metadataY, body);
            if (!string.IsNullOrEmpty(row.Issue))
            {
                var issueLines = WrapText(row.Issue, regularFont, textPaint, PdfRight - PdfLeft - 12);
                for (var line = 0; line < issueLines.Count; line++)
                    DrawText(canvas, regularFont, textPaint, $"Issue: {issueLines[line]}", PdfLeft + 6,
                        metadataY - 8 - (line * 7.2f), new SKColor(0xB7, 0x3A, 0x3A));
            }
            top = bottom;
        }
        DrawPdfFooter(canvas, artifact, pageNumber, pageCount, regularFont, textPaint, strokePaint, muted);
    }

    private static bool TryReadPdfDate(object? value, out DateOnly date)
    {
        switch (value)
        {
            case DateOnly dateOnly:
                date = dateOnly;
                return true;
            case DateTimeOffset dateTimeOffset:
                date = DateOnly.FromDateTime(dateTimeOffset.UtcDateTime);
                return true;
            case DateTime dateTime:
                date = DateOnly.FromDateTime(dateTime);
                return true;
        }

        return DateOnly.TryParse(
            DisplayValue(value),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out date);
    }

    private static List<DateOnly> EnumerateMonths(DateOnly firstDate, DateOnly lastDate)
    {
        var month = new DateOnly(firstDate.Year, firstDate.Month, 1);
        var lastMonth = new DateOnly(lastDate.Year, lastDate.Month, 1);
        var months = new List<DateOnly>();
        while (month <= lastMonth)
        {
            months.Add(month);
            month = month.AddMonths(1);
        }
        return months;
    }

    private static void DrawGanttPage(
        SKCanvas canvas,
        ProjectFlowHivePsaArtifactTable artifact,
        IReadOnlyList<PdfScheduleRow> rows,
        DateOnly firstDate,
        DateOnly lastDate,
        int pageNumber,
        int pageCount,
        SKFont regularFont,
        SKFont boldFont,
        SKPaint textPaint,
        SKPaint fillPaint,
        SKPaint strokePaint,
        SKBitmap logo)
    {
        var navy = new SKColor(0x0B, 0x2B, 0x4B);
        var cyan = new SKColor(0x0B, 0x6E, 0x99);
        var body = new SKColor(0x0F, 0x29, 0x44);
        var muted = new SKColor(0x57, 0x6A, 0x7B);
        var pale = new SKColor(0xF2, 0xF8, 0xFC);
        var critical = new SKColor(0xB7, 0x3A, 0x3A);

        DrawPdfHeader(canvas, artifact, regularFont, boldFont, textPaint, logo, navy, cyan, body, muted);
        DrawText(canvas, boldFont, textPaint, "Graphical Gantt schedule", 36, 438, navy);

        const float labelLeft = 36f;
        const float labelRight = 308f;
        const float timelineLeft = 316f;
        const float timelineRight = 972f;
        const float headerBottom = 406f;
        const float headerTop = 428f;
        const float rowHeight = 23f;
        const float firstRowTop = 402f;
        var totalDays = Math.Max(1, lastDate.DayNumber - firstDate.DayNumber + 1);
        var timelineWidth = timelineRight - timelineLeft;

        fillPaint.Color = navy;
        canvas.DrawRect(PdfRect(labelLeft, headerBottom, timelineRight, headerTop), fillPaint);
        DrawText(canvas, boldFont, textPaint, "WBS / TASK", labelLeft + 4, 419, SKColors.White);
        DrawText(canvas, boldFont, textPaint, "SCHEDULE / DATES", timelineLeft + 4, 420, SKColors.White);

        for (var dayOffset = 0; dayOffset <= totalDays; dayOffset += 7)
        {
            var x = timelineLeft + (dayOffset / (float)totalDays * timelineWidth);
            if (dayOffset < totalDays)
            {
                var tickDate = firstDate.AddDays(dayOffset);
                DrawText(canvas, regularFont, textPaint, tickDate.ToString("MM/dd", CultureInfo.InvariantCulture), x + 2, 410, SKColors.White);
            }
        }

        var top = firstRowTop;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var bottom = top - rowHeight;
            fillPaint.Color = rowIndex % 2 == 0 ? pale : SKColors.White;
            canvas.DrawRect(PdfRect(labelLeft, bottom, timelineRight, top), fillPaint);
            strokePaint.Color = new SKColor(0xD0, 0xDD, 0xE6);
            canvas.DrawRect(PdfRect(labelLeft, bottom, timelineRight, top), strokePaint);
            for (var dayOffset = 0; dayOffset <= totalDays; dayOffset += 7)
            {
                var x = timelineLeft + (dayOffset / (float)totalDays * timelineWidth);
                strokePaint.Color = new SKColor(0xC4, 0xD5, 0xE0);
                canvas.DrawLine(x, PdfScreenY(bottom), x, PdfScreenY(top), strokePaint);
            }
            DrawWrappedText(canvas, regularFont, textPaint, $"{row.Id}: {row.Label}", labelLeft + 4, top - 8,
                labelRight - labelLeft - 8, body, 3);

            var startOffset = Math.Clamp(row.Start!.Value.DayNumber - firstDate.DayNumber, 0, totalDays - 1);
            var endOffset = Math.Clamp(row.End!.Value.DayNumber - firstDate.DayNumber + 1, 1, totalDays);
            var barLeft = timelineLeft + (startOffset / (float)totalDays * timelineWidth) + 1;
            var barRight = timelineLeft + (endOffset / (float)totalDays * timelineWidth) - 1;
            fillPaint.Color = row.Critical ? critical : cyan;
            canvas.DrawRoundRect(PdfRect(barLeft, bottom + 6, Math.Max(barLeft + 4, barRight), top - 6), 3, 3, fillPaint);
            if (row.Critical)
                DrawText(canvas, regularFont, textPaint, "critical", timelineRight - 38, top - 8, critical);
            top = bottom;
        }

        DrawText(canvas, regularFont, textPaint, "Blue = scheduled work   Red = critical path", 36, 70, muted);
        DrawText(canvas, regularFont, textPaint, "Full task details and unscheduled rows: Task details appendix", 36, 59, muted);
        DrawPdfFooter(canvas, artifact, pageNumber, pageCount, regularFont, textPaint, strokePaint, muted);
    }

    private static void DrawMonthlyCalendarPage(
        SKCanvas canvas,
        ProjectFlowHivePsaArtifactTable artifact,
        IReadOnlyList<PdfScheduleRow> rows,
        DateOnly month,
        int pageNumber,
        int pageCount,
        SKFont regularFont,
        SKFont boldFont,
        SKPaint textPaint,
        SKPaint fillPaint,
        SKPaint strokePaint,
        SKBitmap logo)
    {
        var navy = new SKColor(0x0B, 0x2B, 0x4B);
        var cyan = new SKColor(0x0B, 0x6E, 0x99);
        var body = new SKColor(0x0F, 0x29, 0x44);
        var muted = new SKColor(0x57, 0x6A, 0x7B);
        var pale = new SKColor(0xF2, 0xF8, 0xFC);
        var monthStart = month;
        var monthEnd = month.AddDays(DateTime.DaysInMonth(month.Year, month.Month) - 1);

        DrawPdfHeader(canvas, artifact, regularFont, boldFont, textPaint, logo, navy, cyan, body, muted);
        DrawText(canvas, boldFont, textPaint, $"Graphical monthly calendar - {month:MMMM yyyy}", 36, 438, navy);

        const float calendarLeft = 36f;
        const float calendarRight = 972f;
        const float weekdayBottom = 398f;
        const float weekdayTop = 420f;
        const float gridTop = 395f;
        const float gridBottom = 90f;
        var cellWidth = (calendarRight - calendarLeft) / 7f;
        var cellHeight = (gridTop - gridBottom) / 6f;
        var weekdays = new[] { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };

        fillPaint.Color = navy;
        canvas.DrawRect(PdfRect(calendarLeft, weekdayBottom, calendarRight, weekdayTop), fillPaint);
        for (var weekday = 0; weekday < weekdays.Length; weekday++)
            DrawText(canvas, boldFont, textPaint, weekdays[weekday], calendarLeft + weekday * cellWidth + 4, 411, SKColors.White);

        for (var cell = 0; cell < 42; cell++)
        {
            var week = cell / 7;
            var weekday = cell % 7;
            var date = monthStart.AddDays(cell - (int)monthStart.DayOfWeek);
            var cellTop = gridTop - week * cellHeight;
            var cellBottom = cellTop - cellHeight;
            var cellLeft = calendarLeft + weekday * cellWidth;
            var cellRight = cellLeft + cellWidth;
            var inMonth = date >= monthStart && date <= monthEnd;
            fillPaint.Color = inMonth ? (week % 2 == 0 ? SKColors.White : pale) : new SKColor(0xFA, 0xFB, 0xFC);
            canvas.DrawRect(PdfRect(cellLeft, cellBottom, cellRight, cellTop), fillPaint);
            strokePaint.Color = new SKColor(0xD0, 0xDD, 0xE6);
            canvas.DrawRect(PdfRect(cellLeft, cellBottom, cellRight, cellTop), strokePaint);
            if (!inMonth) continue;

            DrawText(canvas, boldFont, textPaint, date.Day.ToString(CultureInfo.InvariantCulture), cellLeft + 4, cellTop - 10, body);
            var activeRows = rows.Where(row => row.IsScheduled && row.Start <= date && row.End >= date).ToArray();
            var visibleRows = activeRows.Take(3).ToArray();
            for (var taskIndex = 0; taskIndex < visibleRows.Length; taskIndex++)
            {
                var row = visibleRows[taskIndex];
                var chipTop = cellTop - 16 - (taskIndex * 11);
                var chipBottom = chipTop - 9;
                fillPaint.Color = row.Critical ? new SKColor(0xB7, 0x3A, 0x3A) : cyan;
                canvas.DrawRoundRect(PdfRect(cellLeft + 3, chipBottom, cellRight - 3, chipTop), 2, 2, fillPaint);
                DrawText(canvas, regularFont, textPaint,
                    FitText($"{row.Id}: {row.Label}", regularFont, textPaint, cellWidth - 12, $"{row.Id} (appendix)"),
                    cellLeft + 6, chipTop - 7, SKColors.White);
            }
            if (activeRows.Length > visibleRows.Length)
            {
                var overflowTop = cellTop - 16 - (visibleRows.Length * 11);
                DrawText(canvas, regularFont, textPaint,
                    $"OVERFLOW-{activeRows.Length - visibleRows.Length}-MORE-SEE-APPENDIX",
                    cellLeft + 5, overflowTop - 7, muted);
            }
        }

        DrawText(canvas, regularFont, textPaint, "Tasks spanning a date remain visible in each active day cell.", 36, 70, muted);
        DrawText(canvas, regularFont, textPaint, "Full task details and overflow/unscheduled rows: Task details appendix", 36, 59, muted);
        DrawPdfFooter(canvas, artifact, pageNumber, pageCount, regularFont, textPaint, strokePaint, muted);
    }

    private static void DrawText(SKCanvas canvas, SKFont font, SKPaint paint, string value, float x, float y, SKColor color)
    {
        paint.Color = color;
        canvas.DrawText(value ?? string.Empty, x, PdfScreenY(y), SKTextAlign.Left, font, paint);
    }

    private static float PdfScreenY(float y) => PdfHeight - y;

    private static SKRect PdfRect(float left, float bottom, float right, float top) =>
        new(left, PdfScreenY(top), right, PdfScreenY(bottom));

    private static void DrawWrappedText(
        SKCanvas canvas,
        SKFont font,
        SKPaint paint,
        string value,
        float x,
        float firstBaseline,
        float width,
        SKColor color,
        int maximumLines)
    {
        var lines = WrapText(value, font, paint, width).Take(maximumLines).ToArray();
        for (var index = 0; index < lines.Length; index++)
            DrawText(canvas, font, paint, lines[index], x, firstBaseline - (index * 7.2f), color);
    }

    private static IReadOnlyList<string> WrapText(string? value, SKFont font, SKPaint paint, float width)
    {
        var text = (value ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal);
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var current = new StringBuilder();
            foreach (var word in paragraph.Split(' ', StringSplitOptions.None))
            {
                var candidate = current.Length == 0 ? word : $"{current} {word}";
                if (candidate.Length > 0 && font.MeasureText(candidate, paint) <= width)
                {
                    current.Clear().Append(candidate);
                    continue;
                }
                if (current.Length > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }
                foreach (var rune in word.EnumerateRunes())
                {
                    var runeText = rune.ToString();
                    if (current.Length > 0 && font.MeasureText(current.ToString() + runeText, paint) > width)
                    {
                        lines.Add(current.ToString());
                        current.Clear();
                    }
                    current.Append(runeText);
                }
            }
            if (current.Length > 0 || paragraph.Length == 0) lines.Add(current.ToString());
        }
        return lines.Count == 0 ? [string.Empty] : lines;
    }

    private static string FitText(string value, SKFont font, SKPaint paint, float width, string overflowLabel)
    {
        if (font.MeasureText(value, paint) <= width) return value;
        return font.MeasureText(overflowLabel, paint) <= width ? overflowLabel : "(appendix)";
    }
    private static void SetSpreadsheetText(IXLCell cell, string? value) => SetSpreadsheetTextCore(cell, value);

    private static void SetSpreadsheetValue(IXLCell cell, object? value)
    {
        if (value is null)
        {
            cell.Clear(XLClearOptions.Contents);
            return;
        }

        if (value is DateOnly date)
        {
            cell.Value = XLCellValue.FromObject(date.ToDateTime(TimeOnly.MinValue), CultureInfo.InvariantCulture);
            cell.Style.DateFormat.Format = "yyyy-mm-dd";
            return;
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            cell.Value = XLCellValue.FromObject(dateTimeOffset.UtcDateTime, CultureInfo.InvariantCulture);
            cell.Style.DateFormat.Format = "yyyy-mm-dd";
            return;
        }

        if (value is DateTime dateTime)
        {
            cell.Value = XLCellValue.FromObject(dateTime, CultureInfo.InvariantCulture);
            cell.Style.DateFormat.Format = "yyyy-mm-dd";
            return;
        }

        if (value is string text)
        {
            SetSpreadsheetTextCore(cell, text);
            return;
        }

        cell.Value = XLCellValue.FromObject(value, CultureInfo.InvariantCulture);
    }

    private static void SetSpreadsheetTextCore(IXLCell cell, string? value) => cell.Value = SpreadsheetText(value);

    private static string SpreadsheetText(string? value)
    {
        var text = value ?? string.Empty;
        var trimmed = text.TrimStart();
        var formulaLike = trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@';
        var preservesLiteralApostrophe = text.StartsWith("'", StringComparison.Ordinal);
        return formulaLike || preservesLiteralApostrophe
            ? $"'{text}"
            : text;
    }

    private static string DisplayValue(object? value) => value switch
    {
        null => string.Empty,
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty
    };
    private static string Join(string? left, string? right) => string.Join(" · ", new[] { left, right }.Where(value => !string.IsNullOrWhiteSpace(value)));
    private static string SafeSheetName(string value)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var safe = new string((value ?? "Artifact").Select(character => invalid.Contains(character) ? '-' : character).ToArray()).Trim();
        if (safe.Length == 0) safe = "Artifact";
        return safe[..Math.Min(safe.Length, 31)];
    }
}
