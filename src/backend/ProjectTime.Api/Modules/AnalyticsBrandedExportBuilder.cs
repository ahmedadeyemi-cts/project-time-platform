using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Creates official US Signal branded Analytics Center exports. PDF is generated
/// with the embedded US Signal image and standard PDF fonts; Excel uses the same
/// embedded logo, criteria/source worksheets, frozen headers, filters, and print
/// setup. No external rendering service or customer data transfer is required.
/// </summary>
internal static class AnalyticsBrandedExportBuilder
{
    private const string LogoPngResource = "ProjectTime.Api.Assets.Branding.USSNavyStacked.png";
    private const string LogoJpgResource = "ProjectTime.Api.Assets.Branding.USSNavyStacked.jpg";

    internal static AnalyticsBrandedExport Build(
        EnterpriseReportRunRecord run,
        string? requestedFormat)
    {
        var format = NormalizeFormat(requestedFormat);
        var payload = format switch
        {
            "pdf" => (BuildPdf(run), "application/pdf", $"{Safe(run.ReportCode)}-{run.StartedAt:yyyyMMdd-HHmmss}-ussignal.pdf"),
            "xlsx" => (BuildExcel(run), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{Safe(run.ReportCode)}-{run.StartedAt:yyyyMMdd-HHmmss}-ussignal.xlsx"),
            "csv" => (BuildCsv(run), "text/csv; charset=utf-8", $"{Safe(run.ReportCode)}-{run.StartedAt:yyyyMMdd-HHmmss}.csv"),
            _ => (BuildJson(run), "application/json", $"{Safe(run.ReportCode)}-{run.StartedAt:yyyyMMdd-HHmmss}.json")
        };
        return new(
            payload.Item1,
            payload.Item2,
            payload.Item3,
            format,
            Convert.ToHexString(SHA256.HashData(payload.Item1)).ToLowerInvariant());
    }

    internal static string NormalizeFormat(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "pdf" => "pdf",
            "csv" => "csv",
            "json" => "json",
            _ => "xlsx"
        };

    private static byte[] BuildCsv(EnterpriseReportRunRecord run)
    {
        var columns = Columns(run.Columns);
        var rows = Rows(run.Results);
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', columns.Select(column => Csv(column.Label))));
        foreach (var row in rows)
            builder.AppendLine(string.Join(',', columns.Select(column => Csv(Value(row, column.Key)))));
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static byte[] BuildJson(EnterpriseReportRunRecord run) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            brand = "US Signal",
            product = "ProjectPulse Analytics Center",
            run.RunId,
            run.ReportCode,
            run.ReportName,
            run.ResultStatus,
            run.RowCount,
            run.ScopeSnapshot,
            run.Filters,
            columns = run.Columns,
            sources = run.Sources,
            results = run.Results,
            run.StartedAt,
            run.CompletedAt
        }, new JsonSerializerOptions { WriteIndented = true });

    private static byte[] BuildExcel(EnterpriseReportRunRecord run)
    {
        var columns = Columns(run.Columns);
        var rows = Rows(run.Results);
        using var workbook = new XLWorkbook();
        var report = workbook.Worksheets.Add("Report");
        var columnCount = Math.Max(columns.Length, 4);

        var logo = Resource(LogoPngResource);
        if (logo.Length > 0)
        {
            using var logoStream = new MemoryStream(logo, writable: false);
            var picture = report.AddPicture(logoStream, "USSignalLogo");
            picture.MoveTo(report.Cell(1, 1));
            picture.Scale(0.35);
            report.Row(1).Height = 48;
        }

        report.Range(1, 3, 1, columnCount).Merge();
        report.Cell(1, 3).Value = "Analytics Center";
        report.Cell(1, 3).Style.Font.Bold = true;
        report.Cell(1, 3).Style.Font.FontSize = 20;
        report.Cell(1, 3).Style.Font.FontColor = XLColor.FromHtml("#0B2F52");
        report.Range(2, 3, 2, columnCount).Merge();
        report.Cell(2, 3).Value = run.ReportName;
        report.Cell(2, 3).Style.Font.Bold = true;
        report.Cell(2, 3).Style.Font.FontSize = 13;
        report.Cell(2, 3).Style.Font.FontColor = XLColor.FromHtml("#1B5F91");

        report.Cell(3, 1).Value = "Run ID";
        report.Cell(3, 2).Value = run.RunId.ToString();
        report.Cell(3, 3).Value = "Generated";
        report.Cell(3, 4).Value = run.CompletedAt.UtcDateTime;
        report.Cell(4, 1).Value = "Result status";
        report.Cell(4, 2).Value = run.ResultStatus;
        report.Cell(4, 3).Value = "Rows";
        report.Cell(4, 4).Value = run.RowCount;
        report.Cell(5, 1).Value = "ProjectPulse release scope";
        report.Cell(5, 2).Value = "Role-scoped at execution time";
        report.Range(3, 1, 5, 4).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        report.Range(3, 1, 5, 4).Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        report.Range(3, 1, 5, 4).Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F7FB");
        report.Range(3, 1, 5, 1).Style.Font.Bold = true;
        report.Range(3, 3, 5, 3).Style.Font.Bold = true;

        const int headerRow = 7;
        for (var index = 0; index < columns.Length; index++)
        {
            var cell = report.Cell(headerRow, index + 1);
            cell.Value = columns[index].Label;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0B2F52");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.WrapText = true;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
            {
                var cell = report.Cell(headerRow + rowIndex + 1, columnIndex + 1);
                cell.Value = Value(rows[rowIndex], columns[columnIndex].Key);
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                cell.Style.Alignment.WrapText = true;
                if (rowIndex % 2 == 1)
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F7FAFC");
            }
        }

        if (columns.Length > 0)
        {
            var lastRow = Math.Max(headerRow, headerRow + rows.Length);
            report.Range(headerRow, 1, lastRow, columns.Length).SetAutoFilter();
            report.Range(headerRow, 1, lastRow, columns.Length).Style.Border.InsideBorder = XLBorderStyleValues.Hair;
            report.SheetView.FreezeRows(headerRow);
            report.Columns(1, columns.Length).AdjustToContents(8, 44);
        }
        report.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        report.PageSetup.FitToPages(1, 0);
        report.PageSetup.Margins.Top = 0.4;
        report.PageSetup.Margins.Bottom = 0.4;
        report.PageSetup.Footer.Center.AddText("US Signal · ProjectPulse Analytics Center");
        report.PageSetup.Footer.Right.AddText("Page &[Page] of &[Pages]");

        var criteria = workbook.Worksheets.Add("Criteria");
        criteria.Cell(1, 1).Value = "US Signal Analytics Center — Effective Criteria";
        criteria.Cell(1, 1).Style.Font.Bold = true;
        criteria.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#0B2F52");
        criteria.Cell(2, 1).Value = "Criterion";
        criteria.Cell(2, 2).Value = "Value";
        criteria.Range(2, 1, 2, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#0B2F52");
        criteria.Range(2, 1, 2, 2).Style.Font.FontColor = XLColor.White;
        criteria.Range(2, 1, 2, 2).Style.Font.Bold = true;
        var criterionRow = 3;
        foreach (var property in ObjectProperties(run.Filters))
        {
            criteria.Cell(criterionRow, 1).Value = Humanize(property.Name);
            criteria.Cell(criterionRow, 2).Value = JsonText(property.Value);
            criterionRow++;
        }
        criteria.Columns(1, 2).AdjustToContents(12, 80);
        criteria.Column(2).Style.Alignment.WrapText = true;

        var sources = workbook.Worksheets.Add("Sources");
        sources.Cell(1, 1).Value = "US Signal Analytics Center — Source Evidence";
        sources.Cell(1, 1).Style.Font.Bold = true;
        sources.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#0B2F52");
        var sourceHeaders = new[] { "Source", "Status", "Required", "Records", "Observed", "Diagnostic", "Message" };
        for (var index = 0; index < sourceHeaders.Length; index++)
        {
            sources.Cell(2, index + 1).Value = sourceHeaders[index];
            sources.Cell(2, index + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#0B2F52");
            sources.Cell(2, index + 1).Style.Font.FontColor = XLColor.White;
            sources.Cell(2, index + 1).Style.Font.Bold = true;
        }
        var sourceRow = 3;
        foreach (var source in ArrayItems(run.Sources))
        {
            sources.Cell(sourceRow, 1).Value = JsonProperty(source, "name", "Name");
            sources.Cell(sourceRow, 2).Value = JsonProperty(source, "status", "Status");
            sources.Cell(sourceRow, 3).Value = JsonProperty(source, "required", "Required");
            sources.Cell(sourceRow, 4).Value = JsonProperty(source, "recordCount", "RecordCount");
            sources.Cell(sourceRow, 5).Value = JsonProperty(source, "observedAt", "ObservedAt");
            sources.Cell(sourceRow, 6).Value = JsonProperty(source, "diagnosticCode", "DiagnosticCode");
            sources.Cell(sourceRow, 7).Value = JsonProperty(source, "message", "Message");
            sourceRow++;
        }
        sources.Columns(1, sourceHeaders.Length).AdjustToContents(10, 60);
        sources.Columns(1, sourceHeaders.Length).Style.Alignment.WrapText = true;
        sources.SheetView.FreezeRows(2);

        workbook.Properties.Title = run.ReportName;
        workbook.Properties.Subject = "ProjectPulse Analytics Center";
        workbook.Properties.Company = "US Signal";
        workbook.Properties.Author = "ProjectPulse";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] BuildPdf(EnterpriseReportRunRecord run)
    {
        var columns = Columns(run.Columns);
        var rows = Rows(run.Results);
        var logo = Resource(LogoJpgResource);
        var image = JpegDimensions(logo);
        var document = new PdfDocumentBuilder();
        var catalogId = document.ReserveObject();
        var pagesId = document.ReserveObject();
        var regularFontId = document.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var boldFontId = document.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>");
        var imageId = 0;
        if (logo.Length > 0 && image.Width > 0 && image.Height > 0)
        {
            imageId = document.AddStream(
                $"/Type /XObject /Subtype /Image /Width {image.Width} /Height {image.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode",
                logo);
        }

        const int rowsPerPage = 28;
        var pageCount = Math.Max(1, (int)Math.Ceiling(rows.Length / (double)rowsPerPage));
        var pageIds = new List<int>();
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var pageRows = rows.Skip(pageIndex * rowsPerPage).Take(rowsPerPage).ToArray();
            var content = PdfPageContent(
                run,
                columns,
                pageRows,
                pageIndex + 1,
                pageCount,
                imageId > 0,
                image);
            var contentId = document.AddStream(string.Empty, Encoding.ASCII.GetBytes(content));
            var pageId = document.AddObject(
                $"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 792 612] " +
                $"/Resources << /Font << /F1 {regularFontId} 0 R /F2 {boldFontId} 0 R >> " +
                (imageId > 0 ? $"/XObject << /Logo {imageId} 0 R >> " : string.Empty) +
                $">> /Contents {contentId} 0 R >>");
            pageIds.Add(pageId);
        }

        document.SetObject(
            pagesId,
            $"<< /Type /Pages /Kids [{string.Join(' ', pageIds.Select(id => $"{id} 0 R"))}] /Count {pageIds.Count} >>");
        document.SetObject(catalogId, $"<< /Type /Catalog /Pages {pagesId} 0 R >>");
        return document.Build(catalogId);
    }

    private static string PdfPageContent(
        EnterpriseReportRunRecord run,
        ExportColumn[] columns,
        JsonElement[] rows,
        int page,
        int pages,
        bool hasLogo,
        (int Width, int Height) image)
    {
        var builder = new StringBuilder();
        builder.AppendLine("q 0.043 0.184 0.322 rg 0 548 792 64 re f Q");
        if (hasLogo)
        {
            var targetHeight = 42d;
            var targetWidth = Math.Min(122d, targetHeight * image.Width / Math.Max(1d, image.Height));
            builder.AppendLine(FormattableString.Invariant(
                $"q {targetWidth:0.##} 0 0 {targetHeight:0.##} 28 558 cm /Logo Do Q"));
        }
        builder.AppendLine(PdfText(172, 585, "F2", 19, "Analytics Center", 1, 1, 1));
        builder.AppendLine(PdfText(172, 565, "F1", 10, run.ReportName, 0.82, 0.9, 0.97));
        builder.AppendLine(PdfText(650, 585, "F2", 9, $"Page {page} of {pages}", 1, 1, 1));
        builder.AppendLine(PdfText(650, 568, "F1", 8, run.CompletedAt.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture), 0.82, 0.9, 0.97));

        builder.AppendLine("q 0.953 0.969 0.984 rg 28 505 736 34 re f Q");
        builder.AppendLine(PdfText(38, 526, "F2", 8.5, $"Result: {Humanize(run.ResultStatus)}   Rows: {run.RowCount}   Run ID: {run.RunId}", 0.067, 0.137, 0.243));
        var criteriaSummary = string.Join("  |  ", ObjectProperties(run.Filters)
            .Where(property => !property.Name.Equals("limit", StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .Select(property => $"{Humanize(property.Name)}: {Truncate(JsonText(property.Value), 32)}"));
        builder.AppendLine(PdfText(38, 512, "F1", 7.2, criteriaSummary.Length == 0 ? "Criteria: role-scoped defaults" : criteriaSummary, 0.25, 0.33, 0.43));

        var visibleColumns = columns.Length == 0
            ? [new ExportColumn("status", "Status")]
            : columns;
        const double left = 28;
        const double right = 764;
        const double headerY = 480;
        const double rowHeight = 14;
        var width = right - left;
        var columnWidth = width / visibleColumns.Length;
        var fontSize = Math.Clamp(7.5 - Math.Max(0, visibleColumns.Length - 8) * 0.25, 4.8, 7.5);

        builder.AppendLine(FormattableString.Invariant($"q 0.043 0.184 0.322 rg {left:0.##} {headerY:0.##} {width:0.##} {rowHeight:0.##} re f Q"));
        for (var index = 0; index < visibleColumns.Length; index++)
        {
            var x = left + index * columnWidth + 3;
            builder.AppendLine(PdfText(x, headerY + 4, "F2", fontSize, Truncate(visibleColumns[index].Label, MaxCharacters(columnWidth, fontSize)), 1, 1, 1));
        }

        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var y = headerY - (rowIndex + 1) * rowHeight;
            if (rowIndex % 2 == 1)
                builder.AppendLine(FormattableString.Invariant($"q 0.965 0.976 0.988 rg {left:0.##} {y:0.##} {width:0.##} {rowHeight:0.##} re f Q"));
            builder.AppendLine(FormattableString.Invariant($"q 0.82 0.87 0.91 RG 0.25 w {left:0.##} {y:0.##} {width:0.##} {rowHeight:0.##} re S Q"));
            for (var columnIndex = 0; columnIndex < visibleColumns.Length; columnIndex++)
            {
                var x = left + columnIndex * columnWidth + 3;
                var value = Value(rows[rowIndex], visibleColumns[columnIndex].Key);
                builder.AppendLine(PdfText(x, y + 4, "F1", fontSize, Truncate(value, MaxCharacters(columnWidth, fontSize)), 0.09, 0.14, 0.22));
            }
        }

        builder.AppendLine("q 0.043 0.184 0.322 RG 0.8 w 28 28 m 764 28 l S Q");
        builder.AppendLine(PdfText(28, 15, "F1", 7.5, "US Signal · ProjectPulse Analytics Center · Role-scoped at execution time", 0.24, 0.34, 0.45));
        builder.AppendLine(PdfText(700, 15, "F1", 7.5, $"{page}/{pages}", 0.24, 0.34, 0.45));
        return builder.ToString();
    }

    private static string PdfText(
        double x,
        double y,
        string font,
        double size,
        string text,
        double red,
        double green,
        double blue) => FormattableString.Invariant(
            $"BT {red:0.###} {green:0.###} {blue:0.###} rg /{font} {size:0.##} Tf 1 0 0 1 {x:0.##} {y:0.##} Tm ({EscapePdf(Ascii(text))}) Tj ET");

    private static int MaxCharacters(double width, double fontSize) =>
        Math.Max(3, (int)Math.Floor(width / Math.Max(2.5, fontSize * 0.52)));

    private static string EscapePdf(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("(", "\\(", StringComparison.Ordinal)
        .Replace(")", "\\)", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);

    private static string Ascii(string value) => new(
        (value ?? string.Empty)
            .Select(character => character is >= ' ' and <= '~' ? character : ' ')
            .ToArray());

    private static byte[] Resource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream is null) return Array.Empty<byte>();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static (int Width, int Height) JpegDimensions(byte[] data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) return (0, 0);
        var index = 2;
        while (index + 8 < data.Length)
        {
            if (data[index] != 0xFF) { index++; continue; }
            var marker = data[index + 1];
            index += 2;
            if (marker is 0xD8 or 0xD9) continue;
            if (index + 2 > data.Length) break;
            var length = data[index] * 256 + data[index + 1];
            if (length < 2 || index + length > data.Length) break;
            if (marker is >= 0xC0 and <= 0xC3 or >= 0xC5 and <= 0xC7 or >= 0xC9 and <= 0xCB or >= 0xCD and <= 0xCF)
            {
                if (index + 7 >= data.Length) break;
                var height = data[index + 3] * 256 + data[index + 4];
                var width = data[index + 5] * 256 + data[index + 6];
                return (width, height);
            }
            index += length;
        }
        return (0, 0);
    }

    private static ExportColumn[] Columns(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array) return Array.Empty<ExportColumn>();
        return element.EnumerateArray()
            .Select(item => new ExportColumn(
                JsonProperty(item, "key", "Key"),
                JsonProperty(item, "label", "Label")))
            .Where(column => column.Key.Length > 0)
            .ToArray();
    }

    private static JsonElement[] Rows(JsonElement element) =>
        ArrayItems(element).ToArray();

    private static IEnumerable<JsonElement> ArrayItems(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().Select(item => item.Clone())
            : Array.Empty<JsonElement>();

    private static IEnumerable<JsonProperty> ObjectProperties(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
            ? element.EnumerateObject().ToArray()
            : Array.Empty<JsonProperty>();

    private static string Value(JsonElement row, string key)
    {
        if (row.ValueKind != JsonValueKind.Object) return string.Empty;
        foreach (var property in row.EnumerateObject())
            if (property.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                return JsonText(property.Value);
        return string.Empty;
    }

    private static string JsonProperty(JsonElement row, params string[] names)
    {
        if (row.ValueKind != JsonValueKind.Object) return string.Empty;
        foreach (var property in row.EnumerateObject())
            if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                return JsonText(property.Value);
        return string.Empty;
    }

    private static string JsonText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(JsonText)),
        JsonValueKind.Object => string.Join("; ", value.EnumerateObject().Select(property => $"{Humanize(property.Name)}: {JsonText(property.Value)}")),
        JsonValueKind.True => "Yes",
        JsonValueKind.False => "No",
        _ => value.ToString()
    };

    private static string Humanize(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value ?? string.Empty)
        {
            if (character == '_' || character == '-') builder.Append(' ');
            else if (char.IsUpper(character) && builder.Length > 0) builder.Append(' ').Append(character);
            else builder.Append(character);
        }
        var text = builder.ToString().Trim();
        return text.Length == 0
            ? string.Empty
            : char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string Csv(string value)
    {
        var safe = (value ?? string.Empty).Replace('\0', ' ');
        return safe.Contains(',') || safe.Contains('"') || safe.Contains('\n')
            ? $"\"{safe.Replace("\"", "\"\"")}\""
            : safe;
    }

    private static string Truncate(string value, int maximum)
    {
        var clean = (value ?? string.Empty).Replace('\0', ' ').Trim();
        if (clean.Length <= maximum) return clean;
        return maximum <= 3 ? clean[..maximum] : clean[..(maximum - 3)] + "...";
    }

    private static string Safe(string value)
    {
        var safe = new string((value ?? "analytics-report").ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray()).Trim('-');
        return safe.Length == 0 ? "analytics-report" : safe[..Math.Min(80, safe.Length)];
    }

    private sealed record ExportColumn(string Key, string Label);

    private sealed class PdfDocumentBuilder
    {
        private readonly List<byte[]?> _objects = [];

        internal int ReserveObject()
        {
            _objects.Add(null);
            return _objects.Count;
        }

        internal int AddObject(string value)
        {
            _objects.Add(Encoding.ASCII.GetBytes(value));
            return _objects.Count;
        }

        internal int AddStream(string dictionary, byte[] content)
        {
            var prefix = Encoding.ASCII.GetBytes($"<< {dictionary} /Length {content.Length} >>\nstream\n");
            var suffix = Encoding.ASCII.GetBytes("\nendstream");
            var bytes = new byte[prefix.Length + content.Length + suffix.Length];
            Buffer.BlockCopy(prefix, 0, bytes, 0, prefix.Length);
            Buffer.BlockCopy(content, 0, bytes, prefix.Length, content.Length);
            Buffer.BlockCopy(suffix, 0, bytes, prefix.Length + content.Length, suffix.Length);
            _objects.Add(bytes);
            return _objects.Count;
        }

        internal void SetObject(int id, string value) =>
            _objects[id - 1] = Encoding.ASCII.GetBytes(value);

        internal byte[] Build(int catalogId)
        {
            using var stream = new MemoryStream();
            var header = Encoding.ASCII.GetBytes("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
            stream.Write(header);
            var offsets = new List<long> { 0 };
            for (var index = 0; index < _objects.Count; index++)
            {
                offsets.Add(stream.Position);
                var prefix = Encoding.ASCII.GetBytes($"{index + 1} 0 obj\n");
                stream.Write(prefix);
                stream.Write(_objects[index] ?? Encoding.ASCII.GetBytes("<< >>"));
                stream.Write(Encoding.ASCII.GetBytes("\nendobj\n"));
            }
            var xref = stream.Position;
            stream.Write(Encoding.ASCII.GetBytes($"xref\n0 {_objects.Count + 1}\n"));
            stream.Write(Encoding.ASCII.GetBytes("0000000000 65535 f \n"));
            foreach (var offset in offsets.Skip(1))
                stream.Write(Encoding.ASCII.GetBytes($"{offset:0000000000} 00000 n \n"));
            stream.Write(Encoding.ASCII.GetBytes(
                $"trailer\n<< /Size {_objects.Count + 1} /Root {catalogId} 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
            return stream.ToArray();
        }
    }
}
