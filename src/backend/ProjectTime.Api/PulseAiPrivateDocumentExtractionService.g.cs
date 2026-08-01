using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClosedXML.Excel;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiPrivateDocumentExtractionService
{
    private static readonly Regex HtmlScriptStyle = new(
        "<(script|style)[^>]*>.*?</\\1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex HtmlTags = new(
        "<[^>]+>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex MultiSpace = new(
        "[ \\t]+",
        RegexOptions.Compiled);

    private static readonly Regex ExcessBlankLines = new(
        "(?:\\r?\\n){3,}",
        RegexOptions.Compiled);

    private readonly ILogger<PulseAiPrivateDocumentExtractionService> _logger;

    public PulseAiPrivateDocumentExtractionService(
        ILogger<PulseAiPrivateDocumentExtractionService> logger)
    {
        _logger = logger;
    }

    public async Task<PulseAiDocumentExtractionResult> ExtractAsync(
        PulseAiAuthorizedDocumentSource source,
        PulseAiDocumentPipelineOptions options,
        CancellationToken cancellationToken = default)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var safety = await AssessSafetyAsync(source, options, cancellationToken);
        var blockers = safety.Blockers.ToList();

        if (!options.ExtractionPreviewEnabled)
        {
            blockers.Add("Private document extraction preview is disabled by configuration.");
        }

        if (!safety.AllowedForPreview || !options.ExtractionPreviewEnabled)
        {
            return EmptyResult(
                source,
                safety,
                options.ExtractionPreviewEnabled
                    ? "blocked_by_document_safety_policy"
                    : "extraction_preview_disabled",
                blockers,
                safety.Warnings,
                generatedAt);
        }

        try
        {
            var extension = Path.GetExtension(source.OriginalFileName).ToLowerInvariant();
            var result = extension switch
            {
                ".pdf" => ExtractPdf(source, options, safety, cancellationToken),
                ".docx" => ExtractDocx(source, options, safety, cancellationToken),
                ".pptx" => ExtractPptx(source, options, safety, cancellationToken),
                ".xlsx" => ExtractXlsx(source, options, safety, cancellationToken),
                ".html" or ".htm" => await ExtractHtmlAsync(source, options, safety, cancellationToken),
                ".xml" => await ExtractXmlAsync(source, options, safety, cancellationToken),
                ".txt" or ".md" or ".csv" or ".json" =>
                    await ExtractTextAsync(source, options, safety, cancellationToken),
                _ => EmptyResult(
                    source,
                    safety,
                    "unsupported_document_format",
                    ["The file extension is not supported by the private extraction pipeline."],
                    safety.Warnings,
                    generatedAt)
            };

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI private document extraction failed. DocumentId={DocumentId} Diagnostic={Diagnostic}",
                source.DocumentId,
                Diagnostic(exception));

            return EmptyResult(
                source,
                safety,
                "extraction_preview_failed",
                ["The private extractor could not safely interpret this document."],
                [.. safety.Warnings, $"Diagnostic code: {Diagnostic(exception)}"],
                generatedAt);
        }
    }

    public IReadOnlyList<PulseAiDocumentChunk> CreateChunks(
        PulseAiDocumentExtractionResult extraction,
        PulseAiDocumentPipelineOptions options)
    {
        if (!extraction.ExtractionSucceeded || extraction.Sections.Count == 0)
        {
            return [];
        }

        var chunks = new List<PulseAiDocumentChunk>();
        var globalIndex = 0;

        foreach (var section in extraction.Sections)
        {
            var text = Normalize(section.Text);
            if (text.Length == 0) continue;

            var start = 0;
            while (start < text.Length && chunks.Count < options.MaximumChunks)
            {
                var desiredEnd = Math.Min(text.Length, start + options.ChunkCharacters);
                var end = FindNaturalBoundary(text, start, desiredEnd);
                if (end <= start) end = desiredEnd;

                var chunkText = text[start..end].Trim();
                if (chunkText.Length > 0)
                {
                    var textHash = Sha256(chunkText);
                    var chunkId = DeterministicChunkId(
                        extraction.DocumentId,
                        extraction.SourceSha256,
                        section.Anchor,
                        globalIndex,
                        textHash);

                    chunks.Add(new PulseAiDocumentChunk(
                        ChunkId: chunkId,
                        DocumentId: extraction.DocumentId,
                        ChunkIndex: globalIndex,
                        Anchor: section.Anchor,
                        Title: section.Title,
                        PageNumber: section.PageNumber,
                        SheetName: section.SheetName,
                        Text: chunkText,
                        CharacterCount: chunkText.Length,
                        EstimatedTokenCount: EstimateTokens(chunkText.Length),
                        TextSha256: textHash,
                        SourceSha256: extraction.SourceSha256));
                    globalIndex++;
                }

                if (end >= text.Length) break;
                var next = Math.Max(end - options.ChunkOverlapCharacters, start + 1);
                start = next;
            }

            if (chunks.Count >= options.MaximumChunks) break;
        }

        return chunks;
    }

    public IReadOnlyList<PulseAiIndexProjectionRecord> BuildIndexProjection(
        PulseAiAuthorizedDocumentSource source,
        IReadOnlyList<PulseAiDocumentChunk> chunks,
        PulseAiDocumentPipelineOptions options)
    {
        var preparedAt = DateTimeOffset.UtcNow;
        var documentVersion = $"{source.OriginalFileName}@{source.UploadedAt:O}";
        var embeddingStatus = options.PrivateEmbeddingEndpointConfigured
            ? "private_embedding_endpoint_configured_execution_not_authorized"
            : "private_embedding_endpoint_not_configured";
        var indexStatus = options.PrivateVectorIndexConfigured
            ? "private_index_configured_write_not_authorized"
            : "private_index_not_configured";

        return chunks.Select(chunk => new PulseAiIndexProjectionRecord(
            ChunkId: chunk.ChunkId,
            DocumentId: source.DocumentId,
            ProjectId: source.ProjectId,
            ProjectCode: source.ProjectCode,
            ProjectName: source.ProjectName,
            CustomerName: source.CustomerName,
            DocumentCategory: source.DocumentCategory,
            DocumentVersion: documentVersion,
            Classification: source.Classification,
            EngineeringVisible: source.EngineeringVisible,
            AiTimesheetContextEnabled: source.AiTimesheetContextEnabled,
            AccessScope: source.AccessScope,
            CitationAnchor: chunk.Anchor,
            PageNumber: chunk.PageNumber,
            SheetName: chunk.SheetName,
            SourceSha256: chunk.SourceSha256,
            TextSha256: chunk.TextSha256,
            CharacterCount: chunk.CharacterCount,
            EstimatedTokenCount: chunk.EstimatedTokenCount,
            EmbeddingStatus: embeddingStatus,
            IndexStatus: indexStatus,
            PreparedAt: preparedAt)).ToArray();
    }

    private static async Task<PulseAiDocumentSafetyAssessment> AssessSafetyAsync(
        PulseAiAuthorizedDocumentSource source,
        PulseAiDocumentPipelineOptions options,
        CancellationToken cancellationToken)
    {
        var blockers = new List<string>();
        var warnings = new List<string>();
        var extension = Path.GetExtension(source.OriginalFileName).ToLowerInvariant();
        var extensionAllowed = PulseAiPrivateDocumentPipelinePolicy.SupportedExtensions
            .Contains(extension, StringComparer.OrdinalIgnoreCase);
        var macroEnabled = PulseAiPrivateDocumentPipelinePolicy.ExplicitlyBlockedExtensions
            .Contains(extension, StringComparer.OrdinalIgnoreCase)
            || extension is ".docm" or ".xlsm" or ".pptm";

        if (!extensionAllowed)
            blockers.Add($"Extension {extension} is not in the private extraction allowlist.");
        if (macroEnabled)
            blockers.Add("Macro-enabled, executable, script, or archive formats are prohibited.");

        string fullPath;
        string rootPath;
        var pathConfined = false;
        try
        {
            fullPath = Path.GetFullPath(source.StoragePath);
            rootPath = Path.GetFullPath(options.UploadRoot);
            var normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            pathConfined = fullPath.StartsWith(
                normalizedRoot,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }
        catch
        {
            fullPath = source.StoragePath;
            rootPath = options.UploadRoot;
        }

        if (!pathConfined)
            blockers.Add("The stored path is outside the configured private upload root.");

        var exists = File.Exists(fullPath);
        if (!exists)
        {
            blockers.Add("The stored document file was not found.");
            return new PulseAiDocumentSafetyAssessment(
                Status: "file_missing",
                Extension: extension,
                DetectedFormat: "unknown",
                ExtensionAllowed: extensionAllowed,
                SignatureMatchesExtension: false,
                SizeWithinLimit: false,
                PathConfined: pathConfined,
                IsRegularFile: false,
                ReparsePointDetected: false,
                MacroEnabledFormat: macroEnabled,
                ArchiveBombRiskDetected: false,
                MalwareScanAttested: options.MalwareScanAttested,
                MalwareScannerMode: options.MalwareScannerMode,
                FileSizeBytes: 0,
                SourceSha256: string.Empty,
                Blockers: blockers,
                Warnings: warnings);
        }

        var attributes = File.GetAttributes(fullPath);
        var reparsePoint = attributes.HasFlag(FileAttributes.ReparsePoint);
        var regularFile = !attributes.HasFlag(FileAttributes.Directory);
        if (reparsePoint) blockers.Add("Symbolic links and reparse points are not accepted.");
        if (!regularFile) blockers.Add("The stored path is not a regular file.");

        var fileInfo = new FileInfo(fullPath);
        var sizeWithinLimit = fileInfo.Length > 0 && fileInfo.Length <= options.MaximumFileBytes;
        if (!sizeWithinLimit)
            blockers.Add($"Document size must be between 1 byte and {options.MaximumFileBytes} bytes.");
        if (source.SizeBytes > 0 && source.SizeBytes != fileInfo.Length)
            warnings.Add("Stored-file size differs from document metadata; the source requires reconciliation.");

        var header = new byte[12];
        await using (var stream = File.OpenRead(fullPath))
        {
            var read = await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
            if (read < header.Length) Array.Resize(ref header, read);
        }

        var detected = DetectFormat(header, extension);
        var signatureMatches = SignatureMatches(extension, detected);
        if (!signatureMatches)
            blockers.Add($"File signature {detected} does not match extension {extension}.");

        var archiveBombRisk = false;
        if (detected == "zip_openxml" && sizeWithinLimit)
        {
            archiveBombRisk = InspectArchiveRisk(fullPath, options, warnings);
            if (archiveBombRisk)
                blockers.Add("The Open XML package exceeds safe archive entry or expansion limits.");
        }

        if (!options.MalwareScanAttested)
            blockers.Add("A verifiable malware-scan result is required before parsing document content.");
        if (options.MalwareScannerMode.Equals("not_configured", StringComparison.OrdinalIgnoreCase))
            warnings.Add("Malware scanner mode is not configured.");

        var sourceHash = sizeWithinLimit
            ? await Sha256FileAsync(fullPath, cancellationToken)
            : string.Empty;

        var status = blockers.Count == 0 ? "document_admitted_for_private_preview" : "document_blocked";
        return new PulseAiDocumentSafetyAssessment(
            Status: status,
            Extension: extension,
            DetectedFormat: detected,
            ExtensionAllowed: extensionAllowed,
            SignatureMatchesExtension: signatureMatches,
            SizeWithinLimit: sizeWithinLimit,
            PathConfined: pathConfined,
            IsRegularFile: regularFile,
            ReparsePointDetected: reparsePoint,
            MacroEnabledFormat: macroEnabled,
            ArchiveBombRiskDetected: archiveBombRisk,
            MalwareScanAttested: options.MalwareScanAttested,
            MalwareScannerMode: options.MalwareScannerMode,
            FileSizeBytes: fileInfo.Length,
            SourceSha256: sourceHash,
            Blockers: blockers,
            Warnings: warnings);
    }

    private static PulseAiDocumentExtractionResult ExtractPdf(
        PulseAiAuthorizedDocumentSource source,
        PulseAiDocumentPipelineOptions options,
        PulseAiDocumentSafetyAssessment safety,
        CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(source.StoragePath);
        var sections = new List<PulseAiExtractedSection>();
        var warnings = safety.Warnings.ToList();
        var totalCharacters = 0;
        var pageCount = document.NumberOfPages;
        var processedPages = 0;
        var lowTextPages = 0;

        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (processedPages >= options.MaximumPages || sections.Count >= options.MaximumSections) break;
            var text = Normalize(ContentOrderTextExtractor.GetText(page));
            if (text.Length < 40) lowTextPages++;
            if (text.Length == 0)
            {
                processedPages++;
                continue;
            }

            text = LimitText(text, options.MaximumCharacters - totalCharacters);
            if (text.Length == 0) break;
            sections.Add(CreateSection(
                sections.Count,
                $"page:{page.Number}",
                $"Page {page.Number}",
                text,
                page.Number,
                null));
            totalCharacters += text.Length;
            processedPages++;
            if (totalCharacters >= options.MaximumCharacters) break;
        }

        if (pageCount > options.MaximumPages)
            warnings.Add($"Only the first {options.MaximumPages} PDF pages were evaluated.");
        if (totalCharacters >= options.MaximumCharacters)
            warnings.Add("Extraction reached the configured character limit.");

        var ocrRequired = pageCount > 0
            && (sections.Count == 0 || lowTextPages >= Math.Max(1, processedPages / 2));
        if (ocrRequired)
            warnings.Add("The PDF appears image-only or text-sparse and requires the private OCR adapter.");

        return ReadyResult(
            source,
            safety,
            "pdf_pdfpig_content_order",
            pageCount,
            sections,
            ocrRequired,
            warnings);
    }

    private static PulseAiDocumentExtractionResult ExtractDocx(
        PulseAiAuthorizedDocumentSource source,
        PulseAiDocumentPipelineOptions options,
        PulseAiDocumentSafetyAssessment safety,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(source.StoragePath);
        var documentEntry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("DOCX word/document.xml was not found.");
        using var stream = documentEntry.Open();
        var xml = XDocument.Load(stream, System.Xml.Linq.LoadOptions.PreserveWhitespace);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        var sections = new List<PulseAiExtractedSection>();
        var currentTitle = "Document body";
        var current = new StringBuilder();
        var totalCharacters = 0;

        void Flush()
        {
            var text = Normalize(current.ToString());
            current.Clear();
            if (text.Length == 0 || sections.Count >= options.MaximumSections) return;
            text = LimitText(text, options.MaximumCharacters - totalCharacters);
            if (text.Length == 0) return;
            sections.Add(CreateSection(
                sections.Count,
                $"docx:section:{sections.Count + 1}",
                currentTitle,
                text,
                null,
                null));
            totalCharacters += text.Length;
        }

        foreach (var paragraph in xml.Descendants(w + "p"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (totalCharacters >= options.MaximumCharacters || sections.Count >= options.MaximumSections) break;
            var paragraphText = string.Concat(paragraph.Descendants(w + "t").Select(node => node.Value));
            paragraphText = NormalizeInline(paragraphText);
            if (paragraphText.Length == 0) continue;
            var style = paragraph.Descendants(w + "pStyle")
                .Select(node => node.Attribute(w + "val")?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            var isHeading = style?.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) == true
                || style?.Equals("Title", StringComparison.OrdinalIgnoreCase) == true;
            if (isHeading)
            {
                Flush();
                currentTitle = paragraphText;
            }
            else
            {
                current.AppendLine(paragraphText);
            }
        }
        Flush();

        return ReadyResult(
            source,
            safety,
            "docx_openxml_paragraph_order",
            0,
            sections,
            false,
            safety.Warnings);
    }

    private static PulseAiDocumentExtractionResult ExtractPptx(
        PulseAiAuthorizedDocumentSource source,
        PulseAiDocumentPipelineOptions options,
        PulseAiDocumentSafetyAssessment safety,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(source.StoragePath);
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var slideEntries = archive.Entries
            .Where(entry => Regex.IsMatch(entry.FullName, "^ppt/slides/slide[0-9]+\\.xml$", RegexOptions.IgnoreCase))
            .OrderBy(entry => NumericSuffix(entry.Name))
            .Take(options.MaximumPages)
            .ToArray();

        var sections = new List<PulseAiExtractedSection>();
        var totalCharacters = 0;
        foreach (var entry in slideEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sections.Count >= options.MaximumSections || totalCharacters >= options.MaximumCharacters) break;
            using var stream = entry.Open();
            var xml = XDocument.Load(stream, System.Xml.Linq.LoadOptions.PreserveWhitespace);
            var textRuns = xml.Descendants(a + "t")
                .Select(node => NormalizeInline(node.Value))
                .Where(value => value.Length > 0)
                .ToArray();
            var text = LimitText(Normalize(string.Join(Environment.NewLine, textRuns)), options.MaximumCharacters - totalCharacters);
            if (text.Length == 0) continue;
            var slideNumber = NumericSuffix(entry.Name);
            sections.Add(CreateSection(
                sections.Count,
                $"slide:{slideNumber}",
                $"Slide {slideNumber}",
                text,
                slideNumber,
                null));
            totalCharacters += text.Length;
        }

        var warnings = safety.Warnings.ToList();
        if (archive.Entries.Count(entry => entry.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)) > options.MaximumPages)
            warnings.Add($"Only the first {options.MaximumPages} slides were evaluated.");

        return ReadyResult(
            source,
            safety,
            "pptx_openxml_slide_text",
            slideEntries.Length,
            sections,
            sections.Count == 0,
            warnings);
    }

    private static PulseAiDocumentExtractionResult ExtractXlsx(
        PulseAiAuthorizedDocumentSource source,
        PulseAiDocumentPipelineOptions options,
        PulseAiDocumentSafetyAssessment safety,
        CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook(source.StoragePath);
        var sections = new List<PulseAiExtractedSection>();
        var totalCharacters = 0;
        var sheetCount = 0;

        foreach (var worksheet in workbook.Worksheets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sheetCount >= options.MaximumPages || sections.Count >= options.MaximumSections) break;
            var range = worksheet.RangeUsed();
            sheetCount++;
            if (range is null) continue;

            var builder = new StringBuilder();
            foreach (var row in range.RowsUsed())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = row.Cells(range.RangeAddress.FirstAddress.ColumnNumber, range.RangeAddress.LastAddress.ColumnNumber)
                    .Select(cell => NormalizeInline(cell.GetFormattedString()))
                    .ToArray();
                if (values.All(string.IsNullOrWhiteSpace)) continue;
                builder.AppendLine(string.Join("\t", values));
                if (builder.Length + totalCharacters >= options.MaximumCharacters) break;
            }

            var text = LimitText(Normalize(builder.ToString()), options.MaximumCharacters - totalCharacters);
            if (text.Length == 0) continue;
            sections.Add(CreateSection(
                sections.Count,
                $"sheet:{SafeAnchor(worksheet.Name)}",
                worksheet.Name,
                text,
                null,
                worksheet.Name));
            totalCharacters += text.Length;
            if (totalCharacters >= options.MaximumCharacters) break;
        }

        var warnings = safety.Warnings.ToList();
        if (workbook.Worksheets.Count > options.MaximumPages)
            warnings.Add($"Only the first {options.MaximumPages} worksheets were evaluated.");

        return ReadyResult(
            source,
            safety,
            "xlsx_closedxml_formatted_cells",
            sheetCount,
            sections,
            false,
            warnings);
    }

    private static async Task<PulseAiDocumentExtractionResult> ExtractTextAsync(
        PulseAiAuthorizedDocumentSource source,
        PulseAiDocumentPipelineOptions options,
        PulseAiDocumentSafetyAssessment safety,
        CancellationToken cancellationToken)
    {
        var text = await ReadTextLimitedAsync(source.StoragePath, options.MaximumCharacters, cancellationToken);
        var sections = SplitTextSections(text, options);
        return ReadyResult(
            source,
            safety,
            "private_utf_text_reader",
            0,
            sections,
            false,
            safety.Warnings);
    }

    private static async Task<PulseAiDocumentExtractionResult> ExtractHtmlAsync(
        PulseAiAuthorizedDocumentSource source,
        PulseAiDocumentPipelineOptions options,
        PulseAiDocumentSafetyAssessment safety,
        CancellationToken cancellationToken)
    {
        var html = await ReadTextLimitedAsync(source.StoragePath, options.MaximumCharacters, cancellationToken);
        html = HtmlScriptStyle.Replace(html, " ");
        var text = WebUtility.HtmlDecode(HtmlTags.Replace(html, Environment.NewLine));
        var sections = SplitTextSections(text, options);
        return ReadyResult(
            source,
            safety,
            "private_html_text_normalization",
            0,
            sections,
            false,
            safety.Warnings);
    }

    private static async Task<PulseAiDocumentExtractionResult> ExtractXmlAsync(
        PulseAiAuthorizedDocumentSource source,
        PulseAiDocumentPipelineOptions options,
        PulseAiDocumentSafetyAssessment safety,
        CancellationToken cancellationToken)
    {
        var xmlText = await ReadTextLimitedAsync(source.StoragePath, options.MaximumCharacters, cancellationToken);
        var xml = XDocument.Parse(xmlText, System.Xml.Linq.LoadOptions.PreserveWhitespace);
        var text = string.Join(Environment.NewLine,
            xml.DescendantNodes()
                .OfType<XText>()
                .Select(node => NormalizeInline(node.Value))
                .Where(value => value.Length > 0));
        var sections = SplitTextSections(text, options);
        return ReadyResult(
            source,
            safety,
            "private_xml_text_nodes",
            0,
            sections,
            false,
            safety.Warnings);
    }

    private static IReadOnlyList<PulseAiExtractedSection> SplitTextSections(
        string text,
        PulseAiDocumentPipelineOptions options)
    {
        text = Normalize(text);
        if (text.Length == 0) return [];
        var sections = new List<PulseAiExtractedSection>();
        var parts = Regex.Split(text, "(?:\\r?\\n){2,}");
        var total = 0;
        var buffer = new StringBuilder();

        void Flush()
        {
            var value = Normalize(buffer.ToString());
            buffer.Clear();
            if (value.Length == 0 || sections.Count >= options.MaximumSections) return;
            value = LimitText(value, options.MaximumCharacters - total);
            if (value.Length == 0) return;
            sections.Add(CreateSection(
                sections.Count,
                $"text:section:{sections.Count + 1}",
                $"Section {sections.Count + 1}",
                value,
                null,
                null));
            total += value.Length;
        }

        foreach (var part in parts)
        {
            if (sections.Count >= options.MaximumSections || total >= options.MaximumCharacters) break;
            var clean = Normalize(part);
            if (clean.Length == 0) continue;
            if (buffer.Length > 0 && buffer.Length + clean.Length > options.ChunkCharacters * 2)
                Flush();
            buffer.AppendLine(clean);
        }
        Flush();
        return sections;
    }

    private static PulseAiDocumentExtractionResult ReadyResult(
        PulseAiAuthorizedDocumentSource source,
        PulseAiDocumentSafetyAssessment safety,
        string method,
        int pageCount,
        IReadOnlyList<PulseAiExtractedSection> sections,
        bool ocrRequired,
        IReadOnlyList<string> warnings)
    {
        var characterCount = sections.Sum(section => section.CharacterCount);
        var blockers = new List<string>();
        var status = sections.Count > 0
            ? "extraction_preview_ready"
            : ocrRequired
                ? "ocr_required"
                : "no_extractable_text";
        if (sections.Count == 0)
            blockers.Add(ocrRequired
                ? "No usable text was extracted; private OCR is required."
                : "No usable text was extracted from the admitted document.");

        return new PulseAiDocumentExtractionResult(
            Status: status,
            DocumentId: source.DocumentId,
            OriginalFileName: source.OriginalFileName,
            DetectedFormat: safety.DetectedFormat,
            ExtractionMethod: method,
            PageCount: pageCount,
            SectionCount: sections.Count,
            CharacterCount: characterCount,
            EstimatedTokenCount: EstimateTokens(characterCount),
            OcrRequired: ocrRequired,
            SourceSha256: safety.SourceSha256,
            Safety: safety,
            Sections: sections,
            Warnings: warnings,
            Blockers: blockers,
            GeneratedAt: DateTimeOffset.UtcNow);
    }

    private static PulseAiDocumentExtractionResult EmptyResult(
        PulseAiAuthorizedDocumentSource source,
        PulseAiDocumentSafetyAssessment safety,
        string status,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings,
        DateTimeOffset generatedAt) =>
        new(
            Status: status,
            DocumentId: source.DocumentId,
            OriginalFileName: source.OriginalFileName,
            DetectedFormat: safety.DetectedFormat,
            ExtractionMethod: "not_executed",
            PageCount: 0,
            SectionCount: 0,
            CharacterCount: 0,
            EstimatedTokenCount: 0,
            OcrRequired: false,
            SourceSha256: safety.SourceSha256,
            Safety: safety,
            Sections: [],
            Warnings: warnings,
            Blockers: blockers,
            GeneratedAt: generatedAt);

    private static PulseAiExtractedSection CreateSection(
        int index,
        string anchor,
        string title,
        string text,
        int? pageNumber,
        string? sheetName)
    {
        text = Normalize(text);
        return new PulseAiExtractedSection(
            SectionIndex: index,
            Anchor: anchor,
            Title: string.IsNullOrWhiteSpace(title) ? $"Section {index + 1}" : title.Trim(),
            Text: text,
            PageNumber: pageNumber,
            SheetName: sheetName,
            CharacterCount: text.Length,
            TextSha256: Sha256(text));
    }

    private static bool InspectArchiveRisk(
        string path,
        PulseAiDocumentPipelineOptions options,
        List<string> warnings)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.Entries.Count > 20_000)
            {
                warnings.Add("Open XML package contains an unusually large number of entries.");
                return true;
            }

            long compressed = 0;
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                compressed += Math.Max(1, entry.CompressedLength);
                expanded += entry.Length;
                if (entry.Length > options.MaximumFileBytes * 4) return true;
                if (expanded > options.MaximumFileBytes * 12) return true;
            }

            var ratio = compressed == 0 ? 0 : (decimal)expanded / compressed;
            if (ratio > 250)
            {
                warnings.Add($"Open XML expansion ratio {ratio:F1} exceeds the safe threshold.");
                return true;
            }
            return false;
        }
        catch
        {
            warnings.Add("Open XML archive inspection failed.");
            return true;
        }
    }

    private static string DetectFormat(byte[] header, string extension)
    {
        if (header.Length >= 5
            && header[0] == (byte)'%'
            && header[1] == (byte)'P'
            && header[2] == (byte)'D'
            && header[3] == (byte)'F'
            && header[4] == (byte)'-') return "pdf";
        if (header.Length >= 4
            && header[0] == (byte)'P'
            && header[1] == (byte)'K'
            && header[2] == 3
            && header[3] == 4) return "zip_openxml";
        if (header.Any(value => value == 0)) return "binary_unknown";
        if (extension is ".txt" or ".md" or ".csv" or ".json" or ".xml" or ".html" or ".htm")
            return "text";
        return "unknown";
    }

    private static bool SignatureMatches(string extension, string detected) => extension switch
    {
        ".pdf" => detected == "pdf",
        ".docx" or ".pptx" or ".xlsx" => detected == "zip_openxml",
        ".txt" or ".md" or ".csv" or ".json" or ".xml" or ".html" or ".htm" => detected == "text",
        _ => false
    };

    private static async Task<string> ReadTextLimitedAsync(
        string path,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 64 * 1024,
            leaveOpen: false);
        var buffer = new char[Math.Min(64 * 1024, maximumCharacters)];
        var result = new StringBuilder(Math.Min(maximumCharacters, 256 * 1024));
        while (result.Length < maximumCharacters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requested = Math.Min(buffer.Length, maximumCharacters - result.Length);
            var read = await reader.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
            if (read == 0) break;
            result.Append(buffer, 0, read);
        }
        return Normalize(result.ToString());
    }

    private static int FindNaturalBoundary(string text, int start, int desiredEnd)
    {
        if (desiredEnd >= text.Length) return text.Length;
        var minimum = start + (int)((desiredEnd - start) * 0.65);
        for (var index = desiredEnd; index > minimum; index--)
        {
            if (text[index - 1] is '\n' or '.' or ';' or ':' or ' ')
                return index;
        }
        return desiredEnd;
    }

    private static string LimitText(string text, int remaining)
    {
        if (remaining <= 0) return string.Empty;
        return text.Length <= remaining ? text : text[..remaining];
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = value.Replace("\0", string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        text = MultiSpace.Replace(text, " ");
        text = ExcessBlankLines.Replace(text, "\n\n");
        return text.Trim();
    }

    private static string NormalizeInline(string? value) =>
        MultiSpace.Replace((value ?? string.Empty).Replace("\0", string.Empty), " ").Trim();

    private static int EstimateTokens(int characters) =>
        characters <= 0 ? 0 : (int)Math.Ceiling(characters / 4.0);

    private static int NumericSuffix(string value)
    {
        var match = Regex.Match(value, "([0-9]+)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : int.MaxValue;
    }

    private static string SafeAnchor(string value)
    {
        var normalized = Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return normalized.Length == 0 ? "unnamed" : normalized[..Math.Min(80, normalized.Length)];
    }

    private static string DeterministicChunkId(
        Guid documentId,
        string sourceSha256,
        string anchor,
        int chunkIndex,
        string textSha256)
    {
        var digest = Sha256($"{documentId:N}|{sourceSha256}|{anchor}|{chunkIndex}|{textSha256}");
        return $"pulse-chunk-{digest[..32]}";
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static async Task<string> Sha256FileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Diagnostic(Exception exception) => exception switch
    {
        InvalidDataException => "invalid_document_package",
        UnauthorizedAccessException => "document_access_denied",
        IOException => "document_io_failure",
        NotSupportedException => "document_format_not_supported",
        _ => "document_extraction_failure"
    };
}
