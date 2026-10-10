using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectTime.Api.Ai;

internal static class SecurityDocumentScanTests
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "pulse-scan-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "source.txt");
        var checks = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
            checks++;
        }
        try
        {
            await File.WriteAllTextAsync(file, "Ordinary document content for a clean extraction fixture.");
            var bytes = await File.ReadAllBytesAsync(file);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var source = new PulseAiAuthorizedDocumentSource(
                DocumentId: Guid.NewGuid(), ProjectId: Guid.NewGuid(), ProjectCode: "TEST",
                ProjectName: "Isolated scan fixture", CustomerName: "Synthetic",
                DocumentType: "sow", DocumentCategory: "scope", OriginalFileName: "source.txt",
                StoredFileName: "source.txt", StoragePath: file, ContentType: "text/plain",
                SizeBytes: bytes.LongLength, EngineeringVisible: true, AiTimesheetContextEnabled: true,
                ExtractionStatus: "queued", ExistingContextSummaryReady: false, ContextLastProcessedAt: null,
                UploadedAt: DateTimeOffset.UtcNow, UploadSource: "test", AccessScope: "project",
                Classification: "internal", RoleCodes: []);
            var options = PulseAiDocumentPipelineOptions.FromEnvironment() with
            {
                UploadRoot = root, ExtractionPreviewEnabled = true, MalwareScanAttested = true,
                MalwareScannerMode = "isolated_scan_fixture"
            };
            var extractor = new PulseAiPrivateDocumentExtractionService(
                NullLogger<PulseAiPrivateDocumentExtractionService>.Instance);
            foreach (var invalid in new[]
            {
                options,
                options with { VerifiedCleanSourceSha256 = new string('0', 64) },
                options with { VerifiedCleanSourceSha256 = hash, MalwareScanAttested = false }
            })
            {
                var blocked = await extractor.ExtractAsync(source, invalid);
                Check(!blocked.ExtractionSucceeded && !blocked.Safety.MalwareScanAttested,
                    "Environment attestation, foreign scan hashes, and non-clean evidence must fail closed");
                Check(blocked.Sections.Count == 0, "Unscanned document content must never reach extraction");
            }
            var approved = options with { VerifiedCleanSourceSha256 = hash };
            var clean = await extractor.ExtractAsync(source, approved);
            Check(clean.ExtractionSucceeded && clean.Safety.MalwareScanAttested,
                "An ordinary file with exact clean-scan evidence remains usable");
            Check(clean.SourceSha256 == hash && clean.Sections.Count > 0, "Extraction is bound to the clean file");
            await File.AppendAllTextAsync(file, "Changed after scan.");
            var stale = await extractor.ExtractAsync(source, approved);
            Check(!stale.ExtractionSucceeded && !stale.Safety.MalwareScanAttested,
                "Changing the file invalidates the earlier clean scan");
            Check(stale.Sections.Count == 0, "Stale scan evidence never authorizes content parsing");
            Console.WriteLine($"SECURITY_DOCUMENT_SCAN_EVIDENCE=PASS assertions={checks}");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
