using System.Text.Json;

namespace ProjectTime.Api.Modules;

internal sealed record EnterpriseNotificationPolicyRow(
    Guid PolicyId,
    string PolicyCode,
    string PolicyName,
    string Category,
    string SourceModule,
    string EventCode,
    string TriggerMode,
    string RecipientStrategy,
    JsonElement TriggerConfiguration,
    JsonElement RecipientConfiguration,
    string Severity,
    string DeliveryBoundary,
    bool AcknowledgementRequired,
    int? AcknowledgementEscalationMinutes,
    string SubjectTemplate,
    string TextTemplate,
    string OwnerModule,
    string ProducerContract,
    string SourceState,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record EnterpriseNotificationEventRow(
    Guid EventId,
    string PolicyCode,
    string SourceModule,
    string SourceEventId,
    string IdempotencyKey,
    string EntityType,
    Guid? EntityId,
    Guid? ProjectId,
    Guid? SubjectUserId,
    DateTimeOffset OccurredAt,
    DateTimeOffset AvailableAt,
    JsonElement Payload,
    string IngestionSource,
    string EventStatus,
    Guid? DispatchId,
    int AttemptCount,
    string LastErrorCode,
    string LastErrorMessage,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record EnterpriseNotificationEventRequest(
    string? PolicyCode,
    string? SourceModule,
    string? SourceEventId,
    string? IdempotencyKey,
    string? EntityType,
    Guid? EntityId,
    Guid? ProjectId,
    Guid? SubjectUserId,
    DateTimeOffset? OccurredAt,
    DateTimeOffset? AvailableAt,
    JsonElement? Payload);

internal sealed record EnterpriseNotificationPolicyUpdateRequest(
    bool? Enabled,
    string? DeliveryBoundary,
    string? Severity,
    string? RecipientStrategy,
    JsonElement? TriggerConfiguration,
    JsonElement? RecipientConfiguration,
    string? SubjectTemplate,
    string? TextTemplate,
    bool? AcknowledgementRequired,
    int? AcknowledgementEscalationMinutes,
    string? ChangeReason);

internal sealed record EnterpriseNotificationPreviewRequest(
    string? PolicyCode,
    Guid? ProjectId,
    Guid? SubjectUserId,
    JsonElement? Payload);

internal sealed record EnterpriseNotificationAcknowledgementRequest(
    string? Statement);

internal sealed record EnterpriseNotificationTemplate(
    string Subject,
    string TextBody,
    string HtmlBody,
    IReadOnlyDictionary<string, string> Tokens);

internal sealed record EnterpriseNotificationRecipientResolution(
    ProjectNotificationUser[] Recipients,
    string Status,
    string DiagnosticCode,
    string Message,
    string[] Evidence);

internal sealed record EnterpriseNotificationSourceObservation(
    string SourceCode,
    string SourceModule,
    string Status,
    string DiagnosticCode,
    string Message,
    int RecordsObserved,
    int EventsCreated,
    DateTimeOffset ObservedAt)
{
    internal static EnterpriseNotificationSourceObservation Healthy(
        string sourceCode,
        string sourceModule,
        int recordsObserved,
        int eventsCreated,
        string message = "Authoritative source scan completed.") => new(
            sourceCode,
            sourceModule,
            "healthy",
            string.Empty,
            message,
            recordsObserved,
            eventsCreated,
            DateTimeOffset.UtcNow);

    internal static EnterpriseNotificationSourceObservation Unavailable(
        string sourceCode,
        string sourceModule,
        string diagnosticCode,
        string message) => new(
            sourceCode,
            sourceModule,
            "unavailable",
            diagnosticCode,
            message,
            0,
            0,
            DateTimeOffset.UtcNow);

    internal static EnterpriseNotificationSourceObservation Failed(
        string sourceCode,
        string sourceModule,
        string diagnosticCode,
        string message) => new(
            sourceCode,
            sourceModule,
            "failed",
            diagnosticCode,
            message,
            0,
            0,
            DateTimeOffset.UtcNow);
}

internal sealed record EnterpriseNotificationDispatchSummary(
    Guid EventId,
    Guid? DispatchId,
    string PolicyCode,
    string Status,
    string Provider,
    string RecipientBoundary,
    int RecipientCount,
    string DiagnosticCode,
    string Message);

internal sealed record EnterpriseNotificationRunSummary(
    Guid RunId,
    string Status,
    int ObservedCount,
    int CreatedCount,
    int DispatchedCount,
    int QueuedCount,
    int SuppressedCount,
    int FailedCount,
    EnterpriseNotificationSourceObservation[] Sources,
    EnterpriseNotificationDispatchSummary[] Dispatches,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string Message);

internal sealed record EnterpriseNotificationInventoryRow(
    string PolicyCode,
    string PolicyName,
    string Category,
    string SourceModule,
    string EventCode,
    string TriggerMode,
    string RecipientStrategy,
    string Severity,
    string DeliveryBoundary,
    bool AcknowledgementRequired,
    string OwnerModule,
    string ProducerContract,
    string SourceState,
    bool Enabled,
    string DeliveryAuthority,
    bool DirectSmtpAuthorized,
    bool DirectBrevoAuthorized,
    string RuntimeCoverage,
    string RuntimeMessage);

internal sealed record EnterpriseNotificationEventInsertResult(
    Guid EventId,
    bool Created,
    string Status,
    string Message);
