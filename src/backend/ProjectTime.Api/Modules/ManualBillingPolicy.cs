namespace ProjectTime.Api.Modules;

public sealed record ManualInvoiceRequest(
    Guid OperationId, string ExpectedFingerprint, string InvoiceType,
    decimal AgreedTotal, decimal BillToDate, decimal PreviouslyBilledOutsidePulse,
    DateOnly PeriodStart, DateOnly PeriodEnd, string Description,
    string AuthorizationReference, string ExternalBillingReference, string Reason, bool Confirmed);

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
    private static bool ValidText(string? value, int min, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= min && value.Length <= max;
}
