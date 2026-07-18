using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace ProjectTime.Api.Modules;

internal sealed record BrandedInvoiceDocument(
    string InvoiceNumber,
    string InvoiceType,
    string InvoiceStatus,
    DateOnly? InvoiceDate,
    DateOnly? BillingPeriodStart,
    DateOnly? BillingPeriodEnd,
    string CustomerName,
    string ProjectCode,
    string ProjectName,
    string ContractType,
    string ProjectManager,
    string ProjectCoordinator,
    string PurchaseOrderNumber,
    decimal? PurchaseOrderAmount,
    string CertiniaId,
    string SalesforceId,
    string SellQuote,
    decimal SubtotalAmount,
    decimal AdjustmentAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    string Notes,
    string ImmutableSnapshotSha256,
    string PersonalNamesSummary,
    IReadOnlyList<BrandedInvoiceLine> Lines);

internal sealed record BrandedInvoiceLine(
    int LineNumber,
    DateOnly? WorkDate,
    string Resource,
    string TaskCode,
    string TaskName,
    string Description,
    string TimeType,
    string LaborCategory,
    decimal Hours,
    string RateCode,
    string RateDescription,
    decimal UnitRate,
    decimal Amount);

internal static class BrandedInvoiceArtifactRenderer
{
    private const double PdfPageWidth = 612d;
    private const double PdfPageHeight = 792d;
    private const double PdfLeft = 30d;
    private const double PdfRight = 582d;
    private const double PdfBottomLimit = 742d;

    public static byte[] BuildPdf(BrandedInvoiceDocument invoice)
    {
        var pages = new List<StringBuilder>();
        var page = NewPdfPage();
        pages.Add(page);
        DrawFirstPageHeader(page, invoice);

        var cursor = 293d;
        DrawPdfTableHeader(page, cursor);
        cursor += 25d;

        for (var index = 0; index < invoice.Lines.Count; index++)
        {
            var line = invoice.Lines[index];
            var resourceLines = WrapText(line.Resource, 20);
            var taskTitle = string.Join(" ", new[] { line.TaskCode, line.TaskName }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            var taskLines = WrapText(taskTitle, 46);
            var descriptionLines = WrapText(line.Description, 61);
            var workHeight = Math.Max(11d, taskLines.Count * 10d + descriptionLines.Count * 8.5d + 4d);
            var rowHeight = Math.Max(38d, Math.Max(resourceLines.Count * 9d + 12d, workHeight + 10d));

            if (cursor + rowHeight > PdfBottomLimit)
            {
                page = NewPdfPage();
                pages.Add(page);
                DrawContinuationHeader(page, invoice);
                cursor = 84d;
                DrawPdfTableHeader(page, cursor);
                cursor += 25d;
            }

            DrawPdfLineRow(page, invoice, line, index, cursor, rowHeight, resourceLines, taskLines, descriptionLines);
            cursor += rowHeight;
        }

        var totalsHeight = 138d;
        if (cursor + totalsHeight > PdfBottomLimit)
        {
            page = NewPdfPage();
            pages.Add(page);
            DrawContinuationHeader(page, invoice);
            cursor = 96d;
        }

        DrawPdfTotals(page, invoice, cursor + 12d);

        for (var index = 0; index < pages.Count; index++)
        {
            DrawPdfFooter(pages[index], invoice, index + 1, pages.Count);
        }

        return BuildPdfDocument(pages);
    }

    public static byte[] BuildExcel(BrandedInvoiceDocument invoice)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddXlsxEntry(archive, "[Content_Types].xml", XlsxContentTypes());
            AddXlsxEntry(archive, "_rels/.rels", XlsxRootRelationships());
            AddXlsxEntry(archive, "docProps/core.xml", XlsxCoreProperties(invoice));
            AddXlsxEntry(archive, "docProps/app.xml", XlsxAppProperties());
            AddXlsxEntry(archive, "xl/workbook.xml", XlsxWorkbook(invoice));
            AddXlsxEntry(archive, "xl/_rels/workbook.xml.rels", XlsxWorkbookRelationships());
            AddXlsxEntry(archive, "xl/styles.xml", XlsxStyles());
            AddXlsxEntry(archive, "xl/worksheets/sheet1.xml", XlsxInvoiceWorksheet(invoice));
            AddXlsxEntry(archive, "xl/worksheets/sheet2.xml", XlsxDetailWorksheet(invoice));
            AddXlsxEntry(archive, "xl/worksheets/sheet3.xml", XlsxAuditWorksheet(invoice));
        }
        return output.ToArray();
    }

    private static StringBuilder NewPdfPage()
    {
        var page = new StringBuilder();
        page.Append("1 1 1 rg\n0 0 612 792 re f\n");
        return page;
    }

    private static void DrawFirstPageHeader(StringBuilder page, BrandedInvoiceDocument invoice)
    {
        DrawBrandMark(page, 43d, 42d);
        PdfText(page, 64d, 27d, 15d, "US SIGNAL", bold: true, color: "0.05 0.17 0.31");
        PdfText(page, 64d, 46d, 8.5d, "Professional Services", color: "0.38 0.47 0.58");
        PdfTextRight(page, PdfRight, 26d, 16d, "US Signal SP Invoice", bold: true, color: "0.05 0.11 0.20");
        PdfTextRight(page, PdfRight, 49d, 9.5d, invoice.InvoiceNumber, color: "0.35 0.44 0.56");
        PdfLine(page, PdfLeft, 84d, PdfRight, 84d, "0.00 0.48 0.73", 2d);

        PdfCard(page, 30d, 98d, 168d, 58d, "CUSTOMER", invoice.CustomerName,
            "Work Register");
        PdfCard(page, 204d, 98d, 250d, 58d, "PROJECT",
            $"{invoice.ProjectCode} - {invoice.ProjectName}", invoice.ContractType);
        PdfCard(page, 460d, 98d, 122d, 58d, "INVOICE",
            invoice.InvoiceNumber, NormalizeTitle(invoice.InvoiceType));

        PdfCard(page, 30d, 163d, 168d, 58d, "BILLING PERIOD",
            $"{FormatDate(invoice.BillingPeriodStart)} through {FormatDate(invoice.BillingPeriodEnd)}",
            $"Invoice date: {FormatDate(invoice.InvoiceDate)}");
        PdfCard(page, 204d, 163d, 250d, 58d, "OWNERSHIP",
            invoice.ProjectManager, $"PTC: {invoice.ProjectCoordinator}");
        PdfCard(page, 460d, 163d, 122d, 58d, "PURCHASE ORDER",
            Fallback(invoice.PurchaseOrderNumber, "Not configured"),
            invoice.PurchaseOrderAmount is decimal amount ? Money(amount) : "");

        PdfFillRect(page, 30d, 228d, 552d, 52d, "0.94 0.98 1.00");
        PdfStrokeRect(page, 30d, 228d, 552d, 52d, "0.67 0.84 0.93", 0.8d);
        PdfLabelValue(page, 42d, 240d, "CERTINIA ID", Fallback(invoice.CertiniaId, "Not configured"), 150d);
        PdfLabelValue(page, 218d, 240d, "SELL QUOTE", Fallback(invoice.SellQuote, "Not configured"), 150d);
        PdfLabelValue(page, 394d, 240d, "SALESFORCE ID", Fallback(invoice.SalesforceId, "Not configured"), 168d);
    }

    private static void DrawContinuationHeader(StringBuilder page, BrandedInvoiceDocument invoice)
    {
        DrawBrandMark(page, 42d, 35d);
        PdfText(page, 62d, 22d, 12d, "US SIGNAL", bold: true, color: "0.05 0.17 0.31");
        PdfText(page, 62d, 39d, 8d, "US Signal SP Invoice", color: "0.38 0.47 0.58");
        PdfTextRight(page, PdfRight, 23d, 11d, invoice.InvoiceNumber, bold: true, color: "0.05 0.11 0.20");
        PdfTextRight(page, PdfRight, 41d, 8d, $"{invoice.CustomerName} - {invoice.ProjectCode}", color: "0.38 0.47 0.58");
        PdfLine(page, PdfLeft, 66d, PdfRight, 66d, "0.00 0.48 0.73", 1.5d);
    }

    private static void DrawPdfTableHeader(StringBuilder page, double top)
    {
        var x = new[] { 30d, 92d, 187d, 417d, 462d, 522d, 582d };
        PdfFillRect(page, x[0], top, x[^1] - x[0], 25d, "0.05 0.17 0.31");
        var labels = new[] { "DATE", "RESOURCE", "WORK PERFORMED", "HOURS", "RATE", "AMOUNT" };
        for (var index = 0; index < labels.Length; index++)
        {
            PdfText(page, x[index] + 5d, top + 7d, 7.5d, labels[index], bold: true, color: "1 1 1");
        }
    }

    private static void DrawPdfLineRow(
        StringBuilder page,
        BrandedInvoiceDocument invoice,
        BrandedInvoiceLine line,
        int index,
        double top,
        double height,
        IReadOnlyList<string> resourceLines,
        IReadOnlyList<string> taskLines,
        IReadOnlyList<string> descriptionLines)
    {
        var x = new[] { 30d, 92d, 187d, 417d, 462d, 522d, 582d };
        var fill = index % 2 == 0 ? "0.98 0.99 1.00" : "0.92 0.97 0.99";
        PdfFillRect(page, x[0], top, x[^1] - x[0], height, fill);
        PdfStrokeRect(page, x[0], top, x[^1] - x[0], height, "0.78 0.84 0.89", 0.45d);
        for (var column = 1; column < x.Length - 1; column++)
        {
            PdfLine(page, x[column], top, x[column], top + height, "0.82 0.87 0.91", 0.35d);
        }

        PdfText(page, x[0] + 5d, top + 9d, 8d, FormatDate(line.WorkDate), color: "0.14 0.20 0.29");
        PdfMultiline(page, x[1] + 5d, top + 8d, 8d, resourceLines, 9d, false, "0.14 0.20 0.29");
        PdfMultiline(page, x[2] + 5d, top + 7d, 8.3d, taskLines, 10d, true, "0.08 0.15 0.25");
        var descriptionTop = top + 9d + taskLines.Count * 10d;
        PdfMultiline(page, x[2] + 5d, descriptionTop, 7.3d, descriptionLines, 8.5d, false, "0.31 0.39 0.49");

        PdfTextRight(page, x[4] - 5d, top + 9d, 8d, line.Hours.ToString("0.00", CultureInfo.InvariantCulture), color: "0.14 0.20 0.29");
        PdfTextRight(page, x[5] - 5d, top + 9d, 8d, Money(line.UnitRate), color: "0.14 0.20 0.29");
        PdfTextRight(page, x[6] - 5d, top + 9d, 8d, Money(line.Amount), bold: true, color: "0.08 0.15 0.25");
    }

    private static void DrawPdfTotals(StringBuilder page, BrandedInvoiceDocument invoice, double top)
    {
        PdfFillRect(page, 342d, top, 240d, 102d, "0.95 0.98 1.00");
        PdfStrokeRect(page, 342d, top, 240d, 102d, "0.65 0.79 0.88", 0.8d);
        PdfTotalLine(page, top + 10d, "Subtotal", invoice.SubtotalAmount, false);
        PdfTotalLine(page, top + 31d, "Adjustments", invoice.AdjustmentAmount, false);
        PdfTotalLine(page, top + 52d, "Tax", invoice.TaxAmount, false);
        PdfFillRect(page, 342d, top + 73d, 240d, 29d, "0.05 0.17 0.31");
        PdfText(page, 354d, top + 81d, 10d, "Invoice total", bold: true, color: "1 1 1");
        PdfTextRight(page, 570d, top + 80d, 11d, Money(invoice.TotalAmount), bold: true, color: "1 1 1");

        PdfText(page, 30d, top + 7d, 8d, "INVOICE NOTES", bold: true, color: "0.38 0.47 0.58");
        PdfMultiline(page, 30d, top + 23d, 8d, WrapText(Fallback(invoice.Notes, "No invoice notes."), 58), 10d, false, "0.14 0.20 0.29");
        PdfText(page, 30d, top + 82d, 7.2d,
            "Generated from the immutable ProjectPulse invoice snapshot.", color: "0.42 0.49 0.58");
        PdfText(page, 30d, top + 94d, 7.2d, invoice.PersonalNamesSummary, color: "0.42 0.49 0.58");
    }

    private static void DrawPdfFooter(StringBuilder page, BrandedInvoiceDocument invoice, int pageNumber, int pageCount)
    {
        PdfLine(page, PdfLeft, 758d, PdfRight, 758d, "0.82 0.87 0.91", 0.45d);
        PdfText(page, PdfLeft, 764d, 7d, $"US Signal SP Invoice {invoice.InvoiceNumber}", color: "0.44 0.51 0.60");
        PdfTextRight(page, PdfRight, 764d, 7d, $"Page {pageNumber} of {pageCount}", color: "0.44 0.51 0.60");
    }

    private static void PdfCard(StringBuilder page, double x, double top, double width, double height, string label, string value, string detail)
    {
        PdfFillRect(page, x, top, width, height, "0.97 0.98 0.99");
        PdfStrokeRect(page, x, top, width, height, "0.82 0.87 0.91", 0.55d);
        PdfText(page, x + 8d, top + 8d, 7d, label, bold: true, color: "0.38 0.47 0.58");
        var valueLines = WrapText(value, Math.Max(14, (int)(width / 5.7d)));
        PdfMultiline(page, x + 8d, top + 21d, 8.5d, valueLines.Take(2).ToArray(), 10d, true, "0.08 0.15 0.25");
        if (!string.IsNullOrWhiteSpace(detail))
        {
            PdfText(page, x + 8d, top + height - 14d, 7.2d, detail, color: "0.42 0.49 0.58");
        }
    }

    private static void PdfLabelValue(StringBuilder page, double x, double top, string label, string value, double width)
    {
        PdfText(page, x, top, 7d, label, bold: true, color: "0.38 0.47 0.58");
        PdfMultiline(page, x, top + 13d, 8.2d, WrapText(value, Math.Max(16, (int)(width / 5.7d))).Take(2).ToArray(), 9d, true, "0.08 0.15 0.25");
    }

    private static void PdfTotalLine(StringBuilder page, double top, string label, decimal value, bool bold)
    {
        PdfText(page, 354d, top, 8.5d, label, bold, "0.20 0.28 0.38");
        PdfTextRight(page, 570d, top, 8.5d, Money(value), bold, "0.08 0.15 0.25");
    }

    private static void DrawBrandMark(StringBuilder page, double centerX, double centerTop)
    {
        var color = "0.05 0.17 0.31";
        foreach (var angle in new[] { 0d, 45d, 90d, 135d })
        {
            var radians = angle * Math.PI / 180d;
            var dx = Math.Cos(radians) * 10d;
            var dy = Math.Sin(radians) * 10d;
            PdfLine(page, centerX - dx, centerTop - dy, centerX + dx, centerTop + dy, color, 2.4d);
        }
        PdfFillCircle(page, centerX, centerTop, 2.7d, color);
    }

    private static void PdfFillCircle(StringBuilder page, double centerX, double centerTop, double radius, string color)
    {
        var y = PdfPageHeight - centerTop;
        var k = 0.5522847498d * radius;
        page.Append(color).Append(" rg\n")
            .Append(PdfNumber(centerX + radius)).Append(' ').Append(PdfNumber(y)).Append(" m\n")
            .Append(PdfNumber(centerX + radius)).Append(' ').Append(PdfNumber(y + k)).Append(' ')
            .Append(PdfNumber(centerX + k)).Append(' ').Append(PdfNumber(y + radius)).Append(' ')
            .Append(PdfNumber(centerX)).Append(' ').Append(PdfNumber(y + radius)).Append(" c\n")
            .Append(PdfNumber(centerX - k)).Append(' ').Append(PdfNumber(y + radius)).Append(' ')
            .Append(PdfNumber(centerX - radius)).Append(' ').Append(PdfNumber(y + k)).Append(' ')
            .Append(PdfNumber(centerX - radius)).Append(' ').Append(PdfNumber(y)).Append(" c\n")
            .Append(PdfNumber(centerX - radius)).Append(' ').Append(PdfNumber(y - k)).Append(' ')
            .Append(PdfNumber(centerX - k)).Append(' ').Append(PdfNumber(y - radius)).Append(' ')
            .Append(PdfNumber(centerX)).Append(' ').Append(PdfNumber(y - radius)).Append(" c\n")
            .Append(PdfNumber(centerX + k)).Append(' ').Append(PdfNumber(y - radius)).Append(' ')
            .Append(PdfNumber(centerX + radius)).Append(' ').Append(PdfNumber(y - k)).Append(' ')
            .Append(PdfNumber(centerX + radius)).Append(' ').Append(PdfNumber(y)).Append(" c f\n");
    }

    private static void PdfFillRect(StringBuilder page, double x, double top, double width, double height, string color)
    {
        page.Append(color).Append(" rg\n")
            .Append(PdfNumber(x)).Append(' ').Append(PdfNumber(PdfPageHeight - top - height)).Append(' ')
            .Append(PdfNumber(width)).Append(' ').Append(PdfNumber(height)).Append(" re f\n");
    }

    private static void PdfStrokeRect(StringBuilder page, double x, double top, double width, double height, string color, double lineWidth)
    {
        page.Append(color).Append(" RG\n")
            .Append(PdfNumber(lineWidth)).Append(" w\n")
            .Append(PdfNumber(x)).Append(' ').Append(PdfNumber(PdfPageHeight - top - height)).Append(' ')
            .Append(PdfNumber(width)).Append(' ').Append(PdfNumber(height)).Append(" re S\n");
    }

    private static void PdfLine(StringBuilder page, double x1, double top1, double x2, double top2, string color, double lineWidth)
    {
        page.Append(color).Append(" RG\n")
            .Append(PdfNumber(lineWidth)).Append(" w\n")
            .Append(PdfNumber(x1)).Append(' ').Append(PdfNumber(PdfPageHeight - top1)).Append(" m\n")
            .Append(PdfNumber(x2)).Append(' ').Append(PdfNumber(PdfPageHeight - top2)).Append(" l S\n");
    }

    private static void PdfText(StringBuilder page, double x, double top, double size, string? value, bool bold = false, string color = "0 0 0")
    {
        var safe = PdfTextSafe(value);
        page.Append("BT\n")
            .Append(color).Append(" rg\n/").Append(bold ? "F2" : "F1").Append(' ')
            .Append(PdfNumber(size)).Append(" Tf\n")
            .Append(PdfNumber(x)).Append(' ').Append(PdfNumber(PdfPageHeight - top - size)).Append(" Td\n(")
            .Append(PdfEscape(safe)).Append(") Tj\nET\n");
    }

    private static void PdfTextRight(StringBuilder page, double right, double top, double size, string? value, bool bold = false, string color = "0 0 0")
    {
        var safe = PdfTextSafe(value);
        var width = EstimatePdfTextWidth(safe, size, bold);
        PdfText(page, Math.Max(PdfLeft, right - width), top, size, safe, bold, color);
    }

    private static void PdfMultiline(StringBuilder page, double x, double top, double size, IReadOnlyList<string> lines, double leading, bool bold, string color)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            PdfText(page, x, top + index * leading, size, lines[index], bold, color);
        }
    }

    private static byte[] BuildPdfDocument(IReadOnlyList<StringBuilder> pages)
    {
        var regularFontObject = 3 + pages.Count * 2;
        var boldFontObject = regularFontObject + 1;
        var objects = new List<string> { string.Empty, string.Empty };
        var kids = new List<string>();

        for (var index = 0; index < pages.Count; index++)
        {
            var pageObject = 3 + index * 2;
            var contentObject = pageObject + 1;
            kids.Add($"{pageObject} 0 R");
            var stream = pages[index].ToString();
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {regularFontObject} 0 R /F2 {boldFontObject} 0 R >> >> /Contents {contentObject} 0 R >>");
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}endstream");
        }

        objects[0] = "<< /Type /Catalog /Pages 2 0 R >>";
        objects[1] = $"<< /Type /Pages /Kids [{string.Join(' ', kids)}] /Count {pages.Count} >>";
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>");

        using var output = new MemoryStream();
        WriteAscii(output, "%PDF-1.4\n%USSignalProjectPulse\n");
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(output.Position);
            WriteAscii(output, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xref = output.Position;
        WriteAscii(output, $"xref\n0 {objects.Count + 1}\n");
        WriteAscii(output, "0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            WriteAscii(output, $"{offset:0000000000} 00000 n \n");
        }
        WriteAscii(output, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    private static string XlsxContentTypes() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
          <Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/worksheets/sheet3.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
        </Types>
        """;

    private static string XlsxRootRelationships() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
          <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
        </Relationships>
        """;

    private static string XlsxCoreProperties(BrandedInvoiceDocument invoice)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:dcmitype="http://purl.org/dc/dcmitype/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <dc:title>US Signal SP Invoice {XlsxXml(invoice.InvoiceNumber)}</dc:title>
              <dc:subject>Immutable professional-services invoice</dc:subject>
              <dc:creator>US Signal ProjectPulse</dc:creator>
              <cp:lastModifiedBy>US Signal ProjectPulse</cp:lastModifiedBy>
              <dcterms:created xsi:type="dcterms:W3CDTF">{now}</dcterms:created>
              <dcterms:modified xsi:type="dcterms:W3CDTF">{now}</dcterms:modified>
            </cp:coreProperties>
            """;
    }

    private static string XlsxAppProperties() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
          <Application>US Signal ProjectPulse</Application>
          <DocSecurity>0</DocSecurity>
          <ScaleCrop>false</ScaleCrop>
          <HeadingPairs><vt:vector size="2" baseType="variant"><vt:variant><vt:lpstr>Worksheets</vt:lpstr></vt:variant><vt:variant><vt:i4>3</vt:i4></vt:variant></vt:vector></HeadingPairs>
          <TitlesOfParts><vt:vector size="3" baseType="lpstr"><vt:lpstr>US Signal SP Invoice</vt:lpstr><vt:lpstr>Invoice Detail</vt:lpstr><vt:lpstr>Audit Metadata</vt:lpstr></vt:vector></TitlesOfParts>
          <Company>US Signal</Company>
          <AppVersion>1.0</AppVersion>
        </Properties>
        """;

    private static string XlsxWorkbook(BrandedInvoiceDocument invoice)
    {
        var invoiceEnd = 18 + Math.Max(1, invoice.Lines.Count) + 10;
        var detailEnd = 1 + Math.Max(1, invoice.Lines.Count) + 6;
        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <bookViews><workbookView activeTab="0"/></bookViews>
              <sheets>
                <sheet name="US Signal SP Invoice" sheetId="1" r:id="rId1"/>
                <sheet name="Invoice Detail" sheetId="2" r:id="rId2"/>
                <sheet name="Audit Metadata" sheetId="3" r:id="rId3"/>
              </sheets>
              <definedNames>
                <definedName name="_xlnm.Print_Area" localSheetId="0">'US Signal SP Invoice'!$A$1:$K${invoiceEnd}</definedName>
                <definedName name="_xlnm.Print_Area" localSheetId="1">'Invoice Detail'!$A$1:$K${detailEnd}</definedName>
              </definedNames>
              <calcPr calcId="191029"/>
            </workbook>
            """;
    }

    private static string XlsxWorkbookRelationships() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
          <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet3.xml"/>
          <Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    private static string XlsxStyles() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="3"><numFmt numFmtId="164" formatCode="mm/dd/yyyy"/><numFmt numFmtId="165" formatCode="$#,##0.00"/><numFmt numFmtId="166" formatCode="0.00"/></numFmts>
          <fonts count="7">
            <font><sz val="10"/><name val="Aptos"/><family val="2"/></font>
            <font><b/><color rgb="FFFFFFFF"/><sz val="20"/><name val="Aptos Display"/><family val="2"/></font>
            <font><b/><color rgb="FFFFFFFF"/><sz val="10"/><name val="Aptos"/><family val="2"/></font>
            <font><b/><color rgb="FF17324D"/><sz val="10"/><name val="Aptos"/><family val="2"/></font>
            <font><color rgb="FF617187"/><sz val="9"/><name val="Aptos"/><family val="2"/></font>
            <font><b/><color rgb="FF17324D"/><sz val="12"/><name val="Aptos"/><family val="2"/></font>
            <font><b/><color rgb="FFFFFFFF"/><sz val="12"/><name val="Aptos"/><family val="2"/></font>
          </fonts>
          <fills count="7">
            <fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FF123B5D"/><bgColor indexed="64"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFEAF5FB"/><bgColor indexed="64"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFF7FAFC"/><bgColor indexed="64"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FFDDF1FA"/><bgColor indexed="64"/></patternFill></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FF087DB8"/><bgColor indexed="64"/></patternFill></fill>
          </fills>
          <borders count="2">
            <border><left/><right/><top/><bottom/><diagonal/></border>
            <border><left style="thin"><color rgb="FFC9D7E2"/></left><right style="thin"><color rgb="FFC9D7E2"/></right><top style="thin"><color rgb="FFC9D7E2"/></top><bottom style="thin"><color rgb="FFC9D7E2"/></bottom><diagonal/></border>
          </borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="19">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1" applyAlignment="1"><alignment vertical="center"/></xf>
            <xf numFmtId="0" fontId="2" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="3" fillId="3" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center"/></xf>
            <xf numFmtId="0" fontId="0" fillId="4" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
            <xf numFmtId="164" fontId="0" fillId="4" borderId="1" xfId="0" applyNumberFormat="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
            <xf numFmtId="166" fontId="0" fillId="4" borderId="1" xfId="0" applyNumberFormat="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
            <xf numFmtId="165" fontId="0" fillId="4" borderId="1" xfId="0" applyNumberFormat="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
            <xf numFmtId="0" fontId="0" fillId="5" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
            <xf numFmtId="164" fontId="0" fillId="5" borderId="1" xfId="0" applyNumberFormat="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
            <xf numFmtId="166" fontId="0" fillId="5" borderId="1" xfId="0" applyNumberFormat="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
            <xf numFmtId="165" fontId="0" fillId="5" borderId="1" xfId="0" applyNumberFormat="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
            <xf numFmtId="0" fontId="3" fillId="3" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
            <xf numFmtId="165" fontId="3" fillId="3" borderId="1" xfId="0" applyNumberFormat="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
            <xf numFmtId="0" fontId="6" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
            <xf numFmtId="165" fontId="6" fillId="2" borderId="1" xfId="0" applyNumberFormat="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
            <xf numFmtId="0" fontId="4" fillId="0" borderId="0" xfId="0" applyFont="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="3" fillId="0" borderId="1" xfId="0" applyFont="1" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
            <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
          </cellXfs>
          <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
        </styleSheet>
        """;

    private static string XlsxInvoiceWorksheet(BrandedInvoiceDocument invoice)
    {
        var xml = new StringBuilder();
        var merges = new List<string>();
        var row = 1;
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        xml.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        xml.Append("<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr><sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"16\" topLeftCell=\"A17\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        xml.Append("<sheetFormatPr defaultRowHeight=\"18\"/><cols>");
        foreach (var item in new[] { (1, 1, 12d), (2, 3, 14d), (4, 7, 18d), (8, 8, 10d), (9, 9, 12d), (10, 11, 13d) })
        {
            xml.Append("<col min=\"").Append(item.Item1).Append("\" max=\"").Append(item.Item2).Append("\" width=\"").Append(PdfNumber(item.Item3)).Append("\" customWidth=\"1\"/>");
        }
        xml.Append("</cols><sheetData>");

        XlsxRowStart(xml, row, 32); XlsxInline(xml, "A1", "US SIGNAL", 1); XlsxInline(xml, "D1", "US Signal SP Invoice", 1); XlsxInline(xml, "I1", invoice.InvoiceNumber, 1); XlsxRowEnd(xml);
        merges.AddRange(new[] { "A1:C2", "D1:H2", "I1:K2" });
        row = 3; XlsxRowStart(xml, row, 20); XlsxInline(xml, "A3", "Professional Services", 16); XlsxInline(xml, "I3", NormalizeTitle(invoice.InvoiceType), 16); XlsxRowEnd(xml); merges.AddRange(new[] { "A3:H3", "I3:K3" });

        XlsxPairRow(xml, 5, "A", "CUSTOMER", "B", invoice.CustomerName, "G", "INVOICE DATE", "H", FormatDate(invoice.InvoiceDate));
        XlsxPairRow(xml, 6, "A", "PROJECT", "B", $"{invoice.ProjectCode} - {invoice.ProjectName}", "G", "BILLING PERIOD", "H", $"{FormatDate(invoice.BillingPeriodStart)} through {FormatDate(invoice.BillingPeriodEnd)}");
        XlsxPairRow(xml, 7, "A", "PROJECT MANAGER", "B", invoice.ProjectManager, "G", "PROJECT COORDINATOR", "H", invoice.ProjectCoordinator);
        XlsxPairRow(xml, 8, "A", "PURCHASE ORDER", "B", Fallback(invoice.PurchaseOrderNumber, "Not configured"), "G", "CONTRACT TYPE", "H", Fallback(invoice.ContractType, "Not configured"));
        XlsxPairRow(xml, 10, "A", "CERTINIA ID", "B", Fallback(invoice.CertiniaId, "Not configured"), "G", "SELL QUOTE", "H", Fallback(invoice.SellQuote, "Not configured"));
        XlsxPairRow(xml, 11, "A", "SALESFORCE ID", "B", Fallback(invoice.SalesforceId, "Not configured"), "G", "PO AUTHORIZED", "H", invoice.PurchaseOrderAmount is decimal po ? Money(po) : "Not configured");
        merges.AddRange(new[] { "B5:F5", "H5:K5", "B6:F6", "H6:K6", "B7:F7", "H7:K7", "B8:F8", "H8:K8", "B10:F10", "H10:K10", "B11:F11", "H11:K11" });

        XlsxRowStart(xml, 13, 25); XlsxInline(xml, "A13", "BILLING SUMMARY", 3); XlsxInline(xml, "D13", "Total hours", 3); XlsxNumber(xml, "E13", invoice.Lines.Sum(line => line.Hours), 13); XlsxInline(xml, "G13", "Invoice total", 3); XlsxNumber(xml, "H13", invoice.TotalAmount, 13); XlsxRowEnd(xml); merges.AddRange(new[] { "A13:C13", "H13:K13" });

        XlsxRowStart(xml, 16, 28);
        XlsxInline(xml, "A16", "Date", 2); XlsxInline(xml, "B16", "Resource", 2); XlsxInline(xml, "D16", "Work performed", 2); XlsxInline(xml, "H16", "Hours", 2); XlsxInline(xml, "I16", "Rate", 2); XlsxInline(xml, "J16", "Amount", 2); XlsxRowEnd(xml);
        merges.AddRange(new[] { "B16:C16", "D16:G16", "J16:K16" });

        row = 17;
        for (var index = 0; index < invoice.Lines.Count; index++, row++)
        {
            var line = invoice.Lines[index];
            var baseStyle = index % 2 == 0 ? 4 : 8;
            var dateStyle = index % 2 == 0 ? 5 : 9;
            var numberStyle = index % 2 == 0 ? 6 : 10;
            var moneyStyle = index % 2 == 0 ? 7 : 11;
            XlsxRowStart(xml, row, 48);
            XlsxDate(xml, $"A{row}", line.WorkDate, dateStyle);
            XlsxInline(xml, $"B{row}", line.Resource, baseStyle);
            XlsxInline(xml, $"D{row}", $"{line.TaskCode} {line.TaskName}\n{line.Description}", baseStyle);
            XlsxNumber(xml, $"H{row}", line.Hours, numberStyle);
            XlsxNumber(xml, $"I{row}", line.UnitRate, moneyStyle);
            XlsxNumber(xml, $"J{row}", line.Amount, moneyStyle);
            XlsxRowEnd(xml);
            merges.AddRange(new[] { $"B{row}:C{row}", $"D{row}:G{row}", $"J{row}:K{row}" });
        }

        var subtotalRow = row + 1;
        XlsxTotalRow(xml, subtotalRow, "Subtotal", invoice.SubtotalAmount, false, merges);
        XlsxTotalRow(xml, subtotalRow + 1, "Adjustments", invoice.AdjustmentAmount, false, merges);
        XlsxTotalRow(xml, subtotalRow + 2, "Tax", invoice.TaxAmount, false, merges);
        XlsxTotalRow(xml, subtotalRow + 4, "Invoice total", invoice.TotalAmount, true, merges);
        var notesRow = subtotalRow + 6;
        XlsxRowStart(xml, notesRow, 20); XlsxInline(xml, $"A{notesRow}", "Invoice notes", 3); XlsxRowEnd(xml); merges.Add($"A{notesRow}:C{notesRow}");
        XlsxRowStart(xml, notesRow + 1, 48); XlsxInline(xml, $"A{notesRow + 1}", Fallback(invoice.Notes, "No invoice notes."), 4); XlsxRowEnd(xml); merges.Add($"A{notesRow + 1}:K{notesRow + 2}");
        XlsxRowStart(xml, notesRow + 4, 30); XlsxInline(xml, $"A{notesRow + 4}", $"Generated from the immutable ProjectPulse invoice snapshot. {invoice.PersonalNamesSummary}", 16); XlsxRowEnd(xml); merges.Add($"A{notesRow + 4}:K{notesRow + 4}");

        xml.Append("</sheetData><mergeCells count=\"").Append(merges.Count).Append("\">");
        foreach (var merge in merges) xml.Append("<mergeCell ref=\"").Append(merge).Append("\"/>");
        xml.Append("</mergeCells><pageMargins left=\"0.25\" right=\"0.25\" top=\"0.4\" bottom=\"0.4\" header=\"0.2\" footer=\"0.2\"/><pageSetup paperSize=\"9\" orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"0\"/></worksheet>");
        return xml.ToString();
    }

    private static string XlsxDetailWorksheet(BrandedInvoiceDocument invoice)
    {
        var xml = new StringBuilder();
        var end = Math.Max(2, invoice.Lines.Count + 1);
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        xml.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr><sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews><sheetFormatPr defaultRowHeight=\"18\"/><cols>");
        foreach (var item in new[] { (1, 1, 8d), (2, 2, 13d), (3, 3, 22d), (4, 4, 16d), (5, 5, 28d), (6, 6, 48d), (7, 7, 10d), (8, 8, 18d), (9, 9, 28d), (10, 10, 13d), (11, 11, 15d) })
        {
            xml.Append("<col min=\"").Append(item.Item1).Append("\" max=\"").Append(item.Item2).Append("\" width=\"").Append(PdfNumber(item.Item3)).Append("\" customWidth=\"1\"/>");
        }
        xml.Append("</cols><sheetData>");
        XlsxRowStart(xml, 1, 30);
        var headers = new[] { "Line", "Work Date", "Resource", "Task Code", "Task", "Work Description", "Hours", "Rate Code", "Rate Description", "Unit Rate", "Amount" };
        var columns = new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K" };
        for (var index = 0; index < headers.Length; index++) XlsxInline(xml, $"{columns[index]}1", headers[index], 2);
        XlsxRowEnd(xml);
        var row = 2;
        for (var index = 0; index < invoice.Lines.Count; index++, row++)
        {
            var line = invoice.Lines[index];
            var baseStyle = index % 2 == 0 ? 4 : 8;
            var dateStyle = index % 2 == 0 ? 5 : 9;
            var numberStyle = index % 2 == 0 ? 6 : 10;
            var moneyStyle = index % 2 == 0 ? 7 : 11;
            XlsxRowStart(xml, row, 44);
            XlsxNumber(xml, $"A{row}", line.LineNumber, baseStyle);
            XlsxDate(xml, $"B{row}", line.WorkDate, dateStyle);
            XlsxInline(xml, $"C{row}", line.Resource, baseStyle);
            XlsxInline(xml, $"D{row}", line.TaskCode, baseStyle);
            XlsxInline(xml, $"E{row}", line.TaskName, baseStyle);
            XlsxInline(xml, $"F{row}", line.Description, baseStyle);
            XlsxNumber(xml, $"G{row}", line.Hours, numberStyle);
            XlsxInline(xml, $"H{row}", line.RateCode, baseStyle);
            XlsxInline(xml, $"I{row}", line.RateDescription, baseStyle);
            XlsxNumber(xml, $"J{row}", line.UnitRate, moneyStyle);
            XlsxNumber(xml, $"K{row}", line.Amount, moneyStyle);
            XlsxRowEnd(xml);
        }
        var subtotal = row + 1;
        XlsxRowStart(xml, subtotal, 20); XlsxInline(xml, $"J{subtotal}", "Subtotal", 12); XlsxNumber(xml, $"K{subtotal}", invoice.SubtotalAmount, 13); XlsxRowEnd(xml);
        XlsxRowStart(xml, subtotal + 1, 20); XlsxInline(xml, $"J{subtotal + 1}", "Adjustments", 12); XlsxNumber(xml, $"K{subtotal + 1}", invoice.AdjustmentAmount, 13); XlsxRowEnd(xml);
        XlsxRowStart(xml, subtotal + 2, 20); XlsxInline(xml, $"J{subtotal + 2}", "Tax", 12); XlsxNumber(xml, $"K{subtotal + 2}", invoice.TaxAmount, 13); XlsxRowEnd(xml);
        XlsxRowStart(xml, subtotal + 4, 24); XlsxInline(xml, $"J{subtotal + 4}", "Invoice total", 14); XlsxNumber(xml, $"K{subtotal + 4}", invoice.TotalAmount, 15); XlsxRowEnd(xml);
        xml.Append("</sheetData><autoFilter ref=\"A1:K").Append(end).Append("\"/><pageMargins left=\"0.2\" right=\"0.2\" top=\"0.35\" bottom=\"0.35\" header=\"0.2\" footer=\"0.2\"/><pageSetup paperSize=\"9\" orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"0\"/></worksheet>");
        return xml.ToString();
    }

    private static string XlsxAuditWorksheet(BrandedInvoiceDocument invoice)
    {
        var rows = new (string Key, string Value)[]
        {
            ("Document title", "US Signal SP Invoice"),
            ("Invoice number", invoice.InvoiceNumber),
            ("Invoice status", invoice.InvoiceStatus),
            ("Invoice type", invoice.InvoiceType),
            ("Customer", invoice.CustomerName),
            ("Project", $"{invoice.ProjectCode} - {invoice.ProjectName}"),
            ("Immutable snapshot SHA256", invoice.ImmutableSnapshotSha256),
            ("Personal-name controls", invoice.PersonalNamesSummary),
            ("Generated at UTC", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
            ("Artifact purpose", "Internal audit metadata; not customer-facing")
        };
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews><sheetFormatPr defaultRowHeight=\"20\"/><cols><col min=\"1\" max=\"1\" width=\"28\" customWidth=\"1\"/><col min=\"2\" max=\"2\" width=\"86\" customWidth=\"1\"/></cols><sheetData>");
        XlsxRowStart(xml, 1, 28); XlsxInline(xml, "A1", "Audit field", 2); XlsxInline(xml, "B1", "Value", 2); XlsxRowEnd(xml);
        for (var index = 0; index < rows.Length; index++)
        {
            var row = index + 2;
            XlsxRowStart(xml, row, rows[index].Key.Contains("SHA", StringComparison.OrdinalIgnoreCase) ? 42 : 22);
            XlsxInline(xml, $"A{row}", rows[index].Key, 17); XlsxInline(xml, $"B{row}", rows[index].Value, 18); XlsxRowEnd(xml);
        }
        xml.Append("</sheetData><pageMargins left=\"0.4\" right=\"0.4\" top=\"0.5\" bottom=\"0.5\" header=\"0.2\" footer=\"0.2\"/></worksheet>");
        return xml.ToString();
    }

    private static void XlsxPairRow(StringBuilder xml, int row, string leftLabelColumn, string leftLabel, string leftValueColumn, string leftValue, string rightLabelColumn, string rightLabel, string rightValueColumn, string rightValue)
    {
        XlsxRowStart(xml, row, 24); XlsxInline(xml, $"{leftLabelColumn}{row}", leftLabel, 3); XlsxInline(xml, $"{leftValueColumn}{row}", leftValue, 4); XlsxInline(xml, $"{rightLabelColumn}{row}", rightLabel, 3); XlsxInline(xml, $"{rightValueColumn}{row}", rightValue, 4); XlsxRowEnd(xml);
    }

    private static void XlsxTotalRow(StringBuilder xml, int row, string label, decimal value, bool total, ICollection<string> merges)
    {
        XlsxRowStart(xml, row, total ? 26 : 20); XlsxInline(xml, $"H{row}", label, total ? 14 : 12); XlsxNumber(xml, $"J{row}", value, total ? 15 : 13); XlsxRowEnd(xml); merges.Add($"H{row}:I{row}"); merges.Add($"J{row}:K{row}");
    }

    private static void XlsxRowStart(StringBuilder xml, int row, int height) => xml.Append("<row r=\"").Append(row).Append("\" ht=\"").Append(height).Append("\" customHeight=\"1\">");
    private static void XlsxRowEnd(StringBuilder xml) => xml.Append("</row>");

    private static void XlsxInline(StringBuilder xml, string reference, string? value, int style)
    {
        xml.Append("<c r=\"").Append(reference).Append("\" s=\"").Append(style).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">").Append(XlsxXml(value)).Append("</t></is></c>");
    }

    private static void XlsxNumber(StringBuilder xml, string reference, decimal value, int style)
    {
        xml.Append("<c r=\"").Append(reference).Append("\" s=\"").Append(style).Append("\"><v>").Append(value.ToString(CultureInfo.InvariantCulture)).Append("</v></c>");
    }

    private static void XlsxDate(StringBuilder xml, string reference, DateOnly? value, int style)
    {
        if (value is null) { XlsxInline(xml, reference, "", style); return; }
        var serial = value.Value.ToDateTime(TimeOnly.MinValue).ToOADate();
        xml.Append("<c r=\"").Append(reference).Append("\" s=\"").Append(style).Append("\"><v>").Append(serial.ToString("0.########", CultureInfo.InvariantCulture)).Append("</v></c>");
    }

    private static void AddXlsxEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string XlsxXml(string? value)
    {
        var normalized = new string((value ?? string.Empty).Where(character => character is '\t' or '\n' or '\r' || character >= ' ').ToArray());
        return System.Security.SecurityElement.Escape(Limit(normalized, 32767)) ?? string.Empty;
    }

    private static IReadOnlyList<string> WrapText(string? value, int maxCharacters)
    {
        var normalized = string.Join(" ", (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(normalized)) return new[] { string.Empty };
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var word in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length == 0) { current.Append(word); continue; }
            if (current.Length + 1 + word.Length <= maxCharacters) { current.Append(' ').Append(word); continue; }
            lines.Add(current.ToString()); current.Clear(); current.Append(word);
        }
        if (current.Length > 0) lines.Add(current.ToString());
        return lines;
    }

    private static string FormatDate(DateOnly? value) => value?.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture) ?? "Not configured";
    private static string Money(decimal value) => value.ToString("$#,##0.00", CultureInfo.InvariantCulture);
    private static string NormalizeTitle(string? value) => string.IsNullOrWhiteSpace(value) ? "Invoice" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('_', ' ').Trim().ToLowerInvariant());
    private static string Fallback(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
    private static string PdfNumber(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static double EstimatePdfTextWidth(string value, double size, bool bold) => value.Length * size * (bold ? 0.56d : 0.51d);

    private static string PdfTextSafe(string? value)
    {
        var builder = new StringBuilder();
        foreach (var character in value ?? string.Empty)
        {
            builder.Append(character switch
            {
                '\u2013' or '\u2014' => '-',
                '\u2022' => '*',
                >= ' ' and <= '~' => character,
                _ => ' '
            });
        }
        return builder.ToString();
    }

    private static string PdfEscape(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }
}
