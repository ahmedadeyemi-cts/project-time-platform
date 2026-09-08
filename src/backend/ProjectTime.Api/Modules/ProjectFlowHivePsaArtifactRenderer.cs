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

        var visibleColumns = artifact.Columns.Take(8).ToArray();
        var pages = PaginateRows(artifact, visibleColumns, regularFont, textPaint);
        using var output = new MemoryStream();
        using var document = SKDocument.CreatePdf(output);
        for (var index = 0; index < pages.Count; index++)
        {
            using var canvas = document.BeginPage(PdfWidth, PdfHeight);
            DrawPdfPage(canvas, artifact, pages[index], visibleColumns, index + 1, pages.Count,
                regularFont, boldFont, textPaint, fillPaint, strokePaint, logo);
            document.EndPage();
        }
        document.Close();
        return output.ToArray();
    }

    private static List<IReadOnlyList<IReadOnlyList<object?>>> PaginateRows(
        ProjectFlowHivePsaArtifactTable artifact,
        IReadOnlyList<string> visibleColumns,
        SKFont font,
        SKPaint paint)
    {
        var pages = new List<IReadOnlyList<IReadOnlyList<object?>>>();
        var current = new List<IReadOnlyList<object?>>();
        var remaining = 350f;
        foreach (var row in artifact.Rows)
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

        canvas.DrawBitmap(logo, PdfRect(36, 520, 122, 577), new SKSamplingOptions(SKFilterMode.Linear));
        DrawText(canvas, boldFont, textPaint, "US Signal Project FlowHive", 135, 568, navy);
        DrawText(canvas, boldFont, textPaint, ControlLabel, 135, 548, cyan);
        DrawWrappedText(canvas, boldFont, textPaint, artifact.Title, 36, 516, 936, navy, 1);
        DrawText(canvas, regularFont, textPaint, $"Project: {Join(artifact.ProjectCode, artifact.ProjectName)}", 36, 496, body);
        DrawText(canvas, regularFont, textPaint, $"Customer: {artifact.CustomerName}", 520, 496, body);
        DrawText(canvas, regularFont, textPaint, $"Generated UTC: {DateTimeOffset.UtcNow:O}", 36, 480, muted);
        DrawWrappedText(canvas, regularFont, textPaint, $"Notes: {string.Join(" - ", artifact.Notes)}", 36, 464, 936, muted, 2);

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

        strokePaint.Color = new SKColor(0xAD, 0xC4, 0xD3);
        canvas.DrawLine(36, PdfScreenY(53), 972, PdfScreenY(53), strokePaint);
        DrawText(canvas, regularFont, textPaint, $"Artifact type: {artifact.ArtifactKind} · Logo SHA-256 {ProjectFlowHiveBrandAssets.LogoSha256}", 36, 35, muted);
        DrawText(canvas, regularFont, textPaint, $"Page {pageNumber} of {pageCount}", 915, 35, muted);
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
    private static string Truncate(string? value, int length) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim()[..Math.Min(value.Trim().Length, length)];
    private static string Join(string? left, string? right) => string.Join(" · ", new[] { left, right }.Where(value => !string.IsNullOrWhiteSpace(value)));
    private static string SafeSheetName(string value)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var safe = new string((value ?? "Artifact").Select(character => invalid.Contains(character) ? '-' : character).ToArray()).Trim();
        if (safe.Length == 0) safe = "Artifact";
        return safe[..Math.Min(safe.Length, 31)];
    }
}
