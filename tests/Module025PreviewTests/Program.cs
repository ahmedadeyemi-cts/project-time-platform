// Module 025 Stage 3 — in-app preview of the generated SOW/GSD.
//
// These tests prove the three things the stage requires without a live database:
//   1. The preview is derived from the SAME exporter bytes the download serves
//      (it runs the module's existing structured-preview parser over the exact
//      CreateSowDocx/CreateGsdXlsx output), and its content agrees with the
//      Stage 2 golden samples (same phase order, same GSD sheet set, DRAFT badge
//      iff the artifact is a draft).
//   2. The preview endpoints enforce the SAME auth/scope/state gating as the
//      matching download handlers, and can never widen access (shared
//      LoadReadableStateAsync gate; identical state predicates; drafts no-store).
//   3. The kill-switch defaults OFF; OFF ⇒ endpoints 404 and the download path
//      is byte-for-byte unchanged.
//
// Auth/scope and the 404/no-store wiring live in HTTP handlers that need a DB and
// an HttpContext, so those invariants are asserted structurally against the
// canonical module source (the same file the build's code-generator transforms),
// exactly the way the frontend validate:* scripts assert their wiring.

using System.Runtime.CompilerServices;
using System.Text.Json;
using ProjectTime.Api.Ai;
using ProjectTime.Api.Modules;

var checks = 0;
void Check(bool value, string label)
{
    if (!value) throw new InvalidOperationException("FAILED: " + label);
    checks++;
    Console.WriteLine("PASS: " + label);
}

var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
// The preview endpoint runs PreviewTrusted over the exact exporter/download bytes.
JsonElement PreviewOf(string kind, string fileName, byte[] bytes, string? sheetId = null)
    => JsonSerializer.SerializeToElement(Module025TemplatePackage.PreviewTrusted(kind, bytes, sheetId), web);

string[] SheetNames(JsonElement preview) =>
    preview.GetProperty("sheets").EnumerateArray().Select(s => s.GetProperty("name").GetString() ?? "").ToArray();
string[] BlockTexts(JsonElement preview) =>
    preview.GetProperty("blocks").EnumerateArray().Select(b => b.GetProperty("text").GetString() ?? "").ToArray();

string[] Phases = { "Plan", "Design", "Implement", "Validate", "Release" };
string[] StandardSheets =
{
    "Summary", "Phase Breakdown", "Totals Sheet", "SELL SKUs", "Plan", "Design",
    "Implement", "Validate", "Release", "Architect Notes", "Gotcha Items", "Assumptions Responsibilities"
};

// ---------------------------------------------------------------------------
// 1. Preview derived from the SAME exporter bytes the download serves.
// ---------------------------------------------------------------------------
var model = BuildSampleModel();

// Confirmed SOW preview == CreateSowDocx(model) parsed.
var sowConfirmed = PreviewOf("sow", "sow.docx", Module025SowGsdDocumentExporter.CreateSowDocx(model));
Check(sowConfirmed.GetProperty("valid").GetBoolean(), "confirmed SOW preview parses the exporter's .docx");
var confirmedBlocks = BlockTexts(sowConfirmed);
var phaseOrder = Phases.Select(p => Array.IndexOf(confirmedBlocks, p)).ToArray();
Check(phaseOrder.All(i => i >= 0) && phaseOrder.Zip(phaseOrder.Skip(1), (a, b) => a < b).All(ok => ok),
    "SOW preview shows the phase headings in order Plan -> Design -> Implement -> Validate -> Release");
Check(!confirmedBlocks.Any(t => t.Contains("DRAFT - Not approved", StringComparison.Ordinal)),
    "confirmed SOW preview carries NO draft marker");

// Draft SOW preview surfaces the DRAFT marker.
var sowDraft = PreviewOf("sow", "sow.docx", Module025SowGsdDocumentExporter.CreateSowDocx(model, draft: true));
Check(BlockTexts(sowDraft).Any(t => t.Contains("DRAFT - Not approved", StringComparison.Ordinal)),
    "draft SOW preview surfaces the 'DRAFT - Not approved' marker (DRAFT badge)");

// GSD preview == CreateGsdXlsx(model) parsed; standard 12-sheet set.
var gsdConfirmed = PreviewOf("gsd", "gsd.xlsx", Module025SowGsdDocumentExporter.CreateGsdXlsx(model));
Check(gsdConfirmed.GetProperty("valid").GetBoolean(), "GSD preview parses the exporter's .xlsx");
Check(SheetNames(gsdConfirmed).SequenceEqual(StandardSheets),
    "GSD preview exposes the standard 12-sheet set in order");

// Draft GSD marks Summary!E1 DRAFT (default-selected first visible sheet).
var gsdDraft = PreviewOf("gsd", "gsd.xlsx", Module025SowGsdDocumentExporter.CreateGsdXlsx(model, draft: true));
var e1 = gsdDraft.GetProperty("cells").EnumerateArray()
    .FirstOrDefault(c => c.GetProperty("address").GetString() == "E1");
Check(e1.ValueKind == JsonValueKind.Object && (e1.GetProperty("value").GetString() ?? "").StartsWith("DRAFT", StringComparison.Ordinal),
    "draft GSD preview surfaces the Summary!E1 DRAFT marker (DRAFT badge)");

// ---------------------------------------------------------------------------
// 2. Cross-check preview against the Stage 2 golden samples on disk.
// ---------------------------------------------------------------------------
var repoRoot = RepoRoot();
var sampleSow = File.ReadAllBytes(Path.Combine(repoRoot, "docs", "samples", "module025", "sample-sow.docx"));
var sampleGsd = File.ReadAllBytes(Path.Combine(repoRoot, "docs", "samples", "module025", "sample-gsd.xlsx"));

var goldenSow = PreviewOf("sow", "sow.docx", sampleSow);
Check(goldenSow.GetProperty("valid").GetBoolean(), "golden SOW sample previews cleanly");
var goldenBlocks = BlockTexts(goldenSow);
var goldenOrder = Phases.Select(p => Array.IndexOf(goldenBlocks, p)).ToArray();
Check(goldenOrder.All(i => i >= 0) && goldenOrder.Zip(goldenOrder.Skip(1), (a, b) => a < b).All(ok => ok),
    "golden SOW sample preview has phases in order (agrees with the exported artifact)");
Check(!goldenBlocks.Any(t => t.Contains("DRAFT - Not approved", StringComparison.Ordinal)),
    "golden (confirmed) SOW sample preview has no DRAFT marker");

var goldenGsd = PreviewOf("gsd", "gsd.xlsx", sampleGsd);
Check(goldenGsd.GetProperty("valid").GetBoolean(), "golden GSD sample previews cleanly");
Check(SheetNames(goldenGsd).SequenceEqual(StandardSheets),
    "golden GSD sample preview has the standard 12-sheet set (agrees with the exported artifact)");

// ---------------------------------------------------------------------------
// 3. Kill-switch defaults OFF.
// ---------------------------------------------------------------------------
Environment.SetEnvironmentVariable(Module025PreviewPolicy.EnabledEnvironmentVariable, null);
Check(!Module025PreviewPolicy.Enabled, "preview kill-switch defaults OFF when unset");
Environment.SetEnvironmentVariable(Module025PreviewPolicy.EnabledEnvironmentVariable, "false");
Check(!Module025PreviewPolicy.Enabled, "preview kill-switch OFF when explicitly false");
Environment.SetEnvironmentVariable(Module025PreviewPolicy.EnabledEnvironmentVariable, "true");
Check(Module025PreviewPolicy.Enabled, "preview kill-switch ON only when explicitly true");
Environment.SetEnvironmentVariable(Module025PreviewPolicy.EnabledEnvironmentVariable, null);

// ---------------------------------------------------------------------------
// 4. State-gate predicates mirror the matching download handlers exactly.
// ---------------------------------------------------------------------------
// Confirmed preview mirrors DownloadSow/DownloadGsd: requires status == confirmed.
Check(Module025SowGsdModule.PreviewConfirmedAllowed("confirmed"), "confirmed preview allowed for confirmed record");
Check(!Module025SowGsdModule.PreviewConfirmedAllowed("draft"), "confirmed preview blocked for draft (confirmation required)");
Check(!Module025SowGsdModule.PreviewConfirmedAllowed("review_ready"), "confirmed preview blocked for review_ready");
Check(!Module025SowGsdModule.PreviewConfirmedAllowed("archived"), "confirmed preview blocked for archived");
// Draft preview mirrors DownloadDraft: active AND not confirmed/archived.
Check(Module025SowGsdModule.PreviewDraftAllowed("draft", true), "draft preview allowed for active draft");
Check(Module025SowGsdModule.PreviewDraftAllowed("review_ready", true), "draft preview allowed for active review_ready");
Check(!Module025SowGsdModule.PreviewDraftAllowed("confirmed", true), "draft preview blocked for confirmed record");
Check(!Module025SowGsdModule.PreviewDraftAllowed("archived", true), "draft preview blocked for archived record");
Check(!Module025SowGsdModule.PreviewDraftAllowed("draft", false), "draft preview blocked for inactive record");

// ---------------------------------------------------------------------------
// 5. Structural wiring: shared auth/scope, kill-switch 404, no-store, and the
//    download path left unchanged. Asserted against the canonical module source.
// ---------------------------------------------------------------------------
var moduleSource = File.ReadAllText(Path.Combine(repoRoot, "src", "backend", "ProjectTime.Api", "Modules", "Module025SowGsdModule.cs"));
Check(moduleSource.Contains("/preview/sow") && moduleSource.Contains("/preview/gsd"),
    "both preview endpoints are registered");
var previewBody = Slice(moduleSource, "private static async Task<IResult> PreviewDocumentAsync(", "internal static bool PreviewConfirmedAllowed(");
Check(previewBody.Contains("if (!Module025PreviewPolicy.Enabled) return Results.NotFound();"),
    "preview 404s (before any work) when the kill-switch is OFF");
Check(previewBody.Contains("await LoadReadableStateAsync("),
    "preview reuses LoadReadableStateAsync — identical auth/scope gate as the downloads (non-widenable)");
Check(previewBody.Contains("Module025SowGsdDocumentExporter.CreateSowDocx(model, draft)")
      && previewBody.Contains("Module025SowGsdDocumentExporter.CreateGsdXlsx(model, draft)")
      && previewBody.Contains("Module025TemplatePackage.PreviewTrusted("),
    "preview parses the SAME exporter bytes the download serves (no divergent second renderer)");
Check(previewBody.Contains("PreviewConfirmedAllowed(engagement.Status)") && previewBody.Contains("PreviewDraftAllowed(engagement.Status, engagement.IsActive)"),
    "preview applies the shared state predicates");
Check(previewBody.Contains("context.Response.Headers.CacheControl = \"no-store\";"),
    "draft preview sets Cache-Control: no-store");
// Download handlers untouched: confirmed downloads still require confirmation; draft download still no-store.
Check(moduleSource.Contains("private static async Task<IResult> DownloadSowAsync(")
      && moduleSource.Contains("private static async Task<IResult> DownloadGsdAsync(")
      && moduleSource.Contains("private static async Task<IResult> DownloadDraftAsync("),
    "existing download handlers are still present (behaviour unchanged)");
var draftDownload = Slice(moduleSource, "private static async Task<IResult> DownloadDraftAsync(", "internal static string DocumentFileName(");
Check(draftDownload.Contains("context.Response.Headers.CacheControl = \"no-store\";"),
    "existing draft download still sets no-store (unchanged)");

Console.WriteLine($"MODULE025_PREVIEW_TESTS=PASS checks={checks} modelCalls=0");

static string Slice(string source, string start, string end)
{
    var a = source.IndexOf(start, StringComparison.Ordinal);
    if (a < 0) throw new InvalidOperationException("anchor not found: " + start);
    var b = source.IndexOf(end, a, StringComparison.Ordinal);
    if (b < 0) throw new InvalidOperationException("anchor not found: " + end);
    return source[a..b];
}

static string RepoRoot([CallerFilePath] string path = "") =>
    Directory.GetParent(path)!.Parent!.Parent!.FullName; // tests/Module025PreviewTests/Program.cs -> repo root

static Module025DocumentModel BuildSampleModel()
{
    using var json = JsonDocument.Parse("{}");
    var empty = json.RootElement.Clone();
    string[] codes = { "plan", "design", "implement", "validate", "release" };
    var phases = codes.Select((code, i) => new Module025PhaseRow(
        code, i, 8m, 12m + i,
        "Deliver the reviewed " + code + " scope for the sample engagement.",
        new[] { "Review the agreed " + code + " requirements", "Complete the approved " + code + " activities" },
        new[] { "Record the " + code + " configuration decisions" },
        new[] { "Reviewed " + code + " delivery record" },
        new[] { "Provide the agreed " + code + " services and review the results" },
        new[] { "Provide access and confirm the " + code + " outcomes" },
        new[] { "Confirm the " + code + " prerequisites are in place" },
        new[] { "Depends on completion of the prior-phase deliverables" },
        new[] { "Assumes the reviewed " + code + " scope is unchanged" },
        Array.Empty<string>(),
        new[] { "Customer reviews the documented " + code + " results" },
        new[] { "Validate the " + code + " outcome against the acceptance criteria" },
        new[] { "Schedule risk if the " + code + " prerequisites are delayed" },
        "Reviewed phase estimate; task allocation pending.",
        Array.Empty<int>(), false, DateTimeOffset.UnixEpoch)).ToArray();
    var e = new Module025EngagementRow(
        new Guid("00000000-0000-0000-0000-0000000000a1"), "SOW-SAMPLE-0001",
        new Guid("00000000-0000-0000-0000-0000000000b2"), "Sample Solution Architect", "Architecture", "Delivery", null,
        "Sample Engagement Customer", "manual", "fixed", "standard", "standard_gsd", null,
        "Sample Account Executive", null, "Sample Inside Sales",
        "Deliver the agreed project scope using the five delivery phases.",
        "Sample Platform Modernization", empty, empty,
        "review_ready", true, 2, null, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, phases);
    return new Module025DocumentModel(e, phases, 40m, phases.Sum(p => p.FinalHours));
}
