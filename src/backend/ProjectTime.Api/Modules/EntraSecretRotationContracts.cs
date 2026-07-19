using System.Security.Cryptography;

namespace ProjectTime.Api.Modules;

/// <summary>
/// The only extension point allowed to persist or activate an Entra credential.
/// Module 065 ships with the locked implementation. An externally reviewed
/// adapter must be supplied explicitly during a later authorized integration.
/// </summary>
public interface IEntraSecretRotationAdapter
{
    bool IsConfigured { get; }
    string AdapterCode { get; }

    Task<EntraSecretOperationResult> PrepareAsync(
        EntraSecretPreparation command,
        EntraSecretActor actor,
        CancellationToken cancellationToken);

    Task<EntraSecretOperationResult> ApproveAsync(
        Guid operationId,
        EntraSecretApproval approval,
        EntraSecretActor actor,
        CancellationToken cancellationToken);

    Task<EntraSecretOperationResult> StageSecretAsync(
        Guid operationId,
        string proposedVersion,
        SensitiveSecretLease secret,
        EntraSecretActor actor,
        CancellationToken cancellationToken);

    Task<EntraSecretOperationResult> TestAsync(
        Guid operationId,
        EntraSecretActor actor,
        CancellationToken cancellationToken);

    Task<EntraSecretOperationResult> ActivateAsync(
        Guid operationId,
        EntraSecretActor actor,
        CancellationToken cancellationToken);

    Task<EntraSecretOperationResult> RollbackAsync(
        Guid operationId,
        EntraSecretRollback rollback,
        EntraSecretActor actor,
        CancellationToken cancellationToken);
}

/// <summary>
/// A short-lived, zeroable UTF-8 secret buffer. It intentionally has no
/// ToString implementation and exposes no serializable secret property.
/// </summary>
public sealed class SensitiveSecretLease : IDisposable
{
    private byte[]? _buffer;
    private readonly int _length;

    public SensitiveSecretLease(byte[] buffer, int length)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (length <= 0 || length > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
        _buffer = buffer;
        _length = length;
    }

    public ReadOnlyMemory<byte> Utf8Bytes =>
        _buffer is null
            ? throw new ObjectDisposedException(nameof(SensitiveSecretLease))
            : _buffer.AsMemory(0, _length);

    public int Length => _buffer is null ? 0 : _length;

    public void Dispose()
    {
        if (_buffer is null) return;
        CryptographicOperations.ZeroMemory(_buffer);
        _buffer = null;
        GC.SuppressFinalize(this);
    }
}

public sealed record EntraSecretActor(
    Guid ActualUserId,
    string ActualEmail,
    DateTimeOffset StepUpAuthenticatedAt,
    string CorrelationId);

public sealed record EntraSecretPreparation(
    string Environment,
    string ProposedVersion,
    DateTimeOffset ExpiresAt,
    int OverlapHours,
    bool DualApprovalRequired,
    string Reason);

public sealed record EntraSecretApproval(
    string Decision,
    string Note);

public sealed record EntraSecretRollback(
    string TargetVersion,
    string Reason);

/// <summary>
/// Sanitized lifecycle evidence. Provider responses, exception text, secret
/// values, tokens, and secret-store references are deliberately impossible to
/// return through this contract.
/// </summary>
public sealed record EntraSecretOperationResult(
    bool Succeeded,
    string Status,
    string Message,
    Guid? OperationId = null,
    string? State = null,
    string? VersionIdentifier = null,
    string? CorrelationId = null,
    DateTimeOffset? RecordedAt = null)
{
    public static EntraSecretOperationResult Locked(string action) => new(
        false,
        "external_authorization_required",
        $"{action} is locked until Azure and Entra changes are explicitly authorized and an approved credential-store adapter is configured.");
}

/// <summary>
/// Fail-closed default. It performs no provider request, persistence, audit
/// write, activation, or rollback and never reads the secret lease.
/// </summary>
public sealed class LockedEntraSecretRotationAdapter : IEntraSecretRotationAdapter
{
    public static LockedEntraSecretRotationAdapter Instance { get; } = new();
    public bool IsConfigured => false;
    public string AdapterCode => "locked_no_external_adapter";

    private LockedEntraSecretRotationAdapter()
    {
    }

    public Task<EntraSecretOperationResult> PrepareAsync(
        EntraSecretPreparation command,
        EntraSecretActor actor,
        CancellationToken cancellationToken) =>
        Task.FromResult(EntraSecretOperationResult.Locked("Rotation preparation"));

    public Task<EntraSecretOperationResult> ApproveAsync(
        Guid operationId,
        EntraSecretApproval approval,
        EntraSecretActor actor,
        CancellationToken cancellationToken) =>
        Task.FromResult(EntraSecretOperationResult.Locked("Rotation approval"));

    public Task<EntraSecretOperationResult> StageSecretAsync(
        Guid operationId,
        string proposedVersion,
        SensitiveSecretLease secret,
        EntraSecretActor actor,
        CancellationToken cancellationToken) =>
        Task.FromResult(EntraSecretOperationResult.Locked("Secret staging"));

    public Task<EntraSecretOperationResult> TestAsync(
        Guid operationId,
        EntraSecretActor actor,
        CancellationToken cancellationToken) =>
        Task.FromResult(EntraSecretOperationResult.Locked("Token-acquisition testing"));

    public Task<EntraSecretOperationResult> ActivateAsync(
        Guid operationId,
        EntraSecretActor actor,
        CancellationToken cancellationToken) =>
        Task.FromResult(EntraSecretOperationResult.Locked("Credential activation"));

    public Task<EntraSecretOperationResult> RollbackAsync(
        Guid operationId,
        EntraSecretRollback rollback,
        EntraSecretActor actor,
        CancellationToken cancellationToken) =>
        Task.FromResult(EntraSecretOperationResult.Locked("Credential rollback"));
}
