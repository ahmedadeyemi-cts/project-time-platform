namespace ProjectTime.Api.Modules;

/// <summary>
/// Immutable source marker for the protected-Test FlowHive authority release.
/// The marker deliberately has no runtime side effects; changing it causes the
/// established protected-Test application workflow to rebuild and deploy the
/// exact main commit before the focused Migration 094 and UAT controller runs.
/// </summary>
internal static class FlowHiveProtectedTestReleaseMarker
{
    internal const string ContractVersion = "flowhive-protected-test-authority-uat-v1-20260818";
    internal const string RequiredMigration = "094_flowhive_canonical_sow_authority";
    internal const string DeploymentTarget = "protected_test_only";
    internal const string ProductionMutation = "none";
}
