using ProjectTime.Api.Ai;

var failures = new List<string>();
var checks = 0;

void Require(bool condition, string name)
{
    checks++;
    if (condition)
    {
        Console.WriteLine($"CELAR_AI_OPERATIONS_{name}=PASSED");
        return;
    }
    failures.Add(name);
    Console.Error.WriteLine($"CELAR_AI_OPERATIONS_{name}=FAILED");
}

Require(
    CelarAiOperationsPolicy.ContractVersion == "celar-ai-ask-operations-v1-20260810",
    "CONTRACT_VERSION");
Require(
    CelarAiOperationsPolicy.MigrationId == "084_module_076_celar_ai_defect_operations",
    "MIGRATION_ID");
Require(
    CelarAiOperationsPolicy.DefaultAssigneeEmail == "ahmed.adeyemi@ussignal.com",
    "DEFAULT_ASSIGNEE_EMAIL");
Require(
    CelarAiOperationsPolicy.DefaultAssigneeName == "Ahmed Adeyemi",
    "DEFAULT_ASSIGNEE_NAME");
Require(
    CelarAiOperationsPolicy.DefaultRepository == "ahmedadeyemi-cts/project-time-platform",
    "REPOSITORY_ALLOWLIST");

foreach (var question in new[]
{
    "Open a defect",
    "Create a defect for Module 076",
    "Report this issue",
    "This is broken",
    "Please file a defect",
    "Open an issue"
})
{
    Require(CelarAiOperationsPolicy.IsDefectIntent(question), $"DEFECT_INTENT_{checks + 1:000}");
}

foreach (var question in new[]
{
    "Troubleshoot Module 064",
    "Diagnose why the private runtime is unavailable",
    "Run diagnostics for GitHub",
    "Why did this request fail?",
    "The service is not working",
    "The API timed out"
})
{
    Require(CelarAiOperationsPolicy.IsTroubleshootingIntent(question), $"TROUBLESHOOT_INTENT_{checks + 1:000}");
}

Require(!CelarAiOperationsPolicy.IsDefectIntent("How do I enter my time?"), "DEFECT_FALSE_POSITIVE");
Require(!CelarAiOperationsPolicy.IsTroubleshootingIntent("What is the project budget?"), "TROUBLESHOOT_FALSE_POSITIVE");

var sensitive = "Authorization: Bearer abcdefghijklmnopqrstuvwxyz123456 Cookie=session-value password=hunter2 postgresql://user:secret@db/pulse";
var sanitized = CelarAiOperationsPolicy.SanitizeOperationalDetail(sensitive);
Require(!sanitized.Contains("abcdefghijklmnopqrstuvwxyz123456", StringComparison.Ordinal), "BEARER_REDACTED");
Require(!sanitized.Contains("session-value", StringComparison.Ordinal), "COOKIE_REDACTED");
Require(!sanitized.Contains("hunter2", StringComparison.Ordinal), "PASSWORD_REDACTED");
Require(!sanitized.Contains("user:secret@db", StringComparison.Ordinal), "CONNECTION_STRING_REDACTED");
Require(sanitized.Contains("[REDACTED]", StringComparison.Ordinal), "REDACTION_MARKER");

Environment.SetEnvironmentVariable("PROJECTPULSE_ENVIRONMENT", "production");
Environment.SetEnvironmentVariable("PROJECTPULSE_CELAR_AI_AUTOMATIC_DEFECTS_ENABLED", "true");
Environment.SetEnvironmentVariable("PROJECTPULSE_CELAR_AI_SYNTHETIC_FAILURES_ENABLED", "true");
Require(!CelarAiOperationsPolicy.AutomaticMonitoringEnabled, "PRODUCTION_AUTOMATIC_DEFECTS_BLOCKED");
Require(!CelarAiOperationsPolicy.SyntheticFailureEnabled, "PRODUCTION_SYNTHETIC_FAILURES_BLOCKED");

Environment.SetEnvironmentVariable("PROJECTPULSE_ENVIRONMENT", "test");
Require(CelarAiOperationsPolicy.AutomaticMonitoringEnabled, "TEST_AUTOMATIC_DEFECTS_FLAG");
Require(CelarAiOperationsPolicy.SyntheticFailureEnabled, "TEST_SYNTHETIC_FAILURES_FLAG");

Environment.SetEnvironmentVariable("PROJECTPULSE_DEFECT_DEFAULT_ASSIGNEE_EMAIL", "");
Require(
    CelarAiOperationsPolicy.DefaultAssigneeEmailValue == "ahmed.adeyemi@ussignal.com",
    "DEFAULT_ASSIGNEE_FALLBACK");
Environment.SetEnvironmentVariable("PROJECTPULSE_DEFECT_DEFAULT_ASSIGNEE_EMAIL", "ahmed.adeyemi@ussignal.com");
Require(
    CelarAiOperationsPolicy.DefaultAssigneeEmailValue == "ahmed.adeyemi@ussignal.com",
    "DEFAULT_ASSIGNEE_CONFIGURED");

Environment.SetEnvironmentVariable("PROJECTPULSE_CELAR_AI_MONITOR_INTERVAL_SECONDS", "1");
Require(CelarAiOperationsPolicy.ProbeIntervalSeconds == 30, "PROBE_INTERVAL_MINIMUM");
Environment.SetEnvironmentVariable("PROJECTPULSE_CELAR_AI_MONITOR_INTERVAL_SECONDS", "7200");
Require(CelarAiOperationsPolicy.ProbeIntervalSeconds == 3600, "PROBE_INTERVAL_MAXIMUM");

var cleaned = CelarAiOperationsPolicy.CleanList(
    new[] { " one ", "two", "one", "", "three" },
    maximumItems: 2,
    maximumCharacters: 20);
Require(cleaned.SequenceEqual(new[] { "one", "two" }), "CLEAN_LIST_DEDUPLICATION");

var probe = new CelarAiProbeEvidence(
    "private_inference",
    "private_inference",
    "Private inference",
    "failed",
    503,
    1250,
    "runtime_unavailable",
    "The runtime did not respond.",
    "celarai.onenecklab.com/health",
    DateTimeOffset.UtcNow);
Require(probe.Failed && !probe.Healthy, "PROBE_FAILURE_STATE");
var healthy = probe with { Status = "healthy", FailureCode = string.Empty, HttpStatus = 200 };
Require(healthy.Healthy && !healthy.Failed, "PROBE_HEALTHY_STATE");

var draft = new CelarAiDefectDraft(
    "Module 076 defect creation failed",
    "The guided questionnaire returned an error after confirmation.",
    "Bug",
    "High",
    "test",
    "Pulse",
    "076",
    "/api/celar-ai/v1/operations/defects/intake-sessions/id/submit",
    "A durable Module 076 defect should be created.",
    "The request failed.",
    new[] { "Open Ask Celar AI", "Complete the questionnaire", "Select Create defect" },
    "The user cannot report a defect.",
    "None",
    "correlation-123",
    new string('a', 40));
Require(draft.ReproductionSteps.Count == 3, "QUESTIONNAIRE_STEPS");
Require(draft.Category == "Bug" && draft.Priority == "High", "QUESTIONNAIRE_TAXONOMY");

Require(CelarAiOperationsPolicy.MaximumEvidenceItems == 25, "EVIDENCE_LIMIT");
Require(CelarAiOperationsPolicy.MaximumReproductionSteps == 25, "REPRODUCTION_LIMIT");
Require(CelarAiOperationsPolicy.MaximumAutomaticDefectsPerHour == 10, "AUTOMATIC_DEFECT_RATE_LIMIT");

if (failures.Count > 0)
{
    Console.Error.WriteLine($"CELAR_AI_OPERATIONS_POLICY_TEST_FAILURES={string.Join(',', failures)}");
    return 1;
}

Console.WriteLine($"CELAR_AI_OPERATIONS_POLICY_CHECKS={checks}");
Console.WriteLine("CELAR_AI_OPERATIONS_POLICY_TESTS=PASS");
return 0;
