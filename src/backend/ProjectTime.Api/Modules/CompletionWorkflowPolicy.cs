namespace ProjectTime.Api.Modules;

// These are evidence receipts, not Certinia API responses or payment records.
public sealed record CompletionEvidenceReceipt(
    Guid ReceiptId, DateOnly OccurredOn, string Reference, string Evidence,
    string Party, string Notes, Guid RecordedBy, DateTimeOffset RecordedAt,
    string BasisFingerprint, string Scope, Guid? RelatedReceiptId,
    Guid[] CoveredInvoiceIds);

public sealed record CompletionWorkflowState(
    long Revision = 0,
    CompletionEvidenceReceipt? Delivery = null,
    CompletionEvidenceReceipt? Acceptance = null,
    CompletionEvidenceReceipt? Sent = null,
    CompletionEvidenceReceipt? Billed = null);

public sealed record CompletionWorkflowRequest(
    long ExpectedRevision, string? ExpectedBasisFingerprint, Guid OperationId,
    bool Confirmed, DateOnly OccurredOn, string? Reference, string? Evidence,
    string? Party, string? Notes, string? Scope, string? Reason, Guid[]? CoveredInvoiceIds = null);

public sealed record CompletionBillingBasis(
    string Fingerprint, long PendingTimeCount, long OpenTransmissionCount,
    bool AutomatedFinalDelivered, bool AllInvoicesDelivered, Guid[] InvoiceIds)
{
    public CompletionInvoiceReference[] Invoices { get; init; } = [];
}

public sealed record CompletionInvoiceReference(Guid InvoiceId, string InvoiceNumber);

public static class CompletionWorkflowPolicy
{
    public static readonly string[] Actions =
        ["delivery", "acceptance", "sent", "billed", "reopen_delivery", "reopen_billing"];

    public static bool IsAccepted(CompletionWorkflowState state) =>
        state.Delivery is not null && state.Acceptance is not null
        && state.Acceptance.Scope == "accepted"
        && state.Acceptance.RelatedReceiptId == state.Delivery.ReceiptId;

    public static bool IsFullyBilled(CompletionWorkflowState state, CompletionBillingBasis basis) =>
        state.Billed is not null
        && state.Billed.BasisFingerprint == basis.Fingerprint
        && basis.PendingTimeCount == 0 && basis.OpenTransmissionCount == 0
        && (state.Billed.Scope == "manual"
            ? state.Sent is not null && state.Sent.Scope == "final"
                && state.Sent.BasisFingerprint == basis.Fingerprint
                && state.Billed.RelatedReceiptId == state.Sent.ReceiptId
            : state.Billed.Scope == "connected"
                && basis.AutomatedFinalDelivered && basis.AllInvoicesDelivered);

    public static bool HasCurrentManualReconciliation(CompletionWorkflowState state, CompletionBillingBasis basis) =>
        state.Billed?.Scope == "manual" && IsFullyBilled(state, basis);

    public static string? Validate(
        string action, CompletionWorkflowRequest request, CompletionWorkflowState state,
        CompletionBillingBasis basis, DateOnly today)
    {
        if (!Actions.Contains(action)) return "Choose a supported checklist action.";
        if (request.OperationId == Guid.Empty || request.ExpectedRevision < 0)
            return "An operation ID and nonnegative evidence revision are required.";
        if (!request.Confirmed) return "Select the confirmation checkbox before saving.";
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 5 or > 500)
            return "Enter a specific audit reason between 5 and 500 characters.";
        if (action.StartsWith("reopen_", StringComparison.Ordinal)) return null;
        if (request.OccurredOn == default || request.OccurredOn > today)
            return "Record the actual date, not a future date.";
        if (string.IsNullOrWhiteSpace(request.Reference) || request.Reference.Trim().Length is < 2 or > 500)
            return "Enter a delivery/package/invoice reference between 2 and 500 characters.";
        if (string.IsNullOrWhiteSpace(request.Evidence) || request.Evidence.Trim().Length is < 5 or > 2000)
            return "Record a supporting document, email or ticket reference between 5 and 2000 characters.";
        if ((request.Notes?.Length ?? 0) > 2000 || (request.Party?.Length ?? 0) > 200)
            return "Notes are limited to 2000 characters and the customer representative to 200.";
        if (action == "acceptance")
        {
            if (state.Delivery is null) return "Record delivery completion before customer acceptance.";
            if (request.Scope is not ("accepted" or "conditional" or "rejected"))
                return "Choose accepted, accepted with conditions, or corrections requested.";
            if (string.IsNullOrWhiteSpace(request.Party)) return "Identify the customer representative who provided the decision.";
            if (request.OccurredOn < state.Delivery.OccurredOn)
                return "Acceptance cannot precede the recorded delivery completion date.";
        }
        if (action is "sent" or "billed")
        {
            if (request.ExpectedBasisFingerprint != basis.Fingerprint)
                return "The project's billing evidence changed. Refresh and reconcile the current charges before confirming.";
            if (basis.OpenTransmissionCount > 0)
                return "A Certinia delivery is queued, processing or retryable. Resolve that delivery before recording manual billing to avoid duplicates.";
        }
        if (action == "sent" && (request.CoveredInvoiceIds ?? []).Any(id => !basis.InvoiceIds.Contains(id)))
            return "A selected invoice does not belong to the current project. Refresh and review the package.";
        if (action == "acceptance" && (request.Scope is "conditional" or "rejected") && string.IsNullOrWhiteSpace(request.Notes))
            return "Record the outstanding conditions or requested corrections in Notes.";
        if (action == "sent" && request.Scope is not ("partial" or "final"))
            return "Specify whether the transmitted package is partial or final.";
        if (action == "billed")
        {
            if (basis.PendingTimeCount > 0) return "Resolve pending billable time approvals before confirming fully billed.";
            var manualFinal = state.Sent is not null && state.Sent.Scope == "final"
                && state.Sent.BasisFingerprint == basis.Fingerprint;
            if (!manualFinal && !(basis.AutomatedFinalDelivered && basis.AllInvoicesDelivered))
                return "Record the current final package sent to Certinia, or complete connected delivery of all invoices, before confirming fully billed. A partial package is not final billing.";
            if (manualFinal && request.OccurredOn < state.Sent!.OccurredOn)
                return "Billing completion cannot precede the final package's sent date.";
        }
        return null;
    }
}
