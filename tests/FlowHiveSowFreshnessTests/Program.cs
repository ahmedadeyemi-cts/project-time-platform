using ProjectTime.Api.Modules;

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException($"ASSERTION_FAILED {label}");
    Console.WriteLine($"ASSERTION_PASSED {label}");
}

var oldReadyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
var newestPendingId = Guid.Parse("22222222-2222-2222-2222-222222222222");
var oldFailedId = Guid.Parse("33333333-3333-3333-3333-333333333333");
var newestReadyId = Guid.Parse("44444444-4444-4444-4444-444444444444");
var additionalReadyId = Guid.Parse("55555555-5555-5555-5555-555555555555");

var olderReadyNewerPending = ProjectFlowHiveSowFreshnessPolicy.Evaluate(
[
    new ProjectFlowHiveSowFreshnessEvidence(
        oldReadyId,
        "Customer-SOW.doc",
        "sow",
        DateTimeOffset.Parse("2026-08-01T12:00:00Z"),
        true),
    new ProjectFlowHiveSowFreshnessEvidence(
        newestPendingId,
        "Customer-SOW.doc",
        "statement_of_work",
        DateTimeOffset.Parse("2026-08-18T12:00:00Z"),
        false)
]);
Assert(olderReadyNewerPending.PendingReplacements.Count == 1,
    "newer_pending_same_name_blocks_older_ready_scope");
Assert(olderReadyNewerPending.PendingReplacements[0].NewestDocumentId == newestPendingId,
    "newest_document_identified_by_upload_chronology");
Assert(olderReadyNewerPending.PendingReplacements[0].OlderReadyDocumentIds.SequenceEqual([oldReadyId]),
    "older_ready_document_recorded_as_stale_lineage");
Assert(!olderReadyNewerPending.CurrentSowDocumentIds.Contains(oldReadyId),
    "older_ready_document_not_current_while_replacement_pending");

var olderFailedNewerReady = ProjectFlowHiveSowFreshnessPolicy.Evaluate(
[
    new ProjectFlowHiveSowFreshnessEvidence(
        oldFailedId,
        "Customer-SOW.doc",
        "sow",
        DateTimeOffset.Parse("2026-07-15T12:00:00Z"),
        false),
    new ProjectFlowHiveSowFreshnessEvidence(
        newestReadyId,
        "Customer-SOW.doc",
        "sow",
        DateTimeOffset.Parse("2026-08-19T12:00:00Z"),
        true)
]);
Assert(olderFailedNewerReady.PendingReplacements.Count == 0,
    "older_failed_upload_does_not_block_newer_ready_sow");
Assert(
    olderFailedNewerReady.CurrentSowDocumentIds.Count == 1
    && olderFailedNewerReady.CurrentSowDocumentIds.Contains(newestReadyId),
    "newer_ready_document_is_current_authority");

var independentReadyDocuments = ProjectFlowHiveSowFreshnessPolicy.Evaluate(
[
    new ProjectFlowHiveSowFreshnessEvidence(
        newestReadyId,
        "Customer-SOW.doc",
        "sow",
        DateTimeOffset.Parse("2026-08-19T12:00:00Z"),
        true),
    new ProjectFlowHiveSowFreshnessEvidence(
        additionalReadyId,
        "Customer-Change-Order-SOW.pdf",
        "statement_of_work",
        DateTimeOffset.Parse("2026-08-20T12:00:00Z"),
        true)
]);
Assert(independentReadyDocuments.PendingReplacements.Count == 0,
    "independent_sow_filenames_do_not_block_each_other");
Assert(
    independentReadyDocuments.CurrentSowDocumentIds.Count == 2
    && independentReadyDocuments.CurrentSowDocumentIds.Contains(newestReadyId)
    && independentReadyDocuments.CurrentSowDocumentIds.Contains(additionalReadyId),
    "each_independent_sow_lineage_retains_its_newest_ready_document");

var duplicateRows = ProjectFlowHiveSowFreshnessPolicy.Evaluate(
[
    new ProjectFlowHiveSowFreshnessEvidence(
        newestReadyId,
        "Customer-SOW.doc",
        "sow",
        DateTimeOffset.Parse("2026-08-19T12:00:00Z"),
        false),
    new ProjectFlowHiveSowFreshnessEvidence(
        newestReadyId,
        "Customer-SOW.doc",
        "sow",
        DateTimeOffset.Parse("2026-08-19T12:00:00Z"),
        true)
]);
Assert(duplicateRows.PendingReplacements.Count == 0,
    "duplicate_rows_for_one_document_do_not_create_false_replacement");
Assert(
    duplicateRows.CurrentSowDocumentIds.Count == 1
    && duplicateRows.CurrentSowDocumentIds.Contains(newestReadyId),
    "strongest_duplicate_row_preserves_ready_document");

Console.WriteLine("FLOWHIVE_SOW_FRESHNESS_TESTS=PASS");
