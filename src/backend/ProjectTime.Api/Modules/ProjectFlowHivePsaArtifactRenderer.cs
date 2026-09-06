using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace ProjectTime.Api.Modules;

internal sealed record ProjectFlowHivePsaArtifactTable(
    string ArtifactKind,
    string Title,
    string ProjectCode,
    string ProjectName,
    string CustomerName,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<string> Notes);

internal static class ProjectFlowHivePsaArtifactRenderer
{
    private const string ControlLabel = "US SIGNAL PROJECT DELIVERY ARTIFACT";

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
        summary.Cell("C2").Value = CONTROLLabel();
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
            summary.Cell(index + 5, 1).Value = summaryRows[index].Item1;
            summary.Cell(index + 5, 2).Value = summaryRows[index].Item2;
            summary.Cell(index + 5, 1).Style.Font.Bold = true;
        }
        var noteRow = summaryRows.Length + 6;
        summary.Cell(noteRow, 1).Value = "Notes";
        summary.Cell(noteRow, 1).Style.Font.Bold = true;
        summary.Cell(noteRow, 2).Value = string.Join("\n", artifact.Notes);
        summary.Cell(noteRow, 2).Style.Alignment.WrapText = true;
        summary.Column(1).Width = 22;
        summary.Column(2).Width = 85;
        summary.Row(noteRow).Height = Math.Max(36, artifact.Notes.Count * 18);

        var sheet = workbook.Worksheets.Add(SafeSheetName(artifact.ArtifactKind));
        for (var column = 0; column < artifact.Columns.Count; column++)
        {
            sheet.Cell(1, column + 1).Value = artifact.Columns[column];
        }
        for (var row = 0; row < artifact.Rows.Count; row++)
        {
            for (var column = 0; column < artifact.Columns.Count; column++)
            {
                sheet.Cell(row + 2, column + 1).Value = column < artifact.Rows[row].Count
                    ? artifact.Rows[row][column]
                    : string.Empty;
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
            sheet.Columns().AdjustToContents(8, 48);
            sheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            sheet.Style.Alignment.WrapText = true;
        }

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    internal static byte[] BuildPdf(ProjectFlowHivePsaArtifactTable artifact)
    {
        const int rowsPerPage = 18;
        var pages = artifact.Rows.Chunk(rowsPerPage).Select(rows => rows.ToArray()).ToList();
        if (pages.Count == 0) pages.Add([]);
        var contents = pages.Select((rows, index) => BuildPdfPage(artifact, rows, index + 1, pages.Count)).ToArray();
        return BuildPdfDocument(contents, ProjectFlowHiveBrandAssets.LogoJpeg);
    }

    private static string BuildPdfPage(
        ProjectFlowHivePsaArtifactTable artifact,
        IReadOnlyList<IReadOnlyList<string>> rows,
        int pageNumber,
        int pageCount)
    {
        var content = new StringBuilder();
        content.Append("q 86 0 0 57 36 520 cm /Im1 Do Q\n");
        PdfText(content, 135, 568, 18, "US Signal Project FlowHive", true, "0.04 0.17 0.29");
        PdfText(content, 135, 548, 9, CONTROLLabel(), true, "0.04 0.43 0.60");
        PdfText(content, 36, 512, 13, Truncate(artifact.Title, 110), true, "0.04 0.17 0.29");
        PdfText(content, 36, 494, 8, $"Project: {Truncate(Join(artifact.ProjectCode, artifact.ProjectName), 90)}", false, "0.18 0.25 0.34");
        PdfText(content, 520, 494, 8, $"Customer: {Truncate(artifact.CustomerName, 60)}", false, "0.18 0.25 0.34");
        PdfText(content, 36, 478, 7, $"Generated UTC: {DateTimeOffset.UtcNow:O}", false, "0.34 0.42 0.50");

        var visibleColumns = artifact.Columns.Take(8).ToArray();
        var left = 36d;
        var width = 936d / Math.Max(1, visibleColumns.Length);
        content.Append("0.04 0.17 0.29 rg 36 440 936 24 re f\n");
        for (var index = 0; index < visibleColumns.Length; index++)
        {
            PdfText(content, left + (width * index) + 4, 449, 5.6, Truncate(visibleColumns[index].ToUpperInvariant(), 22), true, "1 1 1");
        }

        var y = 420d;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            if (rowIndex % 2 == 0) content.Append($"0.95 0.98 1 rg 36 {y - 5:0} 936 19 re f\n");
            for (var column = 0; column < visibleColumns.Length; column++)
            {
                var value = column < rows[rowIndex].Count ? rows[rowIndex][column] : string.Empty;
                PdfText(content, left + (width * column) + 4, y, 5.6, Truncate(value, 28), false, "0.06 0.16 0.27");
            }
            y -= 19;
        }

        content.Append("0.68 0.77 0.84 RG 36 53 m 972 53 l S\n");
        PdfText(content, 36, 35, 7, $"Artifact type: {artifact.ArtifactKind} · Logo SHA-256 {ProjectFlowHiveBrandAssets.LogoSha256}", false, "0.34 0.42 0.50");
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
        objects[5] = StreamObject($"/Type /XObject /Subtype /Image /Width 222 /Height 148 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {logo.Length}", logo);
        for (var index = 0; index < pageContents.Count; index++)
        {
            var bytes = Ascii(pageContents[index]);
            objects[contentIds[index]] = StreamObject($"/Length {bytes.Length}", bytes);
            objects[pageIds[index]] = Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1008 612] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> /XObject << /Im1 5 0 R >> >> /Contents {contentIds[index]} 0 R >>");
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
            WriteAscii(output, offsets.TryGetValue(id, out var offset)
                ? $"{offset:0000000000} 00000 n \n"
                : "0000000000 00000 f \n");
        }
        WriteAscii(output, $"trailer\n<< /Size {maxId + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        return output.ToArray();
    }

    private static void PdfText(StringBuilder target, double x, double y, double size, string text, bool bold, string rgb)
    {
        target.Append($"BT /{(bold ? "F2" : "F1")} {size.ToString("0.##", CultureInfo.InvariantCulture)} Tf {rgb} rg {x.ToString("0.##", CultureInfo.InvariantCulture)} {y.ToString("0.##", CultureInfo.InvariantCulture)} Td ({EscapePdf(text)}) Tj ET\n");
    }

    private static byte[] StreamObject(string dictionary, byte[] data)
    {
        using var stream = new MemoryStream();
        WriteAscii(stream, $"<< {dictionary} >>\nstream\n");
        stream.Write(data);
        WriteAscii(stream, "\nendstream");
        return stream.ToArray();
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
    private static void WriteAscii(Stream stream, string value) => stream.Write(Ascii(value));
    private static string EscapePdf(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("\r", " ").Replace("\n", " ");
    private static string Truncate(string? value, int length) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim()[..Math.Min(value.Trim().Length, length)];
    private static string Join(string? left, string? right) => string.Join(" · ", new[] { left, right }.Where(value => !string.IsNullOrWhiteSpace(value)));
    private static string SafeSheetName(string value)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var safe = new string((value ?? "Artifact").Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return safe[..Math.Min(Math.Max(1, safe.Length), 31)];
    }
    private static string CONTROLLabel() => CONTROLLabelValue;
    private const string CONTROLLabelValue = CONTROLLabel;
}
