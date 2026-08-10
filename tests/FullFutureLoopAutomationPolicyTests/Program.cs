using ProjectTime.Api.Modules;

var now = DateTimeOffset.UtcNow;
var passed = 0;
var failed = 0;

void Check(string name, bool condition)
{
    if (condition)
    {
        passed += 1;
        Console.WriteLine($"FULL_FUTURE_LOOP_AUTONOMY_{name}=PASSED");
        return;
    }

    failed += 1;
    Console.WriteLine($"FULL_FUTURE_LOOP_AUTONOMY_{name}=FAILED");
}

FullFutureLoopAutomationRequest Request(
    string operation = "deploy",
    string environment = "test",
    bool requestedByAi = false,
    bool migration = false,
    bool migrationApproval = true,
    bool rollbackTarget = true) =>
    new(
        RequestId: Guid.NewGuid(),
        LoopId: Guid.NewGuid(),
        Operation: operation,
        Environment: environment,
        Repository: "ahmedadeyemi-cts/project-time-platform",
        SourceCommit: new string('a', 40),
        RiskClass: FullFutureLoopRiskClass.Normal,
        ChangeType: "application",
        IncludesMigration: migration,
        IncludesSecurityChange: false,
        IncludesInfrastructureChange: false,
        IncludesSecretChange: false,
        IsEmergencyRollback: false,
        ProductionApprovalSatisfied: environment != "production",
        MigrationApprovalSatisfied: migrationApproval,
        SecurityApprovalSatisfied: true,
        InfrastructureApprovalSatisfied: true,
        SecretChangeApprovalSatisfied: true,
        CanaryPassed: true,
        CleanupProven: true,
        VerificationSuitePassed: true,
        RollbackTargetProven: rollbackTarget,
        ExactArtifactDigestsPresent: true,
        SbomPresent: true,
        ProvenancePresent: true,
        SignaturesVerified: true,
        EvidenceGeneratedAt: now,
        RequestedAt: now,
        RequestedByAuthority: "release-manager@example.invalid",
        RequestedByAi: requestedByAi);

var defaultPolicy = FullFutureLoopAutomationPolicy.EnterpriseDefault();
var defaultDecision = FullFutureLoopAutomationPolicyEngine.Evaluate(Request(), defaultPolicy, now);
Check(
    "DEFAULT_FAILS_CLOSED",
    defaultDecision.Disposition == FullFutureLoopAutomationDisposition.Blocked
    && defaultDecision.Reasons.Any(reason => reason.Contains("disabled", StringComparison.OrdinalIgnoreCase))
    && defaultDecision.Reasons.Any(reason => reason.Contains("kill switch", StringComparison.OrdinalIgnoreCase)));

var testPolicy = defaultPolicy with
{
    Enabled = true,
    GlobalKillSwitch = false
};
var testDecision = FullFutureLoopAutomationPolicyEngine.Evaluate(Request(), testPolicy, now);
Check(
    "TEST_DEPLOYMENT_AUTO_EXECUTES",
    testDecision.Disposition == FullFutureLoopAutomationDisposition.AutoExecute
    && testDecision.MayExecute);

var productionPolicy = testPolicy with
{
    AllowedEnvironments = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "canary", "test", "production"
    },
    AllowAutomaticProductionDeployment = true
};
var productionDecision = FullFutureLoopAutomationPolicyEngine.Evaluate(
    Request(environment: "production"),
    productionPolicy,
    now);
Check(
    "PRODUCTION_REQUIRES_APPROVAL",
    productionDecision.Disposition == FullFutureLoopAutomationDisposition.ApprovalRequired
    && productionDecision.RequiredApprovals.Contains("production_environment_approval"));

var aiDecision = FullFutureLoopAutomationPolicyEngine.Evaluate(
    Request(requestedByAi: true),
    testPolicy,
    now);
Check(
    "AI_CANNOT_APPROVE_EXTERNAL_MUTATION",
    aiDecision.Disposition == FullFutureLoopAutomationDisposition.Blocked
    && aiDecision.Reasons.Any(reason => reason.Contains("AI model", StringComparison.OrdinalIgnoreCase)));

var rollbackDecision = FullFutureLoopAutomationPolicyEngine.Evaluate(
    Request(operation: "rollback", rollbackTarget: false),
    testPolicy,
    now);
Check(
    "ROLLBACK_REQUIRES_EXACT_TARGET",
    rollbackDecision.Disposition == FullFutureLoopAutomationDisposition.Blocked
    && rollbackDecision.Reasons.Any(reason => reason.Contains("known-good", StringComparison.OrdinalIgnoreCase)));

var migrationDecision = FullFutureLoopAutomationPolicyEngine.Evaluate(
    Request(migration: true, migrationApproval: false),
    testPolicy,
    now);
Check(
    "MIGRATION_REQUIRES_APPROVAL",
    migrationDecision.Disposition == FullFutureLoopAutomationDisposition.ApprovalRequired
    && migrationDecision.RequiredApprovals.Contains("migration_approval"));

var staleRequest = Request() with
{
    EvidenceGeneratedAt = now.Subtract(testPolicy.EvidenceMaximumAge).AddMinutes(-1)
};
var staleDecision = FullFutureLoopAutomationPolicyEngine.Evaluate(staleRequest, testPolicy, now);
Check(
    "STALE_EVIDENCE_BLOCKS",
    staleDecision.Disposition == FullFutureLoopAutomationDisposition.Blocked
    && staleDecision.Reasons.Any(reason => reason.Contains("older", StringComparison.OrdinalIgnoreCase)));

Check(
    "DIGEST_VALIDATION",
    FullFutureLoopAutomationPolicyEngine.IsValidDigest($"sha256:{new string('b', 64)}")
    && !FullFutureLoopAutomationPolicyEngine.IsValidDigest("latest"));

Check(
    "ADAPTERS_DISABLED_BY_DEFAULT",
    FullFutureLoopAutomationFoundation.DefaultAdapterCatalog().All(adapter =>
        adapter.Mode == FullFutureLoopAdapterMode.Disabled
        && !adapter.IsReady));

Console.WriteLine($"FULL_FUTURE_LOOP_AUTONOMY_TESTS_PASSED={passed}");
Console.WriteLine($"FULL_FUTURE_LOOP_AUTONOMY_TESTS_FAILED={failed}");
Console.WriteLine($"FULL_FUTURE_LOOP_AUTONOMY_CONTRACT={(failed == 0 ? "PASSED" : "FAILED")}");
return failed == 0 ? 0 : 1;
