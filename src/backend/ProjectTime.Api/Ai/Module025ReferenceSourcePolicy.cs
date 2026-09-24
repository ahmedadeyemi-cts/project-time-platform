namespace ProjectTime.Api.Ai;

/// <summary>
/// Dark-launch kill-switch for the Module 025 SOW reference-source feature
/// (Phase 1, canonical templates only). Default OFF: when disabled the canonical
/// admin endpoints report disabled and the SOW/GSD generate path ignores any
/// selected canonical reference, so behaviour is identical to before the feature.
/// </summary>
public static class Module025ReferenceSourcePolicy
{
    public const string ContractVersion = "module025-reference-source-v1-20260923";

    /// <summary>
    /// Stored canonical text is length-capped consistent with the Module 025
    /// ServiceOverview budget. Enforced in code and by a CHECK constraint.
    /// </summary>
    public const int MaximumReferenceTextCharacters = 30_000;

    public const int MaximumLabelCharacters = 300;
    public const int MaximumProjectNameCharacters = 500;

    public static bool Enabled =>
        bool.TryParse(
            Environment.GetEnvironmentVariable("PROJECTPULSE_MODULE025_REFERENCE_SOURCES_ENABLED"),
            out var value)
            ? value
            : false;
}
