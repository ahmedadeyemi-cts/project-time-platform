namespace ProjectTime.Api.Modules;

public sealed record ManualInvoiceRequest(
    Guid OperationId, string ExpectedFingerprint, string InvoiceType,
    decimal AgreedTotal, decimal BillToDate, decimal PreviouslyBilledOutsidePulse,
    DateOnly PeriodStart, DateOnly PeriodEnd, string Description,
    string AuthorizationReference, string ExternalBillingReference, string Reason, bool Confirmed,
    string BillingBasis = "", string ProgressReference = "", string ExceptionReason = "",
    string CommercialReference = "", string CommercialFallbackReason = "", Guid? MilestoneId = null);

public static class ManualBillingPolicy
{
    public static string? Validate(ManualInvoiceRequest request, decimal pulseInvoiced,
        decimal priorExternal, bool finalExists, DateOnly today)
    {
        if (request.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(request.ExpectedFingerprint))
            return "Reload the billing balance before creating an invoice.";
        if (!request.Confirmed) return "Confirm the agreed amount and reconciliation of prior invoices.";
        if (request.InvoiceType is not ("partial" or "final")) return "Choose a partial or final invoice.";
        if (finalExists) return "A final invoice is already recorded. Review its history before authorizing additional charges.";
        foreach (var amount in new[] { request.AgreedTotal, request.BillToDate, request.PreviouslyBilledOutsidePulse })
            if (amount < 0 || amount > 999999999999.99m || decimal.Round(amount, 2) != amount)
                return "Amounts must be nonnegative USD amounts with at most two decimal places.";
        if (request.AgreedTotal <= 0 || request.BillToDate > request.AgreedTotal)
            return "The cumulative billing amount must not exceed the agreed project total.";
        if (request.PreviouslyBilledOutsidePulse < priorExternal)
            return "Previously recorded external billing cannot be reduced. Resolve corrections with Billing before creating another invoice.";
        if (request.BillToDate <= pulseInvoiced + request.PreviouslyBilledOutsidePulse)
            return "Nothing remains to invoice at this billing amount. Prior invoices are deducted, not billed again.";
        if (request.InvoiceType == "partial" && request.BillToDate == request.AgreedTotal)
            return "Use full/final billing when charging the entire agreed total.";
        if (request.InvoiceType == "final" && request.BillToDate != request.AgreedTotal)
            return "A full/final invoice must bill the remaining balance of the agreed project total.";
        if (request.PeriodStart == default || request.PeriodEnd < request.PeriodStart || request.PeriodEnd > today)
            return "Enter a valid billing period ending today or earlier. Describe any advance billing in the invoice description.";
        if (!ValidText(request.Description, 5, 2000) || !ValidText(request.AuthorizationReference, 2, 500)
            || !ValidText(request.Reason, 5, 500))
            return "Provide a description, approved SOW/PO or billing authorization reference, and an audit reason.";
        if (request.PreviouslyBilledOutsidePulse > 0 && !ValidText(request.ExternalBillingReference, 2, 1000))
            return "Provide the references for amounts billed outside Pulse, excluding invoices already in Pulse.";
        if ((request.ExternalBillingReference?.Length ?? 0) > 1000) return "External billing references are limited to 1000 characters.";
        return null;
    }
    public static string? ValidateEligibility(ManualInvoiceRequest request, bool fixedPrice,
        long submittedTimeCount, long pendingTimeCount, bool deliveryComplete, bool canApproveException,
        bool sellAvailable)
    {
        if (!fixedPrice) return "Manual project-amount billing is limited to fixed-price projects. Use approved time or governed billing packages for other contracts.";
        if (!ValidText(request.CommercialReference, 2, 500))
            return "Reference the approved commercial document and version used to verify the project total.";
        if (!sellAvailable && !ValidText(request.CommercialFallbackReason, 5, 500))
            return "Explain the verified saved or manual commercial information used while SELL is unavailable.";
        if (!ValidText(request.ProgressReference, 5, 1000))
            return "Provide the milestone, progress, completion or advance-billing instruction supporting this invoice.";
        if (request.BillingBasis is not ("progress" or "completion" or "exception"))
            return "Choose authorized progress, completed delivery, or an explicit billing exception.";
        if (request.BillingBasis == "exception")
        {
            if (!canApproveException) return "Billing, Finance, Accounting or an administrator must authorize a billing exception in their own session.";
            if (!ValidText(request.ExceptionReason, 10, 1000)) return "Document why billing is authorized before time or delivery is complete.";
            return null;
        }
        if (submittedTimeCount == 0)
            return "No time has been submitted. Resolve missing submissions or have Billing authorize a documented fixed-price exception.";
        if (request.InvoiceType == "final" && (pendingTimeCount > 0 || !deliveryComplete || request.BillingBasis != "completion"))
            return "Full billing requires recorded delivery completion and reviewed time, or a documented billing exception.";
        if (request.BillingBasis == "completion" && !deliveryComplete)
            return "Record delivery completion first, or select authorized partial progress or a billing exception.";
        return null;
    }

    private static bool ValidText(string? value, int min, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= min && value.Length <= max;
}
