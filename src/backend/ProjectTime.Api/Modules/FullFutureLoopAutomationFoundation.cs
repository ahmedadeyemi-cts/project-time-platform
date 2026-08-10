using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Provider-neutral contracts for the Module 083 autonomous control plane.
/// This foundation performs deterministic policy evaluation only. It contains
/// no GitHub, cloud, secret-store, deployment, telemetry, or external-AI client.
/// </summary>
public static partial class FullFutureLoopAutomationFoundation
{
    public const string ContractVersion = "083-autonomous-control-plane-foundation-v1";

    public static readonly IReadOnlySet<string> SupportedEnvironments =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "canary", "test", "production"
        };

    public static readonly IReadOnlySet<string> SupportedOperations =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "observe",
            "classify",
            "create_issue",
            "dispatch_ci",
            "run_canary",
            "deploy",
            "verify",
            "rollback",
            "notify",
            "propose_repair"
        };

    public static IReadOnlyList<FullFutureLoopAdapterDescriptor> DefaultAdapterCatalog() =>
    [
        Adapter(
            "github",
            "GitHub repository and workflow adapter",
            ["repository_read", "pull_request_read", "checks_read", "actions_read", "issues_write", "workflow_dispatch"],
            "GitHub App with least-privilege installation",
            writesExternally: true),
        Adapter(
            "canary",
            "Disposable canary execution adapter",
            ["seed_scenario", "execute_contracts", "collect_evidence", "prove_cleanup"],
            "Protected reusable workflow or isolated runner",
            writesExternally: true),
        Adapter(
            "azure_container_apps",
            "Azure Container Apps deployment adapter",
            ["read_environment", "deploy_exact_digest", "verify_revision", "restore_exact_digest"],
            "GitHub Environment OIDC identity",
            writesExternally: true),
        Adapter(
            "azure_observability",
            "Azure Monitor and Application Insights evidence adapter",
            ["health_read", "slo_read", "logs_read", "release_identity_read"],
            "Read-only managed identity or federated identity",
            writesExternally: false),
        Adapter(
            "module_076",
            "Pulse defect and private repair adapter",
            ["defect_create", "defect_update", "repair_evidence_link"],
            "Pulse service identity and Module 076 capability",
            writesExternally: true),
        Adapter(
            "module_065",
            "Pulse notification adapter",
            ["notification_prepare", "notification_send", "delivery_evidence_read"],
            "Module 065 governed connection",
            writesExternally: true),
        Adapter(
            "celar_ai",
            "Celar AI advisory adapter",
            ["classify", "summarize", "recommend", "draft_repair"],
            "Module 011 through Module 064",
            writesExternally: false)
    ];

    private static FullFutureLoopAdapterDescriptor Adapter(
        string code,
        string name,
        IReadOnlyList<string> capabilities,
        string credentialBoundary,
        bool writesExternally) =>
        new(
            code,
            name,
            capabilities,
            credentialBoundary,
            writesExternally,
            FullFutureLoopAdapterMode.Disabled,
            IsReady: false,
            CircuitOpen: false,
            LastSuccessfulProbeAt: null,
            Detail: "Not configured. The autonomous foundation remains dry-run and fail-closed.");
}

public enum FullFutureLoopAutomationDisposition
{
    AutoExecute,
    ApprovalRequired,
    Blocked
}

public enum FullFutureLoopAdapterMode
{
    Disabled,
    DryRun,
    Active
}

public enum FullFutureLoopRiskClass
{
    Routine,
    Normal,
    High,
    Critical
}

public sealed record FullFutureLoopAdapterDescriptor(
    string Code,
    string Name,
    IReadOnlyList<string> Capabilities,
    string CredentialBoundary,
    bool WritesExternally,
    FullFutureLoopAdapterMode Mode,
    bool IsReady,
    bool CircuitOpen,
    DateTimeOffset? LastSuccessfulProbeAt,
    string Detail);

public sealed record FullFutureLoopAutomationPolicy(
    string PolicyVersion,
    bool Enabled,
    bool GlobalKillSwitch,
    IReadOnlySet<string> AllowedRepositories,
    IReadOnlySet<string> AllowedEnvironments,
    IReadOnlySet<string> AllowedOperations,
    bool AllowAutomaticTestDeployment,
    bool AllowAutomaticTestRollback,
    bool AllowAutomaticProductionDeployment,
    bool AllowAutomaticProductionRollback,
    bool RequireProductionApproval,
    bool RequireMigrationApproval,
    bool RequireSecurityApproval,
    bool RequireInfrastructureApproval,
    bool RequireSecretChangeApproval,
    int MaximumConcurrentRuns,
    int MaximumStepAttempts,
    TimeSpan MaximumRunDuration,
    TimeSpan EvidenceMaximumAge,
    IReadOnlySet<string> ApprovedProductionChangeTypes)
{
    public static FullFutureLoopAutomationPolicy EnterpriseDefault() =>
        new(
            PolicyVersion: "enterprise-default-v1",
            Enabled: false,
            GlobalKillSwitch: true,
            AllowedRepositories: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ahmedadeyemi-cts/project-time-platform"
            },
            AllowedEnvironments: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "canary", "test"
            },
            AllowedOperations: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "observe", "classify", "create_issue", "dispatch_ci", "run_canary",
                "deploy", "verify", "rollback", "notify", "propose_repair"
            },
            AllowAutomaticTestDeployment: true,
            AllowAutomaticTestRollback: true,
            AllowAutomaticProductionDeployment: false,
            AllowAutomaticProductionRollback: false,
            RequireProductionApproval: true,
            RequireMigrationApproval: true,
            RequireSecurityApproval: true,
            RequireInfrastructureApproval: true,
            RequireSecretChangeApproval: true,
            MaximumConcurrentRuns: 2,
            MaximumStepAttempts: 3,
            MaximumRunDuration: TimeSpan.FromHours(2),
            EvidenceMaximumAge: TimeSpan.FromHours(24),
            ApprovedProductionChangeTypes: new HashSet<string>(StringComparer.OrdinalIgnoreCase));
}

public sealed record FullFutureLoopReleaseArtifact(
    string Component,
    string Image,
    string Digest,
    string? SbomReference,
    string? ProvenanceReference,
    string? SignatureReference);

public sealed record FullFutureLoopMigrationEvidence(
    string MigrationId,
    string Sha256,
    bool Additive,
    bool RollbackReviewed,
    bool RequiresDowntime);

public sealed record FullFutureLoopReleaseManifest(
    string ManifestVersion,
    string Repository,
    string SourceCommit,
    int? PullRequestNumber,
    string BuildWorkflow,
    long? BuildRunId,
    int? BuildRunAttempt,
    IReadOnlyList<FullFutureLoopReleaseArtifact> Artifacts,
    IReadOnlyList<FullFutureLoopMigrationEvidence> Migrations,
    string TargetEnvironment,
    IReadOnlyList<string> CanaryEvidenceReferences,
    IReadOnlyList<string> VerificationEvidenceReferences,
    IReadOnlyList<string> ApprovalEvidenceReferences,
    IReadOnlyList<string> RollbackArtifactDigests,
    string ConfigurationFingerprint,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record FullFutureLoopAutomationRequest(
    Guid RequestId,
    Guid? LoopId,
    string Operation,
    string Environment,
    string Repository,
    string SourceCommit,
    FullFutureLoopRiskClass RiskClass,
    string ChangeType,
    bool IncludesMigration,
    bool IncludesSecurityChange,
    bool IncludesInfrastructureChange,
    bool IncludesSecretChange,
    bool IsEmergencyRollback,
    bool ProductionApprovalSatisfied,
    bool MigrationApprovalSatisfied,
    bool SecurityApprovalSatisfied,
    bool InfrastructureApprovalSatisfied,
    bool SecretChangeApprovalSatisfied,
    bool CanaryPassed,
    bool CleanupProven,
    bool VerificationSuitePassed,
    bool RollbackTargetProven,
    bool ExactArtifactDigestsPresent,
    bool SbomPresent,
    bool ProvenancePresent,
    bool SignaturesVerified,
    DateTimeOffset EvidenceGeneratedAt,
    DateTimeOffset RequestedAt,
    string RequestedByAuthority,
    bool RequestedByAi);

public sealed record FullFutureLoopAutomationDecision(
    FullFutureLoopAutomationDisposition Disposition,
    string PolicyVersion,
    string DecisionCode,
    string Summary,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> RequiredApprovals,
    DateTimeOffset EvaluatedAt)
{
    public bool MayExecute => Disposition == FullFutureLoopAutomationDisposition.AutoExecute;
}

public static partial class FullFutureLoopAutomationPolicyEngine
{
    private static readonly Regex CommitPattern =
        new("^[0-9a-f]{40}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DigestPattern =
        new("^sha256:[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static FullFutureLoopAutomationDecision Evaluate(
        FullFutureLoopAutomationRequest request,
        FullFutureLoopAutomationPolicy policy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(policy);

        var blockers = new List<string>();
        var approvals = new List<string>();

        if (!policy.Enabled)
            blockers.Add("Automation policy is disabled.");
        if (policy.GlobalKillSwitch)
            blockers.Add("The global automation kill switch is active.");
        if (string.IsNullOrWhiteSpace(policy.PolicyVersion))
            blockers.Add("A versioned policy is required.");
        if (!FullFutureLoopAutomationFoundation.SupportedOperations.Contains(request.Operation))
            blockers.Add($"Operation '{request.Operation}' is not supported by the control-plane contract.");
        if (!policy.AllowedOperations.Contains(request.Operation))
            blockers.Add($"Operation '{request.Operation}' is outside the allowed policy envelope.");
        if (!FullFutureLoopAutomationFoundation.SupportedEnvironments.Contains(request.Environment))
            blockers.Add($"Environment '{request.Environment}' is not recognized.");
        if (!policy.AllowedEnvironments.Contains(request.Environment))
            blockers.Add($"Environment '{request.Environment}' is not allowlisted by policy.");
        if (!policy.AllowedRepositories.Contains(request.Repository))
            blockers.Add($"Repository '{request.Repository}' is not allowlisted by policy.");
        if (!CommitPattern.IsMatch(request.SourceCommit ?? string.Empty))
            blockers.Add("An exact lowercase 40-character source commit is required.");
        if (string.IsNullOrWhiteSpace(request.RequestedByAuthority))
            blockers.Add("An attributable requesting authority is required.");
        if (request.RequestedByAi)
            blockers.Add("An AI model cannot be the approving or requesting authority for an external mutation.");
        if (request.EvidenceGeneratedAt > now.AddMinutes(5))
            blockers.Add("Evidence timestamp is in the future.");
        if (now - request.EvidenceGeneratedAt > policy.EvidenceMaximumAge)
            blockers.Add("Required release evidence is older than the policy allows.");
        if (policy.MaximumConcurrentRuns < 1)
            blockers.Add("Maximum concurrent runs must be at least one.");
        if (policy.MaximumStepAttempts is < 1 or > 10)
            blockers.Add("Maximum step attempts must be between one and ten.");
        if (policy.MaximumRunDuration <= TimeSpan.Zero)
            blockers.Add("Maximum run duration must be greater than zero.");

        var mutation = IsMutation(request.Operation);
        var releaseOperation = IsReleaseOperation(request.Operation);

        if (mutation && !request.ExactArtifactDigestsPresent && releaseOperation)
            blockers.Add("Release mutation requires immutable artifact digests.");
        if (releaseOperation && !request.SbomPresent)
            blockers.Add("A release mutation requires an SBOM reference.");
        if (releaseOperation && !request.ProvenancePresent)
            blockers.Add("A release mutation requires build provenance.");
        if (releaseOperation && !request.SignaturesVerified)
            blockers.Add("A release mutation requires verified artifact signatures.");
        if (request.Operation is "deploy" or "verify" && !request.CanaryPassed)
            blockers.Add("Deployment or verification requires a passing canary.");
        if (request.Operation is "deploy" or "verify" && !request.CleanupProven)
            blockers.Add("Disposable canary cleanup must be proven before release progression.");
        if (request.Operation == "verify" && !request.VerificationSuitePassed)
            blockers.Add("The release verification suite has not passed.");
        if (request.Operation == "rollback" && !request.RollbackTargetProven)
            blockers.Add("Rollback requires the exact prior known-good target.");

        if (request.IncludesMigration && policy.RequireMigrationApproval && !request.MigrationApprovalSatisfied)
            approvals.Add("migration_approval");
        if (request.IncludesSecurityChange && policy.RequireSecurityApproval && !request.SecurityApprovalSatisfied)
            approvals.Add("security_approval");
        if (request.IncludesInfrastructureChange && policy.RequireInfrastructureApproval && !request.InfrastructureApprovalSatisfied)
            approvals.Add("infrastructure_approval");
        if (request.IncludesSecretChange && policy.RequireSecretChangeApproval && !request.SecretChangeApprovalSatisfied)
            approvals.Add("secret_change_approval");

        var production = request.Environment.Equals("production", StringComparison.OrdinalIgnoreCase);
        var test = request.Environment.Equals("test", StringComparison.OrdinalIgnoreCase)
            || request.Environment.Equals("canary", StringComparison.OrdinalIgnoreCase);

        if (production && policy.RequireProductionApproval && !request.ProductionApprovalSatisfied)
            approvals.Add("production_environment_approval");

        if (production && request.Operation == "deploy" && !policy.AllowAutomaticProductionDeployment)
            approvals.Add("production_deployment_authority");
        if (production && request.Operation == "rollback" && !policy.AllowAutomaticProductionRollback)
            approvals.Add("production_rollback_authority");
        if (test && request.Operation == "deploy" && !policy.AllowAutomaticTestDeployment)
            approvals.Add("test_deployment_authority");
        if (test && request.Operation == "rollback" && !policy.AllowAutomaticTestRollback)
            approvals.Add("test_rollback_authority");

        if (production
            && request.RiskClass is FullFutureLoopRiskClass.High or FullFutureLoopRiskClass.Critical
            && request.Operation == "deploy")
        {
            approvals.Add("high_risk_release_approval");
        }

        if (production
            && request.Operation == "deploy"
            && policy.ApprovedProductionChangeTypes.Count > 0
            && !policy.ApprovedProductionChangeTypes.Contains(request.ChangeType))
        {
            approvals.Add("change_type_exception_approval");
        }

        if (request.IsEmergencyRollback)
        {
            if (request.Operation != "rollback")
                blockers.Add("Emergency rollback classification is valid only for rollback operations.");
            if (!request.RollbackTargetProven)
                blockers.Add("Emergency rollback cannot execute without an exact proven rollback target.");
        }

        var distinctBlockers = blockers.Distinct(StringComparer.Ordinal).ToArray();
        var distinctApprovals = approvals.Distinct(StringComparer.Ordinal).ToArray();

        if (distinctBlockers.Length > 0)
        {
            return Decision(
                FullFutureLoopAutomationDisposition.Blocked,
                policy,
                "AUTOMATION_BLOCKED",
                "The requested action is outside the proven automation boundary.",
                distinctBlockers,
                distinctApprovals,
                now);
        }

        if (distinctApprovals.Length > 0)
        {
            return Decision(
                FullFutureLoopAutomationDisposition.ApprovalRequired,
                policy,
                "AUTOMATION_APPROVAL_REQUIRED",
                "The action is valid but cannot execute until the required authority is recorded.",
                ["All deterministic validation gates passed."],
                distinctApprovals,
                now);
        }

        return Decision(
            FullFutureLoopAutomationDisposition.AutoExecute,
            policy,
            "AUTOMATION_AUTO_EXECUTE",
            "The action is inside the approved, attributable, reversible automation envelope.",
            ["All deterministic validation and approval gates passed."],
            [],
            now);
    }

    public static bool IsValidDigest(string? digest) =>
        DigestPattern.IsMatch(digest ?? string.Empty);

    private static bool IsMutation(string operation) => operation is
        "create_issue" or "dispatch_ci" or "run_canary" or "deploy" or
        "rollback" or "notify" or "propose_repair";

    private static bool IsReleaseOperation(string operation) => operation is
        "deploy" or "verify" or "rollback";

    private static FullFutureLoopAutomationDecision Decision(
        FullFutureLoopAutomationDisposition disposition,
        FullFutureLoopAutomationPolicy policy,
        string code,
        string summary,
        IReadOnlyList<string> reasons,
        IReadOnlyList<string> approvals,
        DateTimeOffset evaluatedAt) =>
        new(
            disposition,
            policy.PolicyVersion,
            code,
            summary,
            new ReadOnlyCollection<string>(reasons.ToArray()),
            new ReadOnlyCollection<string>(approvals.ToArray()),
            evaluatedAt);
}
