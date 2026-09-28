using System.IO.Compression;
using System.Xml;

namespace ProjectTime.Api;

/// <summary>Validate archive/XML budgets before a rich Office parser allocates its object model.</summary>
public static class BoundedOfficeInput
{
    public const int MaximumRows = 10000;
    public const int MaximumColumns = 256;
    public const int MaximumCells = 250000;
    private const long MaximumExpandedBytes = 64 * 1024 * 1024;

    public static void Validate(string path)
    {
        using var stream = File.OpenRead(path);
        Validate(stream);
    }

    public static void Validate(Stream stream)
    {
        if (!stream.CanSeek || stream.Length > 32 * 1024 * 1024)
            throw new InvalidDataException("Office input exceeds its compressed size limit.");
        var initial = stream.Position;
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > 2048) throw new InvalidDataException("Office archive has too many entries.");
            long expanded = 0;
            int nodes = 0, cells = 0, sheets = 0;
            foreach (var entry in archive.Entries)
            {
                expanded = checked(expanded + entry.Length);
                if (entry.Length > 16 * 1024 * 1024 || expanded > MaximumExpandedBytes ||
                    entry.Length > Math.Max(1024 * 1024, entry.CompressedLength * 200))
                    throw new InvalidDataException("Office archive exceeds its expansion budget.");
                if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                    !entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)) continue;
                var worksheet = entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase);
                if (worksheet && ++sheets > 50) throw new InvalidDataException("Workbook has too many worksheets.");
                using var data = entry.Open();
                using var reader = XmlReader.Create(data, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                    MaxCharactersInDocument = 16 * 1024 * 1024,
                    IgnoreComments = true, IgnoreProcessingInstructions = true
                });
                while (reader.Read())
                {
                    if (++nodes > 1000000 || reader.Depth > 64)
                        throw new InvalidDataException("Office XML exceeds its structure budget.");
                    if (!worksheet || reader.NodeType != XmlNodeType.Element) continue;
                    if (reader.LocalName == "c")
                    {
                        if (++cells > MaximumCells) throw new InvalidDataException("Workbook has too many cells.");
                        CheckReference(reader.GetAttribute("r"));
                    }
                    if (reader.LocalName == "dimension")
                        foreach (var reference in (reader.GetAttribute("ref") ?? "").Split(':')) CheckReference(reference);
                    if (reader.LocalName == "row" && int.TryParse(reader.GetAttribute("r"), out var row) && (row < 1 || row > MaximumRows))
                        throw new InvalidDataException("Workbook row exceeds its limit.");
                }
            }
        }
        finally { stream.Position = initial; }
    }

    private static void CheckReference(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return;
        if (reference.Length > 12) throw new InvalidDataException("Invalid cell reference.");
        int index = 0, column = 0;
        while (index < reference.Length && reference[index] is >= 'A' and <= 'Z')
        {
            column = checked(column * 26 + reference[index++] - 'A' + 1);
            if (column > MaximumColumns) throw new InvalidDataException("Workbook column exceeds its limit.");
        }
        if (column < 1 || !int.TryParse(reference[index..], out var row) || row < 1 || row > MaximumRows || (long)row * column > MaximumCells)
            throw new InvalidDataException("Workbook range exceeds its work budget.");
    }
}
