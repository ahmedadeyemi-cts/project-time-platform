using ClosedXML.Excel;
using System.IO.Compression;
using System.Xml.Linq;
using ProjectTime.Api;

internal static class OfficeRangeBudgetTests
{
    internal static void Run()
    {
        int checks = 0;
        foreach (bool sparse in new[] { false, true })
        {
            using var bytes = new MemoryStream();
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("Example");
                sheet.Cell(sparse ? "IV1" : "B1").Value = "Header";
                sheet.Cell(sparse ? "A10000" : "A10").Value = "Value";
                workbook.SaveAs(bytes);
            }
            bytes.Position = 0;
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Update, true))
            {
                var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!;
                XDocument xml;
                using (var input = entry.Open()) xml = XDocument.Load(input);
                xml.Root!.Elements().Where(x => x.Name.LocalName == "dimension").Remove();
                entry.Delete();
                using var output = zip.CreateEntry("xl/worksheets/sheet1.xml").Open();
                xml.Save(output);
            }
            bytes.Position = 0;
            if (sparse)
            {
                try { BoundedOfficeInput.Validate(bytes); throw new Exception("Sparse workbook exceeded the range budget"); }
                catch (InvalidDataException) { checks++; }
            }
            else
            {
                BoundedOfficeInput.Validate(bytes);
                using var parsed = new XLWorkbook(bytes);
                var range = parsed.Worksheet(1).RangeUsed()!;
                if (range.RowCount() != 10 || range.ColumnCount() != 2) throw new Exception("Ordinary sparse workbook changed");
                checks++;
            }
        }
        Console.WriteLine($"OFFICE_RANGE_BUDGET=PASS assertions={checks}");
    }
}
