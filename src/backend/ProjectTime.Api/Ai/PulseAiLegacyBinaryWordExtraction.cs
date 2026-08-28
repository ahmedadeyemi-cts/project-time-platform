using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Private, text-only extraction adapter for legacy Microsoft Word .doc files.
/// Real OLE binary Word documents are processed by a fixed local antiword
/// executable without a shell. Text-compatible .doc documents are processed
/// in-process after the same malware, signature, size, and path-confinement
/// gates. Neither path executes embedded macros or objects, and both preserve
/// the original source checksum and bounded extraction contract.
/// </summary>
public static class PulseAiLegacyBinaryWordExtraction
{
    public const string ContractVersion = "pulse-ai-legacy-binary-word-v2-20260827";
    private const int MaximumErrorCharacters = 4_000;
    private static readonly TimeSpan ExtractionTimeout = TimeSpan.FromSeconds(60);
    private static readonly Regex MultiSpace = new("[ \\t]+", RegexOptions.Compiled);
    private static readonly Regex ExcessBlankLines = new("(?:\\r?\\n){3,}", RegexOptions.Compiled);
    private static readonly Regex HtmlScriptStyle = new(
        "<(script|style)[^>]*>.*?</\\1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex HtmlTags = new(
        "<[^>]+>",
        RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex RtfParagraph = new(
        @"\\(?:par[d]?|line)\b ?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RtfTab = new(
        @"\\tab\b ?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RtfHexEscape = new(
        @"\\'[0-9a-fA-F]{2}",
        RegexOptions.Compiled);
    private static readonly Regex RtfControlWord = new(
        @"\\[a-zA-Z]+-?\d* ?",
        RegexOptions.Compiled);

    public static async Task<PulseAiDocumentExtractionResult> ExtractAsync(
        PulseAiAuthorizedDocumentSource source,
        PulseAiDocumentPipelineOptions options,
        PulseAiDocumentSafetyAssessment safety,
        CancellationToken cancellationToken)
    {
        if (!safety.DetectedFormat.Equals("ole_compound_word", StringComparison.OrdinalIgnoreCase))
        {
            return await ExtractTextCompatibleAsync(
                source,
                options,
                safety,
                cancellationToken);
        }

        var toolPath = ResolveToolPath();
        if (!File.Exists(toolPath))
        {
            return Blocked(
                source,
                safety,
                "legacy_word_extractor_unavailable",
                ["The private legacy Word text extractor is not installed in this API runtime."],
                ["Convert the source to DOCX or PDF, or install the approved local antiword runtime."],
                DateTimeOffset.UtcNow);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = toolPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = options.UploadRoot
        };
        startInfo.ArgumentList.Add(source.StoragePath);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                return Blocked(
                    source,
                    safety,
                    "legacy_word_extractor_start_failed",
                    ["The private legacy Word text extractor could not be started."],
                    [],
                    DateTimeOffset.UtcNow);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ExtractionTimeout);
            var outputTask = ReadBoundedAsync(
                process.StandardOutput,
                options.MaximumCharacters,
                timeout.Token);
            var errorTask = ReadBoundedAsync(
                process.StandardError,
                MaximumErrorCharacters,
                timeout.Token);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return Blocked(
                    source,
                    safety,
                    "legacy_word_extraction_timeout",
                    ["Legacy Word extraction exceeded the private 60-second processing limit."],
                    [],
                    DateTimeOffset.UtcNow);
            }

            var output = Normalize(await outputTask);
            var error = Normalize(await errorTask);
            if (process.ExitCode != 0 || output.Length == 0)
            {
                return Blocked(
                    source,
                    safety,
                    "legacy_word_extraction_failed",
                    ["The local legacy Word extractor could not obtain usable text from this document."],
                    error.Length == 0
                        ? []
                        : ["The extractor reported a bounded diagnostic without returning source document text."],
                    DateTimeOffset.UtcNow);
            }

            var sections = CreateSections(output, options);
            if (sections.Count == 0)
            {
                return Blocked(
                    source,
                    safety,
                    "legacy_word_no_extractable_text",
                    ["The legacy Word document did not contain usable text after private conversion."],
                    [],
                    DateTimeOffset.UtcNow);
            }

            var characterCount = sections.Sum(section => section.CharacterCount);
            var warnings = safety.Warnings
                .Concat(new[]
                {
                    "Legacy binary Word content was converted to bounded plain text by the local antiword adapter; embedded macros or objects were never executed.",
                    "Legacy binary Word formatting is not authoritative. PM and Engineering must review section boundaries and citations before plan adoption."
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Ready(
                source,
                safety,
                sections,
                characterCount,
                "legacy_doc_antiword_text_only",
                warnings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }
        catch
        {
            TryKill(process);
            return Blocked(
                source,
                safety,
                "legacy_word_extraction_failed",
                ["The private legacy Word adapter failed without exposing source text or process output."],
                [],
                DateTimeOffset.UtcNow);
        }
    }

    private static async Task<PulseAiDocumentExtractionResult> ExtractTextCompatibleAsync(
        PulseAiAuthorizedDocumentSource source,
        PulseAiDocumentPipelineOptions options,
        PulseAiDocumentSafetyAssessment safety,
        CancellationToken cancellationToken)
    {
        if (safety.DetectedFormat is not ("legacy_doc_text" or "legacy_doc_html" or "legacy_doc_rtf"))
        {
            return Blocked(
                source,
                safety,
                "legacy_word_signature_not_supported",
                ["The .doc file is neither an approved OLE Word document nor an admitted text-compatible legacy Word document."],
                [],
                DateTimeOffset.UtcNow);
        }

        try
        {
            await using var stream = new FileStream(
                source.StoragePath,
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
            var raw = await ReadBoundedAsync(reader, options.MaximumCharacters, cancellationToken);
            if (!LooksTextual(raw))
            {
                return Blocked(
                    source,
                    safety,
                    "legacy_word_text_compatibility_rejected",
                    ["The admitted text-compatible .doc contained too much binary or undecodable content for safe private text extraction."],
                    [],
                    DateTimeOffset.UtcNow);
            }

            var output = safety.DetectedFormat switch
            {
                "legacy_doc_html" => Normalize(WebUtility.HtmlDecode(
                    HtmlTags.Replace(HtmlScriptStyle.Replace(raw, " "), Environment.NewLine))),
                "legacy_doc_rtf" => NormalizeRtf(raw),
                _ => Normalize(raw)
            };
            if (output.Length == 0)
            {
                return Blocked(
                    source,
                    safety,
                    "legacy_word_no_extractable_text",
                    ["The text-compatible legacy Word document did not contain usable text."],
                    [],
                    DateTimeOffset.UtcNow);
            }

            var sections = CreateSections(output, options);
            if (sections.Count == 0)
            {
                return Blocked(
                    source,
                    safety,
                    "legacy_word_no_extractable_text",
                    ["The text-compatible legacy Word document did not produce citation-preserving sections."],
                    [],
                    DateTimeOffset.UtcNow);
            }

            var characterCount = sections.Sum(section => section.CharacterCount);
            var extractionMethod = safety.DetectedFormat switch
            {
                "legacy_doc_html" => "legacy_doc_private_html_text",
                "legacy_doc_rtf" => "legacy_doc_private_rtf_text",
                _ => "legacy_doc_private_text_reader"
            };
            var warnings = safety.Warnings
                .Concat(new[]
                {
                    "The .doc file uses a non-binary legacy text-compatible representation and was parsed in-process after private malware and path-confinement checks.",
                    "No macro, embedded object, shell, or external provider was executed while extracting this document."
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Ready(
                source,
                safety,
                sections,
                characterCount,
                extractionMethod,
                warnings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Blocked(
                source,
                safety,
                "legacy_word_text_extraction_failed",
                ["The private text-compatible legacy Word adapter failed without exposing source content."],
                [],
                DateTimeOffset.UtcNow);
        }
    }

    private static string NormalizeRtf(string value)
    {
        var text = value;
        text = RtfParagraph.Replace(text, Environment.NewLine);
        text = RtfTab.Replace(text, "\t");
        text = RtfHexEscape.Replace(text, " ");
        text = RtfControlWord.Replace(text, " ");
        text = text
            .Replace("\\{", "{")
            .Replace("\\}", "}")
            .Replace("\\\\", "\\")
            .Replace('{', ' ')
            .Replace('}', ' ');
        return Normalize(text);
    }

    private static bool LooksTextual(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var invalid = 0;
        foreach (var character in value)
        {
            if (character == '\uFFFD'
                || (char.IsControl(character)
                    && character is not '\r' and not '\n' and not '\t'))
            {
                invalid++;
            }
        }

        return invalid <= Math.Max(4, value.Length / 20);
    }

    private static PulseAiDocumentExtractionResult Ready(
        PulseAiAuthorizedDocumentSource source,
        PulseAiDocumentSafetyAssessment safety,
        IReadOnlyList<PulseAiExtractedSection> sections,
        int characterCount,
        string extractionMethod,
        IReadOnlyList<string> warnings) =>
        new(
            Status: "extraction_preview_ready",
            DocumentId: source.DocumentId,
            OriginalFileName: source.OriginalFileName,
            DetectedFormat: safety.DetectedFormat,
            ExtractionMethod: extractionMethod,
            PageCount: 0,
            SectionCount: sections.Count,
            CharacterCount: characterCount,
            EstimatedTokenCount: EstimateTokens(characterCount),
            OcrRequired: false,
            SourceSha256: safety.SourceSha256,
            Safety: safety,
            Sections: sections,
            Warnings: warnings,
            Blockers: [],
            GeneratedAt: DateTimeOffset.UtcNow);

    private static IReadOnlyList<PulseAiExtractedSection> CreateSections(
        string text,
        PulseAiDocumentPipelineOptions options)
    {
        var sections = new List<PulseAiExtractedSection>();
        var paragraphs = Regex.Split(text, "(?:\\r?\\n){2,}")
            .Select(Normalize)
            .Where(value => value.Length > 0)
            .ToArray();
        var buffer = new StringBuilder();
        var totalCharacters = 0;

        void Flush()
        {
            var value = Normalize(buffer.ToString());
            buffer.Clear();
            if (value.Length == 0
                || sections.Count >= options.MaximumSections
                || totalCharacters >= options.MaximumCharacters)
                return;

            var remaining = options.MaximumCharacters - totalCharacters;
            if (value.Length > remaining) value = value[..remaining];
            var firstLine = value
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? string.Empty;
            var title = firstLine.Length is > 0 and <= 140
                ? firstLine
                : $"Legacy Word section {sections.Count + 1}";
            var anchor = $"legacy-doc:section:{sections.Count + 1}";
            sections.Add(new PulseAiExtractedSection(
                SectionIndex: sections.Count,
                Anchor: anchor,
                Title: title,
                Text: value,
                PageNumber: null,
                SheetName: null,
                CharacterCount: value.Length,
                TextSha256: Sha256(value)));
            totalCharacters += value.Length;
        }

        foreach (var paragraph in paragraphs)
        {
            if (sections.Count >= options.MaximumSections
                || totalCharacters >= options.MaximumCharacters)
                break;
            if (buffer.Length > 0
                && buffer.Length + paragraph.Length > options.ChunkCharacters * 2)
                Flush();
            buffer.AppendLine(paragraph);
        }
        Flush();
        return sections;
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        maximumCharacters = Math.Max(0, maximumCharacters);
        var result = new StringBuilder(Math.Min(maximumCharacters, 256 * 1024));
        var buffer = new char[16 * 1024];

        // Continue draining the redirected pipe after the retained-text limit is
        // reached. The same bounded reader is also reused for private file input.
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;

            var remaining = maximumCharacters - result.Length;
            if (remaining <= 0) continue;
            result.Append(buffer, 0, Math.Min(read, remaining));
        }

        return result.ToString();
    }

    private static PulseAiDocumentExtractionResult Blocked(
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
            ExtractionMethod: "legacy_doc_private_text_only",
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

    private static string ResolveToolPath()
    {
        var configured = Environment.GetEnvironmentVariable("PROJECTPULSE_LEGACY_WORD_EXTRACTOR_PATH")?.Trim();
        return string.IsNullOrWhiteSpace(configured)
            ? "/usr/bin/antiword"
            : configured;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort termination. No process output or source text is logged.
        }
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = value.Replace("\0", string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        text = MultiSpace.Replace(text, " ");
        text = ExcessBlankLines.Replace(text, "\n\n");
        return text.Trim();
    }

    private static int EstimateTokens(int characters) =>
        characters <= 0 ? 0 : (int)Math.Ceiling(characters / 4.0);

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}