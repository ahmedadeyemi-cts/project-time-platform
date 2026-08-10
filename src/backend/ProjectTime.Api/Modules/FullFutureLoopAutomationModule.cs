using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public sealed record FullFutureLoopAutomationSimulationRequest(
    string? Operation,
    string? Environment,
    string? Repository,
    string? SourceCommit,
    string? RiskClass = "normal",
    string? ChangeType = "application",
    bool IncludesMigration = false,
    bool IncludesSecurityChange = false,
    bool IncludesInfrastructureChange = false,
    bool IncludesSecretChange = false,
    bool IsEmergencyRollback = false,
    bool ProductionApprovalSatisfied = false,
    bool MigrationApprovalSatisfied = false,
    bool SecurityApprovalSatisfied = false,
    bool InfrastructureApprovalSatisfied = false,
    bool SecretChangeApprovalSatisfied = false,
    bool CanaryPassed = true,
    bool CleanupProven = true,
    bool VerificationSuitePassed = true,
    bool RollbackTargetProven = true,
    bool ExactArtifactDigestsPresent = true,
    bool SbomPresent = true,
    bool ProvenancePresent = true,
    bool SignaturesVerified = true,
    DateTimeOffset? EvidenceGeneratedAt = null,
    string? RequestedByAuthority = null,
    bool RequestedByAi = false,
    bool AssumePolicyEnabled = false,
    bool AssumeKillSwitchReleased = false,
    string? IdempotencyKey = null,
    Guid? LoopId = null);

public sealed record FullFutureLoopAutomationRuntimeRequest(
    bool AutomationEnabled,
    bool GlobalKillSwitch,
    int ExpectedRevision,
    string? Reason);

public sealed record FullFutureLoopAdapterModeRequest(
    string? Mode,
    int ExpectedRevision,
    string? Reason);

public sealed record FullFutureLoopApprovalDecisionRequest(
    string? Decision,
    int ExpectedRevision,
    string? Reason);

/// <summary>
/// Durable, provider-neutral orchestration for Module 083. This phase persists
/// policy decisions, dry-run plans, approvals, exact release manifests, leases,
/// blocked outbox records, and append-only evidence. It deliberately contains no
/// GitHub, Azure, deployment, secret-store, telemetry, process-execution, or
/// external-AI client. Active adapter mode and external execution fail closed.
/// </summary>
public static class FullFutureLoopAutomationModule
{
    private const string Module = "083";
    private const string ContractVersion = "083-autonomous-control-plane-orchestration-v1";
    private const string Migration = "083_module_083_autonomous_control_plane";
    private const string DefaultRepository = "ahmedadeyemi-cts/project-time-platform";

    private static readonly HashSet<string> ViewRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "SYSTEM_ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR", "MANAGER", "RELEASE_MANAGER",
        "ENGINEERING_MANAGER", "ENGINEERING_LEAD", "ENGINEERING_TEAM_LEAD",
        "ENGINEER", "ENGINEERING", "SYSTEMS_ENGINEER", "NETWORK_ENGINEER",
        "SOLUTION_ARCHITECT", "PROJECT_MANAGER", "PROJECT_MANAGEMENT",
        "SUPPORT", "HELP_DESK", "SERVICE_DESK", "SUPPORT_MANAGER",
        "EXECUTIVE", "EXECUTIVE_LEADERSHIP"
    };

    private static readonly HashSet<string> OperateRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "SYSTEM_ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR", "MANAGER", "RELEASE_MANAGER",
        "ENGINEERING_MANAGER", "ENGINEERING_LEAD", "ENGINEERING_TEAM_LEAD",
        "PROJECT_MANAGER", "PROJECT_MANAGEMENT", "SUPPORT_MANAGER"
    };

    private static readonly HashSet<string> ManageRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "SYSTEM_ADMINISTRATOR", "RELEASE_MANAGER"
    };

    public static IEndpointRouteBuilder MapFullFutureLoopAutomationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/full-future-loop/automation");
        group.MapGet("/readiness", (Func<HttpContext, Task<IResult>>)ReadinessAsync);
        group.MapGet("/policy", (Func<HttpContext, Task<IResult>>)GetPolicyAsync);
        group.MapPost("/policy/simulate", (Func<FullFutureLoopAutomationSimulationRequest, HttpContext, Task<IResult>>)SimulateAsync);
        group.MapGet("/adapters", (Func<HttpContext, Task<IResult>>)ListAdaptersAsync);
        group.MapPost("/adapters/{adapterCode}/mode", (Func<string, FullFutureLoopAdapterModeRequest, HttpContext, Task<IResult>>)SetAdapterModeAsync);
        group.MapGet("/runs", (Func<HttpContext, int?, Task<IResult>>)ListRunsAsync);
        group.MapGet("/runs/{runId:guid}", (Func<Guid, HttpContext, Task<IResult>>)GetRunAsync);
        group.MapPost("/runs/dry-run", (Func<FullFutureLoopAutomationSimulationRequest, HttpContext, Task<IResult>>)CreateDryRunAsync);
        group.MapPost("/runs/{runId:guid}/manifest", (Func<Guid, FullFutureLoopReleaseManifest, HttpContext, Task<IResult>>)RegisterManifestAsync);
        group.MapGet("/approvals", (Func<HttpContext, string?, int?, Task<IResult>>)ListApprovalsAsync);
        group.MapPost("/approvals/{approvalId:guid}/decision", (Func<Guid, FullFutureLoopApprovalDecisionRequest, HttpContext, Task<IResult>>)DecideApprovalAsync);
        group.MapPost("/runtime", (Func<FullFutureLoopAutomationRuntimeRequest, HttpContext, Task<IResult>>)UpdateRuntimeAsync);
        group.MapGet("/evidence", (Func<HttpContext, Guid?, int?, Task<IResult>>)ListEvidenceAsync);
        return endpoints;
    }

    private static async Task<IResult> ReadinessAsync(HttpContext context)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = Require(access, write: false);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();

            var state = await LoadStateAsync(connection, null, false, context.RequestAborted);
            var counts = await LoadCountsAsync(connection, context.RequestAborted);
            return Results.Ok(new
            {
                module = Module,
                contractVersion = ContractVersion,
                status = "ready",
                dataReady = true,
                migration = Migration,
                mode = "durable_dry_run",
                runtime = StateResponse(state),
                counts,
                permissions = Permissions(access!),
                scope = Scope(access!),
                adapterCatalog = FullFutureLoopAutomationFoundation.DefaultAdapterCatalog(),
                guarantees = new[]
                {
                    "No external adapter client is installed in this phase.",
                    "Only disabled and dry_run adapter modes are accepted.",
                    "All automation runs are constrained to dry_run=true by the database.",
                    "Production, migration, security, infrastructure, and secret changes remain approval gated.",
                    "The requester cannot approve the same automation run.",
                    "Release manifests and control-plane evidence are append-only.",
                    "View-As remains read-only and AI cannot act as requesting authority."
                },
                externalExecutionEnabled = false,
                generatedAt = DateTimeOffset.UtcNow
            });
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return MigrationRequired();
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "load autonomous control-plane readiness");
        }
    }

    private static async Task<IResult> GetPolicyAsync(HttpContext context)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = Require(access, write: false);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();

            var state = await LoadStateAsync(connection, null, false, context.RequestAborted);
            var stored = await LoadActivePolicyAsync(connection, state.ActivePolicyVersionId, context.RequestAborted);
            var policy = BuildPolicy(stored, state);
            return Results.Ok(new
            {
                module = Module,
                contractVersion = ContractVersion,
                runtime = StateResponse(state),
                activePolicy = PolicyResponse(stored, policy),
                permissions = Permissions(access!),
                scope = Scope(access!),
                externalExecutionEnabled = false
            });
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return MigrationRequired();
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "load autonomous policy");
        }
    }

    private static async Task<IResult> SimulateAsync(FullFutureLoopAutomationSimulationRequest request, HttpContext context)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = Require(access, write: false);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();

            var state = await LoadStateAsync(connection, null, false, context.RequestAborted);
            var stored = await LoadActivePolicyAsync(connection, state.ActivePolicyVersionId, context.RequestAborted);
            var policy = BuildPolicy(stored, state);
            if (request.AssumePolicyEnabled) policy = policy with { Enabled = true };
            if (request.AssumeKillSwitchReleased) policy = policy with { GlobalKillSwitch = false };
            var now = DateTimeOffset.UtcNow;
            var automationRequest = ToAutomationRequest(request, access!, now);
            var decision = FullFutureLoopAutomationPolicyEngine.Evaluate(automationRequest, policy, now);
            return Results.Ok(new
            {
                module = Module,
                contractVersion = ContractVersion,
                mode = "simulation_only",
                decision,
                request = PublicRequest(automationRequest),
                policy = PolicyResponse(stored, policy),
                persisted = false,
                externalExecutionAttempted = false
            });
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "simulate an autonomous policy decision");
        }
    }

    private static async Task<IResult> ListAdaptersAsync(HttpContext context)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = Require(access, write: false);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();

            var adapters = new List<object>();
            await using var command = new NpgsqlCommand("""
                SELECT adapter_code,display_name,capabilities::text,credential_boundary,writes_externally,
                       adapter_mode,is_ready,circuit_open,last_probe_at,last_successful_probe_at,
                       failure_count,detail,revision_number,updated_at
                FROM full_future_loop_automation_adapters
                ORDER BY adapter_code;
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                adapters.Add(new
                {
                    adapterCode = reader.GetString(0),
                    displayName = reader.GetString(1),
                    capabilities = JsonArray(reader.GetString(2)),
                    credentialBoundary = reader.GetString(3),
                    writesExternally = reader.GetBoolean(4),
                    mode = reader.GetString(5),
                    isReady = reader.GetBoolean(6),
                    circuitOpen = reader.GetBoolean(7),
                    lastProbeAt = NullableDateTimeOffset(reader, 8),
                    lastSuccessfulProbeAt = NullableDateTimeOffset(reader, 9),
                    failureCount = reader.GetInt32(10),
                    detail = reader.GetString(11),
                    revision = reader.GetInt32(12),
                    updatedAt = reader.GetFieldValue<DateTimeOffset>(13),
                    externalExecutionEnabled = false
                });
            }
            return Results.Ok(new { module = Module, adapters, permissions = Permissions(access!), externalExecutionEnabled = false });
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return MigrationRequired();
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "list autonomous adapters");
        }
    }

    private static async Task<IResult> SetAdapterModeAsync(string adapterCode, FullFutureLoopAdapterModeRequest request, HttpContext context)
    {
        var mode = Clean(request.Mode, 20).ToLowerInvariant();
        if (mode is not ("disabled" or "dry_run"))
        {
            return Results.Json(new
            {
                module = Module,
                code = "ACTIVE_ADAPTER_MODE_NOT_AUTHORIZED",
                message = "This source phase permits only disabled or dry_run adapter mode. Active external execution requires a separately approved implementation and activation."
            }, statusCode: StatusCodes.Status423Locked);
        }
        var reason = Clean(request.Reason, 1000);
        if (reason.Length < 3) return Validation("Provide a reason containing at least three characters.");

        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = RequireManage(access);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            var updated = await ExecuteAsync(connection, transaction, """
                UPDATE full_future_loop_automation_adapters
                SET adapter_mode=@mode,is_ready=FALSE,circuit_open=FALSE,detail=@detail,
                    revision_number=revision_number+1,updated_by_user_id=@actor,updated_at=NOW()
                WHERE adapter_code=@code AND revision_number=@revision;
                """, context.RequestAborted,
                ("mode", mode),
                ("detail", mode == "dry_run" ? "Dry-run planning enabled. No external request can be sent." : "Adapter disabled."),
                ("actor", access!.EffectiveUserId),
                ("code", Clean(adapterCode, 80).ToLowerInvariant()),
                ("revision", request.ExpectedRevision));
            if (updated != 1)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Conflict("The adapter changed after it was loaded, or the adapter code does not exist. Refresh and try again.");
            }
            await AppendEvidenceAsync(connection, transaction, null, null, access!, "adapter_mode_changed", "information", new
            {
                adapterCode,
                mode,
                reason,
                externalExecutionEnabled = false
            }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new { module = Module, status = "adapter_mode_updated", adapterCode, mode, externalExecutionEnabled = false });
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "update an autonomous adapter mode");
        }
    }

    private static async Task<IResult> ListRunsAsync(HttpContext context, int? limit = null)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = Require(access, write: false);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();

            var runs = new List<object>();
            await using var command = new NpgsqlCommand(RunSelect + " ORDER BY run.created_at DESC LIMIT @limit;", connection);
            command.Parameters.AddWithValue("limit", Math.Clamp(limit ?? 100, 1, 500));
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted)) runs.Add(RunResponse(ReadRun(reader)));
            return Results.Ok(new { module = Module, runs, permissions = Permissions(access!), scope = Scope(access!) });
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return MigrationRequired();
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "list autonomous runs");
        }
    }

    private static async Task<IResult> GetRunAsync(Guid runId, HttpContext context)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = Require(access, write: false);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();
            var run = await LoadRunAsync(connection, null, runId, false, context.RequestAborted);
            if (run is null) return NotFound("AUTOMATION_RUN_NOT_FOUND", "The requested automation run was not found.");
            return Results.Ok(await RunDetailAsync(connection, run, access!, context.RequestAborted));
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "load an autonomous run");
        }
    }

    private static async Task<IResult> CreateDryRunAsync(FullFutureLoopAutomationSimulationRequest request, HttpContext context)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = Require(access, write: true);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();

            var state = await LoadStateAsync(connection, null, false, context.RequestAborted);
            var stored = await LoadActivePolicyAsync(connection, state.ActivePolicyVersionId, context.RequestAborted);
            var policy = BuildPolicy(stored, state);
            var now = DateTimeOffset.UtcNow;
            var automationRequest = ToAutomationRequest(request, access!, now);
            var decision = FullFutureLoopAutomationPolicyEngine.Evaluate(automationRequest, policy, now);
            var idempotencyKey = Clean(request.IdempotencyKey, 200);
            if (idempotencyKey.Length == 0)
                idempotencyKey = BuildIdempotencyKey(automationRequest, stored.PolicyVersionId);

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            var existing = await LoadRunByIdempotencyAsync(connection, transaction, idempotencyKey, context.RequestAborted);
            if (existing is not null)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.Ok(await RunDetailAsync(connection, existing, access!, context.RequestAborted));
            }

            var runId = Guid.NewGuid();
            var correlationId = Guid.NewGuid();
            var disposition = Disposition(decision.Disposition);
            var status = decision.Disposition switch
            {
                FullFutureLoopAutomationDisposition.Blocked => "blocked",
                FullFutureLoopAutomationDisposition.ApprovalRequired => "approval_required",
                _ => "dry_run_completed"
            };
            var requestedAt = automationRequest.RequestedAt;
            var deadline = requestedAt.Add(policy.MaximumRunDuration);
            var requestJson = JsonSerializer.Serialize(PublicRequest(automationRequest));
            var decisionJson = JsonSerializer.Serialize(decision);

            await using (var insert = new NpgsqlCommand("""
                INSERT INTO full_future_loop_automation_runs(
                    run_id,loop_id,idempotency_key,correlation_id,requested_operation,target_environment,
                    repository,source_commit,risk_class,change_type,policy_version_id,disposition,
                    decision_code,run_status,dry_run,attempt_count,maximum_attempts,deadline_at,
                    request_snapshot,decision_snapshot,requested_by_user_id,requested_at,started_at,
                    completed_at,created_at,updated_at)
                VALUES(
                    @run_id,@loop_id,@idempotency,@correlation,@operation,@environment,
                    @repository,@commit,@risk_class,@change_type,@policy_id,@disposition,
                    @decision_code,@status,TRUE,1,@maximum_attempts,@deadline,
                    @request,@decision,@actor,@requested_at,@started_at,@completed_at,NOW(),NOW());
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("run_id", runId);
                AddNullableGuid(insert, "loop_id", request.LoopId);
                insert.Parameters.AddWithValue("idempotency", idempotencyKey);
                insert.Parameters.AddWithValue("correlation", correlationId);
                insert.Parameters.AddWithValue("operation", automationRequest.Operation);
                insert.Parameters.AddWithValue("environment", automationRequest.Environment);
                insert.Parameters.AddWithValue("repository", automationRequest.Repository);
                insert.Parameters.AddWithValue("commit", automationRequest.SourceCommit);
                insert.Parameters.AddWithValue("risk_class", RiskClassName(automationRequest.RiskClass));
                insert.Parameters.AddWithValue("change_type", automationRequest.ChangeType);
                insert.Parameters.AddWithValue("policy_id", stored.PolicyVersionId);
                insert.Parameters.AddWithValue("disposition", disposition);
                insert.Parameters.AddWithValue("decision_code", decision.DecisionCode);
                insert.Parameters.AddWithValue("status", status);
                insert.Parameters.AddWithValue("maximum_attempts", policy.MaximumStepAttempts);
                insert.Parameters.AddWithValue("deadline", deadline);
                AddJson(insert, "request", requestJson);
                AddJson(insert, "decision", decisionJson);
                insert.Parameters.AddWithValue("actor", access!.EffectiveUserId);
                insert.Parameters.AddWithValue("requested_at", requestedAt);
                insert.Parameters.AddWithValue("started_at", requestedAt);
                insert.Parameters.AddWithValue("completed_at", now);
                await insert.ExecuteNonQueryAsync(context.RequestAborted);
            }

            var steps = PlanSteps(automationRequest, decision);
            foreach (var step in steps)
                await InsertStepAsync(connection, transaction, runId, step, access!.EffectiveUserId, context.RequestAborted);

            if (decision.Disposition == FullFutureLoopAutomationDisposition.ApprovalRequired)
            {
                foreach (var approval in decision.RequiredApprovals)
                    await InsertApprovalAsync(connection, transaction, runId, approval, access!.EffectiveUserId, context.RequestAborted);
            }

            if (decision.Disposition == FullFutureLoopAutomationDisposition.AutoExecute)
                await InsertBlockedOutboxAsync(connection, transaction, runId, automationRequest, context.RequestAborted);

            await AppendEvidenceAsync(connection, transaction, runId, request.LoopId, access!, "automation_dry_run_created", Severity(decision), new
            {
                idempotencyKey,
                correlationId,
                disposition,
                decision.DecisionCode,
                decision.Reasons,
                decision.RequiredApprovals,
                stepCount = steps.Count,
                externalExecutionAttempted = false
            }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);

            var created = await LoadRunAsync(connection, null, runId, false, context.RequestAborted);
            return Results.Created($"/api/full-future-loop/automation/runs/{runId}", await RunDetailAsync(connection, created!, access!, context.RequestAborted));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict("The idempotency key or release manifest already exists. Refresh the current run instead of repeating the request.");
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "create an autonomous dry run");
        }
    }

    private static async Task<IResult> RegisterManifestAsync(Guid runId, FullFutureLoopReleaseManifest manifest, HttpContext context)
    {
        var validation = ValidateManifest(manifest);
        if (validation is not null) return validation;

        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = Require(access, write: true);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            var run = await LoadRunAsync(connection, transaction, runId, true, context.RequestAborted);
            if (run is null) return NotFound("AUTOMATION_RUN_NOT_FOUND", "The requested automation run was not found.");
            if (!run.Repository.Equals(manifest.Repository, StringComparison.OrdinalIgnoreCase)
                || !run.SourceCommit.Equals(manifest.SourceCommit, StringComparison.Ordinal)
                || !run.Environment.Equals(manifest.TargetEnvironment, StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Validation("Manifest repository, source commit, and target environment must match the automation run.");
            }

            var document = JsonSerializer.Serialize(manifest);
            var sha = Sha256(document);
            var manifestId = Guid.NewGuid();
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO full_future_loop_release_manifests(
                    manifest_id,run_id,manifest_version,repository,source_commit,target_environment,
                    manifest_document,manifest_sha256,is_read_only,created_by_user_id,created_at,expires_at)
                VALUES(@id,@run_id,@version,@repository,@commit,@environment,
                    @document,@sha,TRUE,@actor,NOW(),@expires_at);
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("id", manifestId);
                insert.Parameters.AddWithValue("run_id", runId);
                insert.Parameters.AddWithValue("version", Clean(manifest.ManifestVersion, 80));
                insert.Parameters.AddWithValue("repository", Clean(manifest.Repository, 240));
                insert.Parameters.AddWithValue("commit", manifest.SourceCommit);
                insert.Parameters.AddWithValue("environment", Clean(manifest.TargetEnvironment, 32).ToLowerInvariant());
                AddJson(insert, "document", document);
                insert.Parameters.AddWithValue("sha", sha);
                insert.Parameters.AddWithValue("actor", access!.EffectiveUserId);
                insert.Parameters.AddWithValue("expires_at", manifest.ExpiresAt);
                await insert.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await AppendEvidenceAsync(connection, transaction, runId, run.LoopId, access!, "release_manifest_registered", "information", new
            {
                manifestId,
                manifestSha256 = sha,
                artifactCount = manifest.Artifacts.Count,
                migrationCount = manifest.Migrations.Count,
                exactRollbackDigests = manifest.RollbackArtifactDigests.Count,
                externalExecutionAttempted = false
            }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Created($"/api/full-future-loop/automation/runs/{runId}", new
            {
                module = Module,
                status = "release_manifest_registered",
                manifestId,
                manifestSha256 = sha,
                isReadOnly = true,
                externalExecutionEnabled = false
            });
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict("An immutable release manifest is already registered for this run.");
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "register an autonomous release manifest");
        }
    }

    private static async Task<IResult> ListApprovalsAsync(HttpContext context, string? status = null, int? limit = null)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = Require(access, write: false);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();

            var rows = new List<object>();
            await using var command = new NpgsqlCommand("""
                SELECT approval.approval_id,approval.run_id,approval.approval_type,approval.approval_status,
                       approval.requested_by_user_id,approval.decided_by_user_id,approval.decision_reason,
                       approval.revision_number,approval.created_at,approval.updated_at,
                       run.requested_operation,run.target_environment,run.repository,run.source_commit,
                       run.requested_by_user_id
                FROM full_future_loop_automation_approvals approval
                JOIN full_future_loop_automation_runs run ON run.run_id=approval.run_id
                WHERE @status='' OR approval.approval_status=@status
                ORDER BY approval.created_at DESC
                LIMIT @limit;
                """, connection);
            command.Parameters.AddWithValue("status", Clean(status, 24).ToLowerInvariant());
            command.Parameters.AddWithValue("limit", Math.Clamp(limit ?? 100, 1, 500));
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                rows.Add(new
                {
                    approvalId = reader.GetGuid(0),
                    runId = reader.GetGuid(1),
                    approvalType = reader.GetString(2),
                    status = reader.GetString(3),
                    requestedByUserId = reader.GetGuid(4),
                    decidedByUserId = reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5),
                    decisionReason = reader.GetString(6),
                    revision = reader.GetInt32(7),
                    createdAt = reader.GetFieldValue<DateTimeOffset>(8),
                    updatedAt = reader.GetFieldValue<DateTimeOffset>(9),
                    operation = reader.GetString(10),
                    environment = reader.GetString(11),
                    repository = reader.GetString(12),
                    sourceCommit = reader.GetString(13),
                    runRequestedByUserId = reader.GetGuid(14),
                    separationOfDutiesSatisfied = reader.GetGuid(14) != access!.EffectiveUserId
                });
            }
            return Results.Ok(new { module = Module, approvals = rows, permissions = Permissions(access!), scope = Scope(access!) });
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "list autonomous approvals");
        }
    }

    private static async Task<IResult> DecideApprovalAsync(Guid approvalId, FullFutureLoopApprovalDecisionRequest request, HttpContext context)
    {
        var decision = Clean(request.Decision, 20).ToLowerInvariant();
        if (decision is not ("approved" or "rejected")) return Validation("Decision must be approved or rejected.");
        var reason = Clean(request.Reason, 2000);
        if (reason.Length < 3) return Validation("Provide a decision reason containing at least three characters.");

        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = RequireApprove(access);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            Guid runId;
            Guid runRequester;
            Guid? loopId;
            string approvalType;
            await using (var select = new NpgsqlCommand("""
                SELECT approval.run_id,run.requested_by_user_id,run.loop_id,approval.approval_type
                FROM full_future_loop_automation_approvals approval
                JOIN full_future_loop_automation_runs run ON run.run_id=approval.run_id
                WHERE approval.approval_id=@id AND approval.approval_status='pending'
                  AND approval.revision_number=@revision
                FOR UPDATE;
                """, connection, transaction))
            {
                select.Parameters.AddWithValue("id", approvalId);
                select.Parameters.AddWithValue("revision", request.ExpectedRevision);
                await using var reader = await select.ExecuteReaderAsync(context.RequestAborted);
                if (!await reader.ReadAsync(context.RequestAborted))
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return Conflict("The approval changed after it was loaded, is no longer pending, or does not exist.");
                }
                runId = reader.GetGuid(0);
                runRequester = reader.GetGuid(1);
                loopId = reader.IsDBNull(2) ? null : reader.GetGuid(2);
                approvalType = reader.GetString(3);
            }
            if (runRequester == access!.EffectiveUserId)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return EnterpriseGovernanceResults.Forbidden(Module, "Separation of duties prevents the requesting user from approving the same automation run.");
            }

            await using (var update = new NpgsqlCommand("""
                UPDATE full_future_loop_automation_approvals
                SET approval_status=@decision,decided_by_user_id=@actor,decision_reason=@reason,
                    decided_at=NOW(),revision_number=revision_number+1,updated_at=NOW()
                WHERE approval_id=@id AND approval_status='pending' AND revision_number=@revision;
                """, connection, transaction))
            {
                update.Parameters.AddWithValue("decision", decision);
                update.Parameters.AddWithValue("actor", access.EffectiveUserId);
                update.Parameters.AddWithValue("reason", reason);
                update.Parameters.AddWithValue("id", approvalId);
                update.Parameters.AddWithValue("revision", request.ExpectedRevision);
                if (await update.ExecuteNonQueryAsync(context.RequestAborted) != 1)
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return Conflict("The approval changed before the decision was saved.");
                }
            }

            var pending = await ScalarIntAsync(connection, transaction, """
                SELECT COUNT(*)::integer FROM full_future_loop_automation_approvals
                WHERE run_id=@run_id AND approval_status='pending';
                """, "run_id", runId, context.RequestAborted);
            var rejected = await ScalarIntAsync(connection, transaction, """
                SELECT COUNT(*)::integer FROM full_future_loop_automation_approvals
                WHERE run_id=@run_id AND approval_status='rejected';
                """, "run_id", runId, context.RequestAborted);
            var runStatus = rejected > 0 ? "blocked" : pending == 0 ? "dry_run_completed" : "approval_required";
            await ExecuteAsync(connection, transaction, """
                UPDATE full_future_loop_automation_runs
                SET run_status=@status,updated_at=NOW()
                WHERE run_id=@run_id;
                """, context.RequestAborted, ("status", runStatus), ("run_id", runId));

            await AppendEvidenceAsync(connection, transaction, runId, loopId, access, "automation_approval_decided", decision == "approved" ? "information" : "warning", new
            {
                approvalId,
                approvalType,
                decision,
                reason,
                pendingApprovals = pending,
                rejectedApprovals = rejected,
                runStatus,
                externalExecutionAttempted = false
            }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new { module = Module, status = "approval_decided", approvalId, decision, runStatus, externalExecutionEnabled = false });
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "decide an autonomous approval");
        }
    }

    private static async Task<IResult> UpdateRuntimeAsync(FullFutureLoopAutomationRuntimeRequest request, HttpContext context)
    {
        var reason = Clean(request.Reason, 2000);
        if (reason.Length < 3) return Validation("Provide a reason containing at least three characters.");

        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = RequireManage(access);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            var state = await LoadStateAsync(connection, transaction, true, context.RequestAborted);
            if (state.Revision != request.ExpectedRevision)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Conflict("The runtime state changed after it was loaded. Refresh and try again.");
            }
            await using (var update = new NpgsqlCommand("""
                UPDATE full_future_loop_automation_state
                SET automation_enabled=@enabled,global_kill_switch=@kill_switch,dry_run_only=TRUE,
                    last_reason=@reason,revision_number=revision_number+1,
                    updated_by_user_id=@actor,updated_at=NOW()
                WHERE state_id=1 AND revision_number=@revision;
                """, connection, transaction))
            {
                update.Parameters.AddWithValue("enabled", request.AutomationEnabled);
                update.Parameters.AddWithValue("kill_switch", request.GlobalKillSwitch);
                update.Parameters.AddWithValue("reason", reason);
                update.Parameters.AddWithValue("actor", access!.EffectiveUserId);
                update.Parameters.AddWithValue("revision", request.ExpectedRevision);
                if (await update.ExecuteNonQueryAsync(context.RequestAborted) != 1)
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return Conflict("The runtime state changed before the update completed.");
                }
            }
            await AppendEvidenceAsync(connection, transaction, null, null, access!, "automation_runtime_changed", "warning", new
            {
                automationEnabled = request.AutomationEnabled,
                globalKillSwitch = request.GlobalKillSwitch,
                dryRunOnly = true,
                reason,
                externalExecutionEnabled = false
            }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            var current = await LoadStateAsync(connection, null, false, context.RequestAborted);
            return Results.Ok(new { module = Module, status = "runtime_updated", runtime = StateResponse(current), externalExecutionEnabled = false });
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "update the autonomous runtime");
        }
    }

    private static async Task<IResult> ListEvidenceAsync(HttpContext context, Guid? runId = null, int? limit = null)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = Require(access, write: false);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();

            var evidence = new List<object>();
            await using var command = new NpgsqlCommand("""
                SELECT evidence_id,run_id,loop_id,event_code,severity,actual_actor_user_id,
                       effective_actor_user_id,evidence_document::text,occurred_at
                FROM full_future_loop_automation_evidence
                WHERE @run_id IS NULL OR run_id=@run_id
                ORDER BY occurred_at DESC
                LIMIT @limit;
                """, connection);
            AddNullableGuid(command, "run_id", runId);
            command.Parameters.AddWithValue("limit", Math.Clamp(limit ?? 200, 1, 1000));
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                evidence.Add(new
                {
                    evidenceId = reader.GetGuid(0),
                    runId = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1),
                    loopId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2),
                    eventCode = reader.GetString(3),
                    severity = reader.GetString(4),
                    actualActorUserId = reader.GetGuid(5),
                    effectiveActorUserId = reader.GetGuid(6),
                    document = JsonObject(reader.GetString(7)),
                    occurredAt = reader.GetFieldValue<DateTimeOffset>(8)
                });
            }
            return Results.Ok(new { module = Module, evidence, appendOnly = true, permissions = Permissions(access!), scope = Scope(access!) });
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "list autonomous evidence");
        }
    }

    private static async Task<object> RunDetailAsync(NpgsqlConnection connection, AutomationRun run, EnterpriseGovernanceAccess access, CancellationToken cancellationToken)
    {
        var steps = new List<object>();
        await using (var command = new NpgsqlCommand("""
            SELECT step_id,step_code,sequence_number,adapter_code,step_status,attempt_number,
                   input_document::text,output_document::text,created_at,updated_at
            FROM full_future_loop_automation_steps
            WHERE run_id=@run_id ORDER BY sequence_number;
            """, connection))
        {
            command.Parameters.AddWithValue("run_id", run.RunId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                steps.Add(new
                {
                    stepId = reader.GetGuid(0),
                    code = reader.GetString(1),
                    sequence = reader.GetInt32(2),
                    adapterCode = reader.IsDBNull(3) ? null : reader.GetString(3),
                    status = reader.GetString(4),
                    attempt = reader.GetInt32(5),
                    input = JsonObject(reader.GetString(6)),
                    output = JsonObject(reader.GetString(7)),
                    createdAt = reader.GetFieldValue<DateTimeOffset>(8),
                    updatedAt = reader.GetFieldValue<DateTimeOffset>(9)
                });
            }
        }

        var approvals = new List<object>();
        await using (var command = new NpgsqlCommand("""
            SELECT approval_id,approval_type,approval_status,requested_by_user_id,
                   decided_by_user_id,decision_reason,revision_number,created_at,updated_at
            FROM full_future_loop_automation_approvals
            WHERE run_id=@run_id ORDER BY created_at;
            """, connection))
        {
            command.Parameters.AddWithValue("run_id", run.RunId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                approvals.Add(new
                {
                    approvalId = reader.GetGuid(0),
                    type = reader.GetString(1),
                    status = reader.GetString(2),
                    requestedByUserId = reader.GetGuid(3),
                    decidedByUserId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4),
                    decisionReason = reader.GetString(5),
                    revision = reader.GetInt32(6),
                    createdAt = reader.GetFieldValue<DateTimeOffset>(7),
                    updatedAt = reader.GetFieldValue<DateTimeOffset>(8),
                    separationOfDutiesSatisfied = run.RequestedByUserId != access.EffectiveUserId
                });
            }
        }

        object? manifest = null;
        await using (var command = new NpgsqlCommand("""
            SELECT manifest_id,manifest_version,manifest_sha256,manifest_document::text,
                   is_read_only,created_at,expires_at
            FROM full_future_loop_release_manifests WHERE run_id=@run_id;
            """, connection))
        {
            command.Parameters.AddWithValue("run_id", run.RunId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                manifest = new
                {
                    manifestId = reader.GetGuid(0),
                    version = reader.GetString(1),
                    sha256 = reader.GetString(2),
                    document = JsonObject(reader.GetString(3)),
                    isReadOnly = reader.GetBoolean(4),
                    createdAt = reader.GetFieldValue<DateTimeOffset>(5),
                    expiresAt = reader.GetFieldValue<DateTimeOffset>(6)
                };
            }
        }

        return new
        {
            module = Module,
            contractVersion = ContractVersion,
            run = RunResponse(run),
            steps,
            approvals,
            manifest,
            permissions = Permissions(access),
            scope = Scope(access),
            externalExecutionEnabled = false
        };
    }

    private static async Task<AutomationRun?> LoadRunAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid runId, bool lockRow, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(RunSelect + " WHERE run.run_id=@run_id" + (lockRow ? " FOR UPDATE" : string.Empty) + ";", connection, transaction);
        command.Parameters.AddWithValue("run_id", runId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRun(reader) : null;
    }

    private static async Task<AutomationRun?> LoadRunByIdempotencyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string key, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(RunSelect + " WHERE run.idempotency_key=@key FOR UPDATE;", connection, transaction);
        command.Parameters.AddWithValue("key", key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRun(reader) : null;
    }

    private static AutomationRun ReadRun(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.IsDBNull(1) ? null : reader.GetGuid(1),
        reader.GetString(2),
        reader.GetGuid(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetString(7),
        reader.GetString(8),
        reader.GetString(9),
        reader.GetGuid(10),
        reader.GetString(11),
        reader.GetString(12),
        reader.GetString(13),
        reader.GetBoolean(14),
        reader.GetInt32(15),
        reader.GetInt32(16),
        NullableDateTimeOffset(reader, 17),
        NullableDateTimeOffset(reader, 18),
        reader.GetGuid(19),
        reader.GetFieldValue<DateTimeOffset>(20),
        NullableDateTimeOffset(reader, 21),
        NullableDateTimeOffset(reader, 22),
        reader.GetFieldValue<DateTimeOffset>(23),
        reader.GetFieldValue<DateTimeOffset>(24));

    private static object RunResponse(AutomationRun run) => new
    {
        runId = run.RunId,
        loopId = run.LoopId,
        run.IdempotencyKey,
        run.CorrelationId,
        operation = run.Operation,
        environment = run.Environment,
        run.Repository,
        sourceCommit = run.SourceCommit,
        riskClass = run.RiskClass,
        changeType = run.ChangeType,
        policyVersionId = run.PolicyVersionId,
        disposition = run.Disposition,
        decisionCode = run.DecisionCode,
        status = run.Status,
        dryRun = run.DryRun,
        run.AttemptCount,
        run.MaximumAttempts,
        run.LeaseExpiresAt,
        run.DeadlineAt,
        run.RequestedByUserId,
        run.RequestedAt,
        run.StartedAt,
        run.CompletedAt,
        run.CreatedAt,
        run.UpdatedAt,
        externalExecutionAttempted = false
    };

    private static async Task InsertStepAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid runId, PlannedStep step, Guid actor, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO full_future_loop_automation_steps(
                step_id,run_id,step_code,sequence_number,adapter_code,step_status,
                attempt_number,input_document,output_document,created_by_user_id,created_at,updated_at)
            VALUES(@id,@run_id,@code,@sequence,@adapter,@status,1,@input,@output,@actor,NOW(),NOW());
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("code", step.Code);
        command.Parameters.AddWithValue("sequence", step.Sequence);
        AddNullableText(command, "adapter", step.AdapterCode);
        command.Parameters.AddWithValue("status", step.Status);
        AddJson(command, "input", "{}");
        AddJson(command, "output", JsonSerializer.Serialize(step.Output));
        command.Parameters.AddWithValue("actor", actor);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertApprovalAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid runId, string type, Guid requester, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO full_future_loop_automation_approvals(
                approval_id,run_id,approval_type,approval_status,requested_by_user_id,
                decision_reason,revision_number,created_at,updated_at)
            VALUES(@id,@run_id,@type,'pending',@requester,'',1,NOW(),NOW())
            ON CONFLICT(run_id,approval_type) DO NOTHING;
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("type", Clean(type, 100).ToLowerInvariant());
        command.Parameters.AddWithValue("requester", requester);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertBlockedOutboxAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid runId, FullFutureLoopAutomationRequest request, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO full_future_loop_outbox(
                outbox_id,run_id,message_type,adapter_code,idempotency_key,payload,
                outbox_status,attempt_count,last_error,created_at,updated_at)
            VALUES(@id,@run_id,'dry_run_external_execution_plan',@adapter,@idempotency,@payload,
                'blocked',0,'Dry-run only. No external dispatcher is installed.',NOW(),NOW());
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("run_id", runId);
        AddNullableText(command, "adapter", AdapterFor(request.Operation));
        command.Parameters.AddWithValue("idempotency", $"{runId:N}:{request.Operation}:dry-run");
        AddJson(command, "payload", JsonSerializer.Serialize(new
        {
            request.Operation,
            request.Environment,
            request.Repository,
            request.SourceCommit,
            externalExecutionAttempted = false
        }));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AppendEvidenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid? runId,
        Guid? loopId,
        EnterpriseGovernanceAccess access,
        string eventCode,
        string severity,
        object document,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO full_future_loop_automation_evidence(
                evidence_id,run_id,loop_id,event_code,severity,actual_actor_user_id,
                effective_actor_user_id,evidence_document,occurred_at)
            VALUES(@id,@run_id,@loop_id,@event_code,@severity,@actual_actor,@effective_actor,@document,NOW());
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        AddNullableGuid(command, "run_id", runId);
        AddNullableGuid(command, "loop_id", loopId);
        command.Parameters.AddWithValue("event_code", Clean(eventCode, 100));
        command.Parameters.AddWithValue("severity", Clean(severity, 20));
        command.Parameters.AddWithValue("actual_actor", access.ActualUserId);
        command.Parameters.AddWithValue("effective_actor", access.EffectiveUserId);
        AddJson(command, "document", JsonSerializer.Serialize(document));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IReadOnlyList<PlannedStep> PlanSteps(FullFutureLoopAutomationRequest request, FullFutureLoopAutomationDecision decision)
    {
        var blocked = decision.Disposition == FullFutureLoopAutomationDisposition.Blocked;
        var approval = decision.Disposition == FullFutureLoopAutomationDisposition.ApprovalRequired;
        return new[]
        {
            new PlannedStep("intake_normalization", 10, null, "completed", new { request.RequestId, request.Operation, request.Environment, request.Repository }),
            new PlannedStep("policy_evaluation", 20, null, "completed", new { decision.DecisionCode, disposition = Disposition(decision.Disposition), decision.Reasons }),
            new PlannedStep("evidence_validation", 30, null, blocked ? "skipped" : "completed", new
            {
                request.CanaryPassed,
                request.CleanupProven,
                request.ExactArtifactDigestsPresent,
                request.SbomPresent,
                request.ProvenancePresent,
                request.SignaturesVerified
            }),
            new PlannedStep("approval_gate", 40, null, blocked ? "skipped" : approval ? "waiting_approval" : "completed", new { decision.RequiredApprovals }),
            new PlannedStep("external_execution_plan", 50, AdapterFor(request.Operation), blocked || approval ? "skipped" : "dry_run_completed", new
            {
                request.Operation,
                request.Environment,
                externalExecutionAttempted = false
            }),
            new PlannedStep("verification_plan", 60, request.Operation is "deploy" or "verify" or "rollback" ? "azure_observability" : null, blocked ? "skipped" : "dry_run_completed", new
            {
                request.VerificationSuitePassed,
                request.RollbackTargetProven,
                externalExecutionAttempted = false
            })
        };
    }

    private static string? AdapterFor(string operation) => operation switch
    {
        "observe" or "verify" => "azure_observability",
        "create_issue" or "propose_repair" => "module_076",
        "dispatch_ci" => "github",
        "run_canary" => "canary",
        "deploy" or "rollback" => "azure_container_apps",
        "notify" => "module_065",
        "classify" => "celar_ai",
        _ => null
    };

    private static string Severity(FullFutureLoopAutomationDecision decision) => decision.Disposition switch
    {
        FullFutureLoopAutomationDisposition.Blocked => "warning",
        FullFutureLoopAutomationDisposition.ApprovalRequired => "notice",
        _ => "information"
    };

    private static FullFutureLoopAutomationRequest ToAutomationRequest(FullFutureLoopAutomationSimulationRequest request, EnterpriseGovernanceAccess access, DateTimeOffset now)
    {
        var operation = Clean(request.Operation, 40, "observe").ToLowerInvariant();
        var environment = Clean(request.Environment, 32, "test").ToLowerInvariant();
        var repository = Clean(request.Repository, 240, DefaultRepository);
        var sourceCommit = Clean(request.SourceCommit, 80).ToLowerInvariant();
        var requestedBy = Clean(request.RequestedByAuthority, 240, access.Email.Length > 0 ? access.Email : access.DisplayName);
        return new FullFutureLoopAutomationRequest(
            Guid.NewGuid(),
            request.LoopId,
            operation,
            environment,
            repository,
            sourceCommit,
            ParseRisk(request.RiskClass),
            Clean(request.ChangeType, 80, "application").ToLowerInvariant(),
            request.IncludesMigration,
            request.IncludesSecurityChange,
            request.IncludesInfrastructureChange,
            request.IncludesSecretChange,
            request.IsEmergencyRollback,
            request.ProductionApprovalSatisfied,
            request.MigrationApprovalSatisfied,
            request.SecurityApprovalSatisfied,
            request.InfrastructureApprovalSatisfied,
            request.SecretChangeApprovalSatisfied,
            request.CanaryPassed,
            request.CleanupProven,
            request.VerificationSuitePassed,
            request.RollbackTargetProven,
            request.ExactArtifactDigestsPresent,
            request.SbomPresent,
            request.ProvenancePresent,
            request.SignaturesVerified,
            request.EvidenceGeneratedAt ?? now,
            now,
            requestedBy,
            request.RequestedByAi);
    }

    private static FullFutureLoopRiskClass ParseRisk(string? value) => Clean(value, 20, "normal").ToLowerInvariant() switch
    {
        "routine" => FullFutureLoopRiskClass.Routine,
        "high" => FullFutureLoopRiskClass.High,
        "critical" => FullFutureLoopRiskClass.Critical,
        _ => FullFutureLoopRiskClass.Normal
    };

    private static string RiskClassName(FullFutureLoopRiskClass value) => value.ToString().ToLowerInvariant();

    private static object PublicRequest(FullFutureLoopAutomationRequest request) => new
    {
        request.RequestId,
        request.LoopId,
        request.Operation,
        request.Environment,
        request.Repository,
        request.SourceCommit,
        riskClass = RiskClassName(request.RiskClass),
        request.ChangeType,
        request.IncludesMigration,
        request.IncludesSecurityChange,
        request.IncludesInfrastructureChange,
        request.IncludesSecretChange,
        request.IsEmergencyRollback,
        request.ProductionApprovalSatisfied,
        request.MigrationApprovalSatisfied,
        request.SecurityApprovalSatisfied,
        request.InfrastructureApprovalSatisfied,
        request.SecretChangeApprovalSatisfied,
        request.CanaryPassed,
        request.CleanupProven,
        request.VerificationSuitePassed,
        request.RollbackTargetProven,
        request.ExactArtifactDigestsPresent,
        request.SbomPresent,
        request.ProvenancePresent,
        request.SignaturesVerified,
        request.EvidenceGeneratedAt,
        request.RequestedAt,
        request.RequestedByAuthority,
        request.RequestedByAi
    };

    private static string BuildIdempotencyKey(FullFutureLoopAutomationRequest request, Guid policyVersionId)
    {
        var material = string.Join('|', request.LoopId, request.Operation, request.Environment, request.Repository,
            request.SourceCommit, request.ChangeType, RiskClassName(request.RiskClass), policyVersionId,
            request.IncludesMigration, request.IncludesSecurityChange, request.IncludesInfrastructureChange,
            request.IncludesSecretChange);
        return $"ffl083:{Sha256(material)}";
    }

    private static async Task<AutomationState> LoadStateAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, bool lockRow, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT automation_enabled,global_kill_switch,dry_run_only,active_policy_version_id,
                   revision_number,updated_by_user_id,updated_at
            FROM full_future_loop_automation_state WHERE state_id=1
            """ + (lockRow ? " FOR UPDATE;" : ";"), connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Module 083 autonomous runtime state is missing.");
        return new AutomationState(
            reader.GetBoolean(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5),
            reader.GetFieldValue<DateTimeOffset>(6));
    }

    private static async Task<StoredPolicy> LoadActivePolicyAsync(NpgsqlConnection connection, Guid? policyId, CancellationToken cancellationToken)
    {
        if (!policyId.HasValue) throw new InvalidOperationException("Module 083 has no active automation policy.");
        await using var command = new NpgsqlCommand("""
            SELECT policy_version_id,policy_version,policy_document::text,policy_sha256,created_at
            FROM full_future_loop_automation_policies WHERE policy_version_id=@id;
            """, connection);
        command.Parameters.AddWithValue("id", policyId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("The active Module 083 automation policy was not found.");
        return new StoredPolicy(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4));
    }

    private static FullFutureLoopAutomationPolicy BuildPolicy(StoredPolicy stored, AutomationState state)
    {
        var baseline = FullFutureLoopAutomationPolicy.EnterpriseDefault();
        using var document = JsonDocument.Parse(stored.PolicyDocument);
        var root = document.RootElement;
        return baseline with
        {
            PolicyVersion = stored.PolicyVersion,
            Enabled = state.AutomationEnabled,
            GlobalKillSwitch = state.GlobalKillSwitch,
            AllowedRepositories = Set(root, "allowedRepositories", baseline.AllowedRepositories),
            AllowedEnvironments = Set(root, "allowedEnvironments", baseline.AllowedEnvironments),
            AllowedOperations = Set(root, "allowedOperations", baseline.AllowedOperations),
            AllowAutomaticTestDeployment = Bool(root, "allowAutomaticTestDeployment", baseline.AllowAutomaticTestDeployment),
            AllowAutomaticTestRollback = Bool(root, "allowAutomaticTestRollback", baseline.AllowAutomaticTestRollback),
            AllowAutomaticProductionDeployment = Bool(root, "allowAutomaticProductionDeployment", baseline.AllowAutomaticProductionDeployment),
            AllowAutomaticProductionRollback = Bool(root, "allowAutomaticProductionRollback", baseline.AllowAutomaticProductionRollback),
            RequireProductionApproval = Bool(root, "requireProductionApproval", baseline.RequireProductionApproval),
            RequireMigrationApproval = Bool(root, "requireMigrationApproval", baseline.RequireMigrationApproval),
            RequireSecurityApproval = Bool(root, "requireSecurityApproval", baseline.RequireSecurityApproval),
            RequireInfrastructureApproval = Bool(root, "requireInfrastructureApproval", baseline.RequireInfrastructureApproval),
            RequireSecretChangeApproval = Bool(root, "requireSecretChangeApproval", baseline.RequireSecretChangeApproval),
            MaximumConcurrentRuns = Int(root, "maximumConcurrentRuns", baseline.MaximumConcurrentRuns),
            MaximumStepAttempts = Int(root, "maximumStepAttempts", baseline.MaximumStepAttempts),
            MaximumRunDuration = TimeSpan.FromMinutes(Int(root, "maximumRunDurationMinutes", (int)baseline.MaximumRunDuration.TotalMinutes)),
            EvidenceMaximumAge = TimeSpan.FromMinutes(Int(root, "evidenceMaximumAgeMinutes", (int)baseline.EvidenceMaximumAge.TotalMinutes)),
            ApprovedProductionChangeTypes = Set(root, "approvedProductionChangeTypes", baseline.ApprovedProductionChangeTypes)
        };
    }

    private static object PolicyResponse(StoredPolicy stored, FullFutureLoopAutomationPolicy policy) => new
    {
        stored.PolicyVersionId,
        policy.PolicyVersion,
        stored.PolicySha256,
        stored.CreatedAt,
        policy.Enabled,
        policy.GlobalKillSwitch,
        allowedRepositories = policy.AllowedRepositories.OrderBy(value => value).ToArray(),
        allowedEnvironments = policy.AllowedEnvironments.OrderBy(value => value).ToArray(),
        allowedOperations = policy.AllowedOperations.OrderBy(value => value).ToArray(),
        policy.AllowAutomaticTestDeployment,
        policy.AllowAutomaticTestRollback,
        policy.AllowAutomaticProductionDeployment,
        policy.AllowAutomaticProductionRollback,
        policy.RequireProductionApproval,
        policy.RequireMigrationApproval,
        policy.RequireSecurityApproval,
        policy.RequireInfrastructureApproval,
        policy.RequireSecretChangeApproval,
        policy.MaximumConcurrentRuns,
        policy.MaximumStepAttempts,
        maximumRunDurationMinutes = (int)policy.MaximumRunDuration.TotalMinutes,
        evidenceMaximumAgeMinutes = (int)policy.EvidenceMaximumAge.TotalMinutes,
        approvedProductionChangeTypes = policy.ApprovedProductionChangeTypes.OrderBy(value => value).ToArray(),
        externalExecutionEnabled = false
    };

    private static object StateResponse(AutomationState state) => new
    {
        automationEnabled = state.AutomationEnabled,
        globalKillSwitch = state.GlobalKillSwitch,
        dryRunOnly = state.DryRunOnly,
        activePolicyVersionId = state.ActivePolicyVersionId,
        revision = state.Revision,
        updatedByUserId = state.UpdatedByUserId,
        updatedAt = state.UpdatedAt,
        externalExecutionEnabled = false
    };

    private static async Task<object> LoadCountsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
              (SELECT COUNT(*)::bigint FROM full_future_loop_automation_runs),
              (SELECT COUNT(*)::bigint FROM full_future_loop_automation_runs WHERE run_status='blocked'),
              (SELECT COUNT(*)::bigint FROM full_future_loop_automation_runs WHERE run_status='approval_required'),
              (SELECT COUNT(*)::bigint FROM full_future_loop_automation_runs WHERE run_status='dry_run_completed'),
              (SELECT COUNT(*)::bigint FROM full_future_loop_automation_approvals WHERE approval_status='pending'),
              (SELECT COUNT(*)::bigint FROM full_future_loop_release_manifests),
              (SELECT COUNT(*)::bigint FROM full_future_loop_automation_adapters WHERE adapter_mode='dry_run'),
              (SELECT COUNT(*)::bigint FROM full_future_loop_outbox WHERE outbox_status='blocked');
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new
        {
            totalRuns = reader.GetInt64(0),
            blockedRuns = reader.GetInt64(1),
            approvalRequiredRuns = reader.GetInt64(2),
            completedDryRuns = reader.GetInt64(3),
            pendingApprovals = reader.GetInt64(4),
            releaseManifests = reader.GetInt64(5),
            dryRunAdapters = reader.GetInt64(6),
            blockedOutboxMessages = reader.GetInt64(7)
        };
    }

    private static IResult? ValidateManifest(FullFutureLoopReleaseManifest? manifest)
    {
        if (manifest is null) return Validation("A release manifest body is required.");
        if (string.IsNullOrWhiteSpace(manifest.ManifestVersion)) return Validation("Manifest version is required.");
        if (string.IsNullOrWhiteSpace(manifest.Repository)) return Validation("Repository is required.");
        if (!IsCommit(manifest.SourceCommit)) return Validation("An exact lowercase 40-character source commit is required.");
        if (!FullFutureLoopAutomationFoundation.SupportedEnvironments.Contains(manifest.TargetEnvironment)) return Validation("Target environment must be canary, test, or production.");
        if (manifest.ExpiresAt <= manifest.CreatedAt) return Validation("Manifest expiry must be later than creation time.");
        if (manifest.Artifacts is null || manifest.Artifacts.Count == 0) return Validation("At least one immutable release artifact is required.");
        if (manifest.Artifacts.Any(artifact => !FullFutureLoopAutomationPolicyEngine.IsValidDigest(artifact.Digest))) return Validation("Every artifact must include an exact sha256 digest.");
        if (manifest.Artifacts.Any(artifact => string.IsNullOrWhiteSpace(artifact.SbomReference) || string.IsNullOrWhiteSpace(artifact.ProvenanceReference) || string.IsNullOrWhiteSpace(artifact.SignatureReference)))
            return Validation("Every artifact requires SBOM, provenance, and signature evidence references.");
        if (manifest.CanaryEvidenceReferences is null || manifest.CanaryEvidenceReferences.Count == 0) return Validation("Passing canary evidence is required.");
        if (manifest.VerificationEvidenceReferences is null || manifest.VerificationEvidenceReferences.Count == 0) return Validation("Verification evidence is required.");
        if (manifest.RollbackArtifactDigests is null || manifest.RollbackArtifactDigests.Count == 0 || manifest.RollbackArtifactDigests.Any(value => !FullFutureLoopAutomationPolicyEngine.IsValidDigest(value)))
            return Validation("At least one exact prior rollback digest is required.");
        if (string.IsNullOrWhiteSpace(manifest.ConfigurationFingerprint)) return Validation("A non-secret configuration fingerprint is required.");
        return null;
    }

    private static bool IsCommit(string? value) => value?.Length == 40 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool Bool(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static int Int(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : fallback;

    private static IReadOnlySet<string> Set(JsonElement root, string name, IReadOnlySet<string> fallback)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) return fallback;
        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => item.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static object JsonObject(string value)
    {
        try { return JsonSerializer.Deserialize<JsonElement>(value); }
        catch { return new { }; }
    }

    private static string[] JsonArray(string value)
    {
        try { return JsonSerializer.Deserialize<string[]>(value) ?? []; }
        catch { return []; }
    }

    private static void AddJson(NpgsqlCommand command, string name, string value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb) { Value = value });

    private static void AddNullableGuid(NpgsqlCommand command, string name, Guid? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Uuid) { Value = value.HasValue ? (object)value.Value : DBNull.Value });

    private static void AddNullableText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Text) { Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : (object)value });

    private static DateTimeOffset? NullableDateTimeOffset(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    private static async Task<int> ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ScalarIntAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        string parameterName,
        object parameterValue,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(parameterName, parameterValue);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static string Clean(string? value, int maximum, string fallback = "")
    {
        var cleaned = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return cleaned.Length <= maximum ? cleaned : cleaned[..maximum];
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool CanView(EnterpriseGovernanceAccess access) =>
        access.IsBroadScope
        || access.Roles.Overlaps(ViewRoles)
        || access.Permissions.Contains("VIEW_FULL_FUTURE_LOOP_AUTOMATION_083")
        || access.Permissions.Contains("MANAGE_ALL");

    private static bool CanOperate(EnterpriseGovernanceAccess access) =>
        !access.IsViewAs && (access.CanManageOrganization
        || access.Roles.Overlaps(OperateRoles)
        || access.Permissions.Contains("OPERATE_FULL_FUTURE_LOOP_AUTOMATION_083")
        || access.Permissions.Contains("MANAGE_FULL_FUTURE_LOOP_AUTOMATION_083")
        || access.Permissions.Contains("MANAGE_ALL"));

    private static bool CanManage(EnterpriseGovernanceAccess access) =>
        !access.IsViewAs && (access.CanManageOrganization
        || access.Roles.Overlaps(ManageRoles)
        || access.Permissions.Contains("MANAGE_FULL_FUTURE_LOOP_AUTOMATION_083")
        || access.Permissions.Contains("MANAGE_ALL"));

    private static bool CanApprove(EnterpriseGovernanceAccess access) =>
        !access.IsViewAs && (access.CanManageOrganization
        || access.Roles.Overlaps(ManageRoles)
        || access.Permissions.Contains("APPROVE_FULL_FUTURE_LOOP_AUTOMATION_083")
        || access.Permissions.Contains("MANAGE_ALL"));

    private static IResult? Require(EnterpriseGovernanceAccess? access, bool write)
    {
        if (access is null) return EnterpriseGovernanceResults.Unauthorized(Module);
        if (!CanView(access)) return EnterpriseGovernanceResults.Forbidden(Module, "Your effective role cannot view the autonomous Full Future Loop control plane.");
        if (write && access.IsViewAs) return EnterpriseGovernanceResults.ViewAsReadOnly(Module);
        if (write && !CanOperate(access)) return EnterpriseGovernanceResults.Forbidden(Module, "Your effective role cannot operate autonomous dry runs.");
        return null;
    }

    private static IResult? RequireManage(EnterpriseGovernanceAccess? access)
    {
        var failure = Require(access, write: true);
        if (failure is not null) return failure;
        return CanManage(access!)
            ? null
            : EnterpriseGovernanceResults.Forbidden(Module, "Autonomous policy, adapter, and runtime controls require administrator or release-manager authority.");
    }

    private static IResult? RequireApprove(EnterpriseGovernanceAccess? access)
    {
        var failure = Require(access, write: true);
        if (failure is not null) return failure;
        return CanApprove(access!)
            ? null
            : EnterpriseGovernanceResults.Forbidden(Module, "This approval requires explicitly assigned autonomous approval authority.");
    }

    private static object Permissions(EnterpriseGovernanceAccess access) => new
    {
        canView = CanView(access),
        canOperateDryRuns = CanOperate(access),
        canManage = CanManage(access),
        canApprove = CanApprove(access),
        isViewAs = access.IsViewAs,
        activeExternalAdapterModeAllowed = false,
        productionExecutionAllowed = false
    };

    private static object Scope(EnterpriseGovernanceAccess access) => new
    {
        actualUserId = access.ActualUserId,
        effectiveUserId = access.EffectiveUserId,
        access.DisplayName,
        access.Email,
        roles = access.Roles.OrderBy(value => value).ToArray(),
        isViewAs = access.IsViewAs
    };

    private static IResult MigrationRequired() => Results.Json(new
    {
        module = Module,
        code = "MODULE_083_AUTOMATION_MIGRATION_REQUIRED",
        message = "Migration 083 must be applied before the autonomous control plane can persist policies, runs, approvals, manifests, and evidence.",
        migration = Migration,
        externalExecutionEnabled = false
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult Validation(string message) =>
        Results.BadRequest(new { module = Module, code = "MODULE_083_AUTOMATION_VALIDATION_FAILED", message });

    private static IResult Conflict(string message) =>
        Results.Conflict(new { module = Module, code = "MODULE_083_AUTOMATION_CONFLICT", message });

    private static IResult NotFound(string code, string message) =>
        Results.NotFound(new { module = Module, code, message });

    private static async Task<bool> RuntimeReadyAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
              EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='083_module_083_autonomous_control_plane')
              AND to_regclass('public.full_future_loop_automation_state') IS NOT NULL
              AND to_regclass('public.full_future_loop_automation_policies') IS NOT NULL
              AND to_regclass('public.full_future_loop_automation_adapters') IS NOT NULL
              AND to_regclass('public.full_future_loop_automation_runs') IS NOT NULL
              AND to_regclass('public.full_future_loop_automation_steps') IS NOT NULL
              AND to_regclass('public.full_future_loop_automation_approvals') IS NOT NULL
              AND to_regclass('public.full_future_loop_release_manifests') IS NOT NULL
              AND to_regclass('public.full_future_loop_automation_evidence') IS NOT NULL
              AND to_regclass('public.full_future_loop_outbox') IS NOT NULL;
            """, connection);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private const string RunSelect = """
        SELECT run.run_id,run.loop_id,run.idempotency_key,run.correlation_id,run.requested_operation,
               run.target_environment,run.repository,run.source_commit,run.risk_class,run.change_type,
               run.policy_version_id,run.disposition,run.decision_code,run.run_status,run.dry_run,
               run.attempt_count,run.maximum_attempts,run.lease_expires_at,run.deadline_at,
               run.requested_by_user_id,run.requested_at,run.started_at,run.completed_at,
               run.created_at,run.updated_at
        FROM full_future_loop_automation_runs run
        """;

    private static string Disposition(FullFutureLoopAutomationDisposition value) => value switch
    {
        FullFutureLoopAutomationDisposition.AutoExecute => "auto_execute",
        FullFutureLoopAutomationDisposition.ApprovalRequired => "approval_required",
        _ => "blocked"
    };

    private sealed record AutomationState(
        bool AutomationEnabled,
        bool GlobalKillSwitch,
        bool DryRunOnly,
        Guid? ActivePolicyVersionId,
        int Revision,
        Guid? UpdatedByUserId,
        DateTimeOffset UpdatedAt);

    private sealed record StoredPolicy(
        Guid PolicyVersionId,
        string PolicyVersion,
        string PolicyDocument,
        string PolicySha256,
        DateTimeOffset CreatedAt);

    private sealed record PlannedStep(
        string Code,
        int Sequence,
        string? AdapterCode,
        string Status,
        object Output);

    private sealed record AutomationRun(
        Guid RunId,
        Guid? LoopId,
        string IdempotencyKey,
        Guid CorrelationId,
        string Operation,
        string Environment,
        string Repository,
        string SourceCommit,
        string RiskClass,
        string ChangeType,
        Guid PolicyVersionId,
        string Disposition,
        string DecisionCode,
        string Status,
        bool DryRun,
        int AttemptCount,
        int MaximumAttempts,
        DateTimeOffset? LeaseExpiresAt,
        DateTimeOffset? DeadlineAt,
        Guid RequestedByUserId,
        DateTimeOffset RequestedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
