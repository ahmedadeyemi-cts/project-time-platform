namespace ProjectTime.Api.Ai;

public static class CelarAiConversationAttachmentPolicy
{
    public const string ContractVersion = "celar-ai-conversation-attachments-v1-20260805";
    public const string MigrationId = "072_celar_ai_conversation_attachments";
    public const string Permission = "ATTACH_CELAR_AI_CHAT_DOCUMENTS";
    public const int MaximumFilesPerRequest = 10;
    public const int MaximumActiveFilesPerConversation = 25;
    public const int MaximumActiveProcessingAttachmentsPerUser = 10;
    public const long MaximumActiveBytesPerConversation = 100L * 1024L * 1024L;
    public const int RetentionDays = 90;
    public const int RetentionBatchSize = 100;
    public const string UploadSource = "celar_ai_chat_attachment";
    public const string Classification = "restricted_conversation_attachment";
}

public sealed record CelarAiConversationAttachment(
    Guid AttachmentId,
    Guid ConversationId,
    Guid DocumentId,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string ProcessingStatus,
    string DiagnosticCode,
    Guid? ProcessingJobId,
    DateTimeOffset RetentionUntil,
    DateTimeOffset? RevokedAt,
    DateTimeOffset CreatedAt)
{
    public bool Ready =>
        RevokedAt is null
        && RetentionUntil > DateTimeOffset.UtcNow
        && ProcessingStatus.Equals("ready", StringComparison.OrdinalIgnoreCase);

    public object ToPublicResponse() => new
    {
        attachmentId = AttachmentId,
        conversationId = ConversationId,
        documentId = DocumentId,
        fileName = OriginalFileName,
        contentType = ContentType,
        sizeBytes = SizeBytes,
        processingStatus = ProcessingStatus,
        diagnosticCode = DiagnosticCode,
        processingJobId = ProcessingJobId,
        ready = Ready,
        retentionUntil = RetentionUntil,
        revokedAt = RevokedAt,
        createdAt = CreatedAt,
        rawDocumentTextReturned = false,
        externalProviderReceivedDocument = false
    };
}

public sealed record CelarAiStoredConversationAttachment(
    CelarAiConversationAttachment Attachment,
    string StoragePath,
    string StoredFileName);

public sealed record CelarAiConversationAttachmentUsage(
    int ActiveCount,
    long ActiveBytes,
    int ActiveProcessingCount,
    bool ConversationOwnedAndActive);

public sealed record CelarAiAttachmentPurgeCandidate(
    Guid AttachmentId,
    Guid DocumentId,
    string StoragePath);

public sealed record CelarAiConversationAttachmentUploadResult(
    string Status,
    IReadOnlyList<CelarAiConversationAttachment> Attachments,
    IReadOnlyList<string> Blockers)
{
    public bool Accepted => Attachments.Count > 0 && Blockers.Count == 0;
}
