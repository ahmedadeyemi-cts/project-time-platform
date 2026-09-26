namespace ProjectTime.Api.Ai;

/// <summary>
/// Dark-launch kill-switch for the Module 025 in-app SOW/GSD preview feature
/// (Stage 3). Default OFF: when disabled the read-only preview endpoints report
/// 404 and the authoring workspace shows no preview control, so behaviour — and
/// the byte-for-byte download path — is identical to before the feature.
/// </summary>
public static class Module025PreviewPolicy
{
    public const string ContractVersion = "module025-in-app-preview-v1-20260925";

    /// <summary>
    /// Kill-switch env var. Default OFF. Follows the module's established
    /// <c>PROJECTPULSE_MODULE025_*_ENABLED</c> flag pattern
    /// (see <see cref="Module025ReferenceSourcePolicy"/>).
    /// </summary>
    public const string EnabledEnvironmentVariable = "PROJECTPULSE_MODULE025_PREVIEW_ENABLED";

    public static bool Enabled =>
        bool.TryParse(
            Environment.GetEnvironmentVariable(EnabledEnvironmentVariable),
            out var value)
            ? value
            : false;
}
