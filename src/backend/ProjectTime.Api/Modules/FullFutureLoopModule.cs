using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public sealed record FullFutureLoopCreateRequest(
    string? Title,
    string? Description,
    string? ChangeType = "major",
    bool SelectiveGovernance = true,
    string? SourceRepository = null,
    string? SourceBranch = null,
    string? SourceCommit = null);

public sealed record FullFutureLoopActionRequest(
    string? Action,
    string? Notes = null,
    int? ExpectedRevision = null);

public sealed record FullFutureLoopAgentRequest(
    string? Question,
    bool OpenSupportIssue = false);

/// <summary>
/// Module 083 provides a safe, persistent sandbox implementation of the complete
/// intent-to-production-to-support-to-repair loop. It never calls GitHub, a cloud
/// provider, a deployment controller, or a production service. Every action is
/// recorded as governed sandbox evidence so the full lifecycle can be exercised
/// in Test before external adapters are separately approved.
/// </summary>
public static class FullFutureLoopModule
{
    private const string Module = "083";
    private const string ContractVersion = "083-full-future-loop-sandbox-v1";
    private const string Migration = "082_module_083_full_future_loop";
    private const string DefaultRepository = "ahmedadeyemi-cts/project-time-platform";

    private static readonly HashSet<string> ViewRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "SYSTEM_ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR", "MANAGER", "PEOPLE_MANAGER",
        "RELEASE_MANAGER", "ENGINEERING_MANAGER", "ENGINEERING_LEAD",
        "ENGINEERING_TEAM_LEAD", "ENGINEER", "ENGINEERING",
        "SYSTEMS_ENGINEER", "NETWORK_ENGINEER", "SOLUTION_ARCHITECT",
        "SUPPORT", "HELP_DESK", "SERVICE_DESK", "SUPPORT_MANAGER",
        "PROJECT_MANAGER", "PROJECT_MANAGEMENT", "EXECUTIVE",
        "EXECUTIVE_LEADERSHIP"
    };

    private static readonly HashSet<string> RunRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "SYSTEM_ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR", "MANAGER", "RELEASE_MANAGER",
        "ENGINEERING_MANAGER", "ENGINEERING_LEAD", "ENGINEERING_TEAM_LEAD",
        "PROJECT_MANAGER", "PROJECT_MANAGEMENT", "SUPPORT_MANAGER",
        "SERVICE_DESK_MANAGER"
    };

    private static readonly HashSet<string> ManageRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "SYSTEM_ADMINISTRATOR",
        "RELEASE_MANAGER"
    };

    private static readonly HashSet<string> SupportedActions = new(StringComparer.Ordinal)
    {
        "approve_governance", "complete_private_build", "run_canary_pass",
        "run_canary_fail", "retry_canary", "promote_sandbox",
        "record_production_signal", "relay_repair_issue", "complete_repair",
        "run_repair_canary_pass", "run_repair_canary_fail",
        "retry_repair_canary", "promote_again", "verify_close"
    };

    public static IEndpointRouteBuilder MapFullFutureLoopEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/full-future-loop");
        group.MapGet("/capabilities", Capabilities);
        group.MapGet("/access", (Func<HttpContext, Task<IResult>>)GetAccessAsync);
        group.MapGet("/summary", (Func<HttpContext, Task<IResult>>)GetSummaryAsync);
        group.MapGet("/loops", (Func<HttpContext, int?, Task<IResult>>)ListLoopsAsync);
        group.MapGet("/loops/{loopId:guid}", (Func<Guid, HttpContext, Task<IResult>>)GetLoopAsync);
        group.MapPost("/loops", (Func<FullFutureLoopCreateRequest, HttpContext, Task<IResult>>)CreateLoopAsync);
        group.MapPost("/loops/{loopId:guid}/actions", (Func<Guid, FullFutureLoopActionRequest, HttpContext, Task<IResult>>)ApplyActionAsync);
        group.MapPost("/loops/{loopId:guid}/run-full-sandbox", (Func<Guid, HttpContext, Task<IResult>>)RunFullSandboxAsync);
        group.MapPost("/loops/{loopId:guid}/reset", (Func<Guid, HttpContext, Task<IResult>>)ResetSandboxAsync);
        group.MapPost("/loops/{loopId:guid}/agent-keep", (Func<Guid, FullFutureLoopAgentRequest, HttpContext, Task<IResult>>)AgentKeepAsync);
        group.MapGet("/loops/{loopId:guid}/history", (Func<Guid, HttpContext, Task<IResult>>)GetHistoryAsync);
        return endpoints;
    }

    private static IResult Capabilities() => Results.Ok(new
    {
        module = Module,
        route = "full-future-loop",
        contractVersion = ContractVersion,
        mode = "safe_persistent_sandbox",
        mission = "Move work from intent to live verification with maximum automation under human authority and verifiable evidence.",
        stages = new[]
        {
            "governance_pending", "private_development", "canary_ready", "canary_failed",
            "promotion_ready", "sandbox_production", "production_signal", "repair_open",
            "repair_canary_ready", "repair_canary_failed", "repromotion_ready",
            "sandbox_repromoted", "verified_closed"
        },
        actions = ActionCatalog(),
        boundaries = new[]
        {
            "No GitHub mutation",
            "No production deployment",
            "No cloud or infrastructure mutation",
            "No secret access",
            "View-As is read-only",
            "All sandbox evidence is append-only",
            "Human authority remains required for governed transitions"
        }
    });

    private static async Task<IResult> GetAccessAsync(HttpContext context)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            if (access is null) return EnterpriseGovernanceResults.Unauthorized(Module);
            if (!CanView(access)) return EnterpriseGovernanceResults.Forbidden(Module, "Your effective role is not authorized to view the Full Future Loop.");
            var dataReady = await RuntimeReadyAsync(connection, context.RequestAborted);
            return Results.Ok(new
            {
                module = Module,
                contractVersion = ContractVersion,
                dataReady,
                status = dataReady ? "ready" : "migration_required",
                migration = Migration,
                scope = AccessScope(access),
                permissions = Permissions(access),
                safety = SafetyBoundary(),
                message = dataReady
                    ? "The Full Future Loop sandbox is ready for end-to-end testing."
                    : "Migration 082 must be applied before Module 083 can persist test loops and evidence.",
                generatedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "resolve Full Future Loop access");
        }
    }

    private static async Task<IResult> GetSummaryAsync(HttpContext context)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = Require(access, write: false);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();

            await using var command = new NpgsqlCommand("""
                SELECT
                    COUNT(*)::bigint,
                    COUNT(*) FILTER (WHERE current_stage='verified_closed')::bigint,
                    COUNT(*) FILTER (WHERE current_stage IN ('canary_failed','repair_canary_failed','production_signal','repair_open'))::bigint,
                    COUNT(*) FILTER (WHERE current_stage NOT IN ('verified_closed'))::bigint,
                    COUNT(*) FILTER (WHERE created_by_user_id=@user_id)::bigint,
                    COALESCE(SUM(iteration_number),0)::bigint
                FROM full_future_loop_items;
                """, connection);
            command.Parameters.AddWithValue("user_id", access!.EffectiveUserId);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            await reader.ReadAsync(context.RequestAborted);
            return Results.Ok(new
            {
                module = Module,
                contractVersion = ContractVersion,
                kpis = new
                {
                    totalLoops = reader.GetInt64(0),
                    verifiedClosed = reader.GetInt64(1),
                    attentionRequired = reader.GetInt64(2),
                    activeLoops = reader.GetInt64(3),
                    createdByMe = reader.GetInt64(4),
                    testIterations = reader.GetInt64(5)
                },
                scope = AccessScope(access),
                permissions = Permissions(access),
                generatedAt = DateTimeOffset.UtcNow
            });
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return MigrationRequired();
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "load Full Future Loop summary");
        }
    }

    private static async Task<IResult> ListLoopsAsync(HttpContext context, int? limit = null)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = Require(access, write: false);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();

            await using var command = new NpgsqlCommand(ItemSelect + " ORDER BY item.updated_at DESC LIMIT @limit;", connection);
            command.Parameters.AddWithValue("limit", Math.Clamp(limit ?? 100, 1, 500));
            var loops = new List<object>();
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
                loops.Add(ItemResponse(ReadItem(reader), access!));
            return Results.Ok(new { module = Module, loops, permissions = Permissions(access!), scope = AccessScope(access!) });
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return MigrationRequired();
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "list Full Future Loop work items");
        }
    }

    private static async Task<IResult> GetLoopAsync(Guid loopId, HttpContext context)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = Require(access, write: false);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();
            var item = await LoadItemAsync(connection, null, loopId, lockRow: false, context.RequestAborted);
            if (item is null) return Results.NotFound(new { module = Module, code = "FULL_FUTURE_LOOP_NOT_FOUND", message = "The selected Full Future Loop work item was not found." });
            return Results.Ok(await DetailResponseAsync(connection, item, access!, context.RequestAborted));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return MigrationRequired();
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "load a Full Future Loop work item");
        }
    }

    private static async Task<IResult> CreateLoopAsync(FullFutureLoopCreateRequest request, HttpContext context)
    {
        var title = Clean(request.Title, 200);
        if (title.Length < 3) return Validation("Enter a title containing at least three characters.");
        var description = Clean(request.Description, 4000);
        var changeType = NormalizeChangeType(request.ChangeType);
        var selectiveGovernance = request.SelectiveGovernance || changeType is "major" or "complex" or "architecture" or "security";
        var initialStage = selectiveGovernance ? "governance_pending" : "private_development";
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = Require(access, write: true);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            var loopId = Guid.NewGuid();
            await using (var command = new NpgsqlCommand("""
                INSERT INTO full_future_loop_items(
                    loop_id,title,description,change_type,selective_governance,environment,
                    current_stage,current_status,source_repository,source_branch,source_commit,
                    created_by_user_id,updated_by_user_id)
                VALUES(
                    @id,@title,@description,@change_type,@selective_governance,'sandbox',
                    @stage,'active',@repository,@branch,@commit,@actor,@actor)
                RETURNING loop_number;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("id", loopId);
                command.Parameters.AddWithValue("title", title);
                command.Parameters.AddWithValue("description", description);
                command.Parameters.AddWithValue("change_type", changeType);
                command.Parameters.AddWithValue("selective_governance", selectiveGovernance);
                command.Parameters.AddWithValue("stage", initialStage);
                command.Parameters.AddWithValue("repository", Clean(request.SourceRepository, 240, DefaultRepository));
                command.Parameters.AddWithValue("branch", Clean(request.SourceBranch, 240, "sandbox/full-future-loop"));
                command.Parameters.AddWithValue("commit", Clean(request.SourceCommit, 80));
                command.Parameters.AddWithValue("actor", access!.EffectiveUserId);
                await command.ExecuteScalarAsync(context.RequestAborted);
            }

            await AppendEventAsync(connection, transaction, loopId, access!, "work_item_created", null, initialStage, "created", "Full Future Loop sandbox work item created.", new { changeType, selectiveGovernance }, context.RequestAborted);
            await AppendArtifactAsync(connection, transaction, loopId, access!, "intent_packet", "intent", "ready", "Intent packet", description.Length == 0 ? title : description, new { title, description, changeType, selectiveGovernance }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            var item = await LoadItemAsync(connection, null, loopId, lockRow: false, context.RequestAborted);
            return Results.Created($"/api/full-future-loop/loops/{loopId}", new
            {
                module = Module,
                status = "created",
                loop = ItemResponse(item!, access!),
                nextActions = AvailableActions(item!)
            });
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "create a Full Future Loop sandbox work item");
        }
    }

    private static async Task<IResult> ApplyActionAsync(Guid loopId, FullFutureLoopActionRequest request, HttpContext context)
    {
        var action = NormalizeAction(request.Action);
        if (action.Length == 0) return Validation("Select a valid Full Future Loop action.");
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = Require(access, write: true);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            var item = await LoadItemAsync(connection, transaction, loopId, lockRow: true, context.RequestAborted);
            if (item is null) return Results.NotFound(new { module = Module, code = "FULL_FUTURE_LOOP_NOT_FOUND", message = "The selected Full Future Loop work item was not found." });
            if (request.ExpectedRevision.HasValue && request.ExpectedRevision.Value != item.Revision)
                return Results.Conflict(new { module = Module, code = "FULL_FUTURE_LOOP_REVISION_CONFLICT", message = "The loop changed after it was loaded. Refresh and try again.", currentRevision = item.Revision });
            var transition = await ApplyActionInternalAsync(connection, transaction, item, action, Clean(request.Notes, 2000), access!, context.RequestAborted);
            if (transition.Error is not null) return transition.Error;
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new
            {
                module = Module,
                status = "action_completed",
                action,
                loop = ItemResponse(transition.Item!, access!),
                nextActions = AvailableActions(transition.Item!)
            });
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, $"apply Full Future Loop action {action}");
        }
    }

    private static async Task<IResult> RunFullSandboxAsync(Guid loopId, HttpContext context)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = Require(access, write: true);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            var item = await LoadItemAsync(connection, transaction, loopId, lockRow: true, context.RequestAborted);
            if (item is null) return Results.NotFound(new { module = Module, code = "FULL_FUTURE_LOOP_NOT_FOUND", message = "The selected Full Future Loop work item was not found." });
            if (item.Stage is not ("governance_pending" or "private_development"))
                return Conflict("The complete sandbox run can start only from governance pending or private development. Reset the loop to start another complete run.", item);

            var actions = new List<string>();
            if (item.Stage == "governance_pending") actions.Add("approve_governance");
            actions.AddRange(new[]
            {
                "complete_private_build", "run_canary_pass", "promote_sandbox",
                "record_production_signal", "relay_repair_issue", "complete_repair",
                "run_repair_canary_pass", "promote_again", "verify_close"
            });
            foreach (var action in actions)
            {
                var transition = await ApplyActionInternalAsync(connection, transaction, item, action, "Automated complete sandbox demonstration.", access!, context.RequestAborted);
                if (transition.Error is not null) return transition.Error;
                item = transition.Item!;
            }
            await AppendArtifactAsync(connection, transaction, loopId, access!, "full_loop_report", "complete_sandbox_run", "passed", "Complete Full Future Loop report", "All governed sandbox stages completed, including support, repair, re-canary, re-promotion, and verification.", new { actions, noExternalMutation = true, completedAt = DateTimeOffset.UtcNow }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new
            {
                module = Module,
                status = "full_sandbox_loop_completed",
                loop = ItemResponse(item, access!),
                actionsExecuted = actions,
                safety = SafetyBoundary()
            });
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "run the complete Full Future Loop sandbox");
        }
    }

    private static async Task<IResult> ResetSandboxAsync(Guid loopId, HttpContext context)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = Require(access, write: true, manageOnly: true);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            var item = await LoadItemAsync(connection, transaction, loopId, lockRow: true, context.RequestAborted);
            if (item is null) return Results.NotFound(new { module = Module, code = "FULL_FUTURE_LOOP_NOT_FOUND" });
            var stage = item.SelectiveGovernance ? "governance_pending" : "private_development";
            await using (var command = new NpgsqlCommand("""
                UPDATE full_future_loop_items
                SET current_stage=@stage,current_status='active',release_tag='',last_canary_status='',
                    iteration_number=iteration_number+1,closed_at=NULL,updated_by_user_id=@actor
                WHERE loop_id=@id AND revision_number=@revision;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("stage", stage);
                command.Parameters.AddWithValue("actor", access!.EffectiveUserId);
                command.Parameters.AddWithValue("id", loopId);
                command.Parameters.AddWithValue("revision", item.Revision);
                if (await command.ExecuteNonQueryAsync(context.RequestAborted) != 1)
                    return Results.Conflict(new { module = Module, code = "FULL_FUTURE_LOOP_REVISION_CONFLICT", message = "Refresh and try again." });
            }
            await AppendEventAsync(connection, transaction, loopId, access!, "sandbox_reset", item.Stage, stage, "reset", "Sandbox loop reset for another immutable test iteration.", new { priorIteration = item.Iteration, nextIteration = item.Iteration + 1 }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            var refreshed = await LoadItemAsync(connection, null, loopId, lockRow: false, context.RequestAborted);
            return Results.Ok(new { module = Module, status = "sandbox_reset", loop = ItemResponse(refreshed!, access!) });
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "reset the Full Future Loop sandbox");
        }
    }

    private static async Task<IResult> AgentKeepAsync(Guid loopId, FullFutureLoopAgentRequest request, HttpContext context)
    {
        var question = Clean(request.Question, 2000);
        if (question.Length < 2) return Validation("Enter a support question for Agent Keep.");
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = Require(access, write: false);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();
            var item = await LoadItemAsync(connection, null, loopId, lockRow: false, context.RequestAborted);
            if (item is null) return Results.NotFound(new { module = Module, code = "FULL_FUTURE_LOOP_NOT_FOUND" });
            if (access!.IsViewAs && request.OpenSupportIssue) return EnterpriseGovernanceResults.ViewAsReadOnly(Module);

            var next = AvailableActions(item).FirstOrDefault() ?? "No additional transition is required.";
            var answer = $"This sandbox loop is currently at {Humanize(item.Stage)} with status {Humanize(item.Status)}. " +
                         $"The next governed action is {Humanize(next)}. Agent Keep has read-only access to approved sandbox evidence and cannot read private source, change a repository, deploy an application, or alter production. ";
            answer += item.Stage switch
            {
                "production_signal" => "The watcher has observed a signal. Relay it into a private repair issue before any repair work begins.",
                "repair_open" => "A private repair issue is open. Complete the repair evidence, then run the repair canary.",
                "verified_closed" => "The complete repair and verification loop is closed with append-only evidence.",
                _ => "Continue through the displayed gate and retain evidence at each transition."
            };

            if (access.IsViewAs)
            {
                return Results.Ok(new
                {
                    module = Module,
                    status = "agent_keep_answered_read_only",
                    answer,
                    issueOpened = false,
                    persisted = false,
                    stateChanged = false,
                    restrictions = new[] { "View-As read-only", "no private source", "no deployment mutation", "no secret access" }
                });
            }

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            await AppendArtifactAsync(connection, transaction, loopId, access, "agent_keep_interaction", "support_guidance", "answered", "Agent Keep support guidance", answer, new { question, answer, readOnly = true, requestedSupportIssue = request.OpenSupportIssue }, context.RequestAborted);
            if (request.OpenSupportIssue)
            {
                await AppendArtifactAsync(connection, transaction, loopId, access, "support_issue", "agent_keep_issue", "open", "Agent Keep support issue", question, new { source = "agent_keep", stage = item.Stage, privateSourceAccess = false }, context.RequestAborted);
                await AppendEventAsync(connection, transaction, loopId, access, "agent_keep_issue_opened", item.Stage, item.Stage, "open", "Agent Keep opened a governed sandbox support issue without changing the lifecycle stage.", new { question }, context.RequestAborted);
            }
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new
            {
                module = Module,
                status = "agent_keep_answered",
                answer,
                issueOpened = request.OpenSupportIssue,
                restrictions = new[] { "read-only evidence", "no private source", "no deployment mutation", "no secret access" }
            });
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "run Agent Keep support guidance");
        }
    }

    private static async Task<IResult> GetHistoryAsync(Guid loopId, HttpContext context)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await EnterpriseGovernanceAccessResolver.ResolveAsync(context, connection, context.RequestAborted);
            var failure = Require(access, write: false);
            if (failure is not null) return failure;
            if (!await RuntimeReadyAsync(connection, context.RequestAborted)) return MigrationRequired();
            var item = await LoadItemAsync(connection, null, loopId, lockRow: false, context.RequestAborted);
            if (item is null) return Results.NotFound(new { module = Module, code = "FULL_FUTURE_LOOP_NOT_FOUND" });
            var events = await LoadEventsAsync(connection, loopId, context.RequestAborted);
            var artifacts = await LoadArtifactsAsync(connection, loopId, context.RequestAborted);
            return Results.Ok(new { module = Module, loopId, events, artifacts, immutable = true });
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "load Full Future Loop history");
        }
    }

    private static async Task<TransitionResult> ApplyActionInternalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LoopItem item,
        string action,
        string notes,
        EnterpriseGovernanceAccess access,
        CancellationToken cancellationToken)
    {
        var expectedStages = action switch
        {
            "approve_governance" => new[] { "governance_pending" },
            "complete_private_build" => new[] { "private_development" },
            "run_canary_pass" or "run_canary_fail" => new[] { "canary_ready" },
            "retry_canary" => new[] { "canary_failed" },
            "promote_sandbox" => new[] { "promotion_ready" },
            "record_production_signal" => new[] { "sandbox_production" },
            "relay_repair_issue" => new[] { "production_signal" },
            "complete_repair" => new[] { "repair_open" },
            "run_repair_canary_pass" or "run_repair_canary_fail" => new[] { "repair_canary_ready" },
            "retry_repair_canary" => new[] { "repair_canary_failed" },
            "promote_again" => new[] { "repromotion_ready" },
            "verify_close" => new[] { "sandbox_repromoted", "sandbox_production" },
            _ => Array.Empty<string>()
        };
        if (expectedStages.Length == 0) return new(null, Validation("The requested action is not supported."));
        if (!expectedStages.Contains(item.Stage, StringComparer.OrdinalIgnoreCase))
            return new(null, Conflict($"{Humanize(action)} cannot run while the loop is at {Humanize(item.Stage)}.", item));

        var toStage = action switch
        {
            "approve_governance" => "private_development",
            "complete_private_build" => "canary_ready",
            "run_canary_pass" => "promotion_ready",
            "run_canary_fail" => "canary_failed",
            "retry_canary" => "canary_ready",
            "promote_sandbox" => "sandbox_production",
            "record_production_signal" => "production_signal",
            "relay_repair_issue" => "repair_open",
            "complete_repair" => "repair_canary_ready",
            "run_repair_canary_pass" => "repromotion_ready",
            "run_repair_canary_fail" => "repair_canary_failed",
            "retry_repair_canary" => "repair_canary_ready",
            "promote_again" => "sandbox_repromoted",
            "verify_close" => "verified_closed",
            _ => item.Stage
        };
        var outcome = action.EndsWith("_fail", StringComparison.Ordinal) ? "failed" : action == "verify_close" ? "verified" : "passed";
        var status = toStage switch
        {
            "canary_failed" or "repair_canary_failed" or "production_signal" or "repair_open" => "attention_required",
            "verified_closed" => "closed",
            _ => "active"
        };
        var sourceCommit = item.Commit;
        if (action == "complete_private_build" && string.IsNullOrWhiteSpace(sourceCommit))
        {
            var identity = Encoding.UTF8.GetBytes($"{item.Id:N}:{item.Iteration}:{item.Revision}");
            sourceCommit = Convert.ToHexString(SHA256.HashData(identity)).ToLowerInvariant()[..40];
        }
        var releaseTag = item.ReleaseTag;
        if (action == "promote_sandbox") releaseTag = $"sandbox-{item.Number}-i{item.Iteration}";
        if (action == "promote_again") releaseTag = $"sandbox-{item.Number}-i{item.Iteration}-repair";
        var lastCanary = item.LastCanaryStatus;
        if (action.Contains("canary", StringComparison.Ordinal)) lastCanary = outcome;

        await using (var command = new NpgsqlCommand("""
            UPDATE full_future_loop_items
            SET current_stage=@stage,current_status=@status,source_commit=@source_commit,release_tag=@release_tag,
                last_canary_status=@canary,updated_by_user_id=@actor,
                closed_at=CASE WHEN @stage='verified_closed' THEN NOW() ELSE NULL END
            WHERE loop_id=@id AND revision_number=@revision;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("stage", toStage);
            command.Parameters.AddWithValue("status", status);
            command.Parameters.AddWithValue("source_commit", sourceCommit ?? string.Empty);
            command.Parameters.AddWithValue("release_tag", releaseTag ?? string.Empty);
            command.Parameters.AddWithValue("canary", lastCanary ?? string.Empty);
            command.Parameters.AddWithValue("actor", access.EffectiveUserId);
            command.Parameters.AddWithValue("id", item.Id);
            command.Parameters.AddWithValue("revision", item.Revision);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                return new(null, Results.Conflict(new { module = Module, code = "FULL_FUTURE_LOOP_REVISION_CONFLICT", message = "Refresh and try again." }));
        }

        var summary = ActionSummary(action);
        await AppendEventAsync(connection, transaction, item.Id, access, action, item.Stage, toStage, outcome, summary, new { notes, iteration = item.Iteration, sourceCommit, releaseTag, externalMutation = false }, cancellationToken);
        var artifactItem = item with { Stage = toStage, Status = status, Commit = sourceCommit, ReleaseTag = releaseTag, LastCanaryStatus = lastCanary };
        await AppendActionArtifactAsync(connection, transaction, artifactItem, action, outcome, summary, notes, access, cancellationToken);
        var updated = await LoadItemAsync(connection, transaction, item.Id, lockRow: false, cancellationToken);
        return new(updated, null);
    }

    private static async Task AppendActionArtifactAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LoopItem item,
        string action,
        string outcome,
        string summary,
        string notes,
        EnterpriseGovernanceAccess access,
        CancellationToken cancellationToken)
    {
        var artifact = action switch
        {
            "approve_governance" => ("decision_packet", "steer_it_decision", "approved", "STEER-IT decision packet"),
            "complete_private_build" => ("private_build", "private_dev_evidence", "validated", "Private development evidence"),
            "run_canary_pass" or "run_canary_fail" => ("canary_run", "initial_canary", outcome, "Initial isolated canary run"),
            "retry_canary" => ("canary_control", "initial_canary_retry", "ready", "Initial canary retry"),
            "promote_sandbox" => ("release_manifest", "sandbox_promotion", "promoted", "Curated sandbox promotion manifest"),
            "record_production_signal" => ("production_evidence", "sandbox_signal", "observed", "Read-only production evidence"),
            "relay_repair_issue" => ("private_repair_issue", "watcher_relay", "open", "Private repair issue"),
            "complete_repair" => ("repair_resolution", "review_and_fix", "validated", "Repair review and fix evidence"),
            "run_repair_canary_pass" or "run_repair_canary_fail" => ("canary_run", "repair_canary", outcome, "Repair isolated canary run"),
            "retry_repair_canary" => ("canary_control", "repair_canary_retry", "ready", "Repair canary retry"),
            "promote_again" => ("release_manifest", "sandbox_repromotion", "promoted", "Curated sandbox re-promotion manifest"),
            "verify_close" => ("verification_report", "live_verification", "verified", "Final verification report"),
            _ => ("lifecycle_evidence", action, outcome, Humanize(action))
        };
        var checks = action.Contains("canary", StringComparison.Ordinal)
            ? new[] { "contract tests", "permission isolation", "evidence validity", "policy gates", "cleanup" }
            : Array.Empty<string>();
        await AppendArtifactAsync(connection, transaction, item.Id, access, artifact.Item1, artifact.Item2, artifact.Item3, artifact.Item4, summary,
            new
            {
                action,
                notes,
                checks,
                checksPassed = outcome == "passed" || action is "run_canary_pass" or "run_repair_canary_pass",
                repository = item.Repository,
                branch = item.Branch,
                commit = item.Commit,
                releaseTag = item.ReleaseTag,
                sandboxOnly = true,
                externalMutation = false
            }, cancellationToken);
    }

    private static async Task<LoopItem?> LoadItemAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid loopId,
        bool lockRow,
        CancellationToken cancellationToken)
    {
        var sql = ItemSelect + " WHERE item.loop_id=@id" + (lockRow ? " FOR UPDATE" : string.Empty) + ";";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", loopId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadItem(reader) : null;
    }

    private static LoopItem ReadItem(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
        reader.GetBoolean(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9),
        reader.GetString(10), reader.GetString(11), reader.GetString(12), reader.GetString(13), reader.GetInt32(14),
        reader.GetInt32(15), reader.GetGuid(16), reader.GetGuid(17), reader.GetFieldValue<DateTimeOffset>(18),
        reader.GetFieldValue<DateTimeOffset>(19), reader.IsDBNull(20) ? null : reader.GetFieldValue<DateTimeOffset>(20));

    private static object ItemResponse(LoopItem item, EnterpriseGovernanceAccess access) => new
    {
        loopId = item.Id,
        loopNumber = $"FFL-{item.Number:00000}",
        item.Title,
        item.Description,
        changeType = item.ChangeType,
        selectiveGovernance = item.SelectiveGovernance,
        environment = item.Environment,
        currentStage = item.Stage,
        currentStatus = item.Status,
        sourceRepository = item.Repository,
        sourceBranch = item.Branch,
        sourceCommit = item.Commit,
        releaseTag = item.ReleaseTag,
        lastCanaryStatus = item.LastCanaryStatus,
        iteration = item.Iteration,
        revision = item.Revision,
        createdAt = item.CreatedAt,
        updatedAt = item.UpdatedAt,
        closedAt = item.ClosedAt,
        nextActions = AvailableActions(item),
        canRun = CanRun(access),
        canManage = CanManage(access)
    };

    private static async Task<object> DetailResponseAsync(NpgsqlConnection connection, LoopItem item, EnterpriseGovernanceAccess access, CancellationToken cancellationToken)
    {
        var events = await LoadEventsAsync(connection, item.Id, cancellationToken);
        var artifacts = await LoadArtifactsAsync(connection, item.Id, cancellationToken);
        return new
        {
            module = Module,
            contractVersion = ContractVersion,
            loop = ItemResponse(item, access),
            events,
            artifacts,
            nodeStates = NodeStates(item, artifacts),
            permissions = Permissions(access),
            scope = AccessScope(access),
            safety = SafetyBoundary()
        };
    }

    private static async Task<List<object>> LoadEventsAsync(NpgsqlConnection connection, Guid loopId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT event_id,event_code,COALESCE(from_stage,''),to_stage,outcome,summary,details::text,
                   actual_actor_user_id,effective_actor_user_id,occurred_at
            FROM full_future_loop_events WHERE loop_id=@id ORDER BY occurred_at,event_id;
            """, connection);
        command.Parameters.AddWithValue("id", loopId);
        var rows = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                eventId = reader.GetGuid(0),
                eventCode = reader.GetString(1),
                fromStage = reader.GetString(2),
                toStage = reader.GetString(3),
                outcome = reader.GetString(4),
                summary = reader.GetString(5),
                details = ParseJson(reader.GetString(6)),
                actualActorUserId = reader.GetGuid(7),
                effectiveActorUserId = reader.GetGuid(8),
                occurredAt = reader.GetFieldValue<DateTimeOffset>(9)
            });
        }
        return rows;
    }

    private static async Task<List<object>> LoadArtifactsAsync(NpgsqlConnection connection, Guid loopId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT artifact_id,artifact_type,artifact_code,status,title,summary,payload::text,
                   is_read_only,created_by_user_id,created_at
            FROM full_future_loop_artifacts WHERE loop_id=@id ORDER BY created_at,artifact_id;
            """, connection);
        command.Parameters.AddWithValue("id", loopId);
        var rows = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                artifactId = reader.GetGuid(0),
                artifactType = reader.GetString(1),
                artifactCode = reader.GetString(2),
                status = reader.GetString(3),
                title = reader.GetString(4),
                summary = reader.GetString(5),
                payload = ParseJson(reader.GetString(6)),
                isReadOnly = reader.GetBoolean(7),
                createdByUserId = reader.GetGuid(8),
                createdAt = reader.GetFieldValue<DateTimeOffset>(9)
            });
        }
        return rows;
    }

    private static async Task AppendEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid loopId,
        EnterpriseGovernanceAccess access,
        string eventCode,
        string? fromStage,
        string toStage,
        string outcome,
        string summary,
        object details,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO full_future_loop_events(
                loop_id,event_code,from_stage,to_stage,outcome,summary,details,
                actual_actor_user_id,effective_actor_user_id)
            VALUES(@loop_id,@event_code,@from_stage,@to_stage,@outcome,@summary,@details,@actual,@effective);
            """, connection, transaction);
        command.Parameters.AddWithValue("loop_id", loopId);
        command.Parameters.AddWithValue("event_code", Clean(eventCode, 80));
        command.Parameters.Add("from_stage", NpgsqlDbType.Varchar).Value = (object?)fromStage ?? DBNull.Value;
        command.Parameters.AddWithValue("to_stage", Clean(toStage, 80));
        command.Parameters.AddWithValue("outcome", Clean(outcome, 40));
        command.Parameters.AddWithValue("summary", Clean(summary, 2000));
        command.Parameters.AddWithValue("details", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(details));
        command.Parameters.AddWithValue("actual", access.ActualUserId);
        command.Parameters.AddWithValue("effective", access.EffectiveUserId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AppendArtifactAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid loopId,
        EnterpriseGovernanceAccess access,
        string artifactType,
        string artifactCode,
        string status,
        string title,
        string summary,
        object payload,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO full_future_loop_artifacts(
                loop_id,artifact_type,artifact_code,status,title,summary,payload,
                is_read_only,created_by_user_id)
            VALUES(@loop_id,@artifact_type,@artifact_code,@status,@title,@summary,@payload,TRUE,@actor);
            """, connection, transaction);
        command.Parameters.AddWithValue("loop_id", loopId);
        command.Parameters.AddWithValue("artifact_type", Clean(artifactType, 80));
        command.Parameters.AddWithValue("artifact_code", Clean(artifactCode, 100));
        command.Parameters.AddWithValue("status", Clean(status, 40));
        command.Parameters.AddWithValue("title", Clean(title, 240));
        command.Parameters.AddWithValue("summary", Clean(summary, 4000));
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(payload));
        command.Parameters.AddWithValue("actor", access.EffectiveUserId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static object[] NodeStates(LoopItem item, IReadOnlyCollection<object> artifacts)
    {
        var order = new[]
        {
            "governance_pending", "private_development", "canary_ready", "promotion_ready",
            "sandbox_production", "production_signal", "repair_open", "repair_canary_ready",
            "repromotion_ready", "sandbox_repromoted", "verified_closed"
        };
        var current = Array.IndexOf(order, item.Stage);
        return order.Select((stage, index) => (object)new
        {
            stage,
            state = index < current ? "complete" : index == current ? "current" : "pending",
            artifactCount = artifacts.Count
        }).ToArray();
    }

    private static object AccessScope(EnterpriseGovernanceAccess access) => new
    {
        mode = access.IsViewAs ? "view_as_read_only" : access.IsBroadScope ? "organization" : "role_scoped",
        actualUserId = access.ActualUserId,
        effectiveUserId = access.EffectiveUserId,
        effectiveUser = access.DisplayName,
        access.Email,
        access.IsViewAs,
        roles = access.Roles.OrderBy(value => value).ToArray()
    };

    private static object Permissions(EnterpriseGovernanceAccess access) => new
    {
        canView = CanView(access),
        canRunSandbox = CanRun(access),
        canManage = CanManage(access),
        canUseAgentKeep = CanView(access),
        canReset = CanManage(access),
        viewAsReadOnly = access.IsViewAs
    };

    private static object SafetyBoundary() => new
    {
        environment = "sandbox",
        productionMutationEnabled = false,
        githubMutationEnabled = false,
        deploymentControllerExecutionEnabled = false,
        cloudMutationEnabled = false,
        secretAccessEnabled = false,
        externalAiRequired = false,
        evidenceMode = "append_only",
        agentKeepBoundary = "No private source access"
    };

    private static IResult? Require(EnterpriseGovernanceAccess? access, bool write, bool manageOnly = false)
    {
        if (access is null) return EnterpriseGovernanceResults.Unauthorized(Module);
        if (!CanView(access)) return EnterpriseGovernanceResults.Forbidden(Module, "Your effective role is not authorized to view Module 083.");
        if (!write) return null;
        if (access.IsViewAs) return EnterpriseGovernanceResults.ViewAsReadOnly(Module);
        if (manageOnly && !CanManage(access)) return EnterpriseGovernanceResults.Forbidden(Module, "Module 083 management authority is required for this action.");
        if (!manageOnly && !CanRun(access)) return EnterpriseGovernanceResults.Forbidden(Module, "Your effective role has read-only Module 083 access.");
        return null;
    }

    private static bool CanView(EnterpriseGovernanceAccess access) => access.IsBroadScope
        || access.Roles.Overlaps(ViewRoles)
        || access.Permissions.Contains("VIEW_FULL_FUTURE_LOOP_083");

    private static bool CanRun(EnterpriseGovernanceAccess access) => !access.IsViewAs && (
        access.CanManageOrganization
        || access.Roles.Overlaps(RunRoles)
        || access.Permissions.Contains("RUN_FULL_FUTURE_LOOP_SANDBOX_083")
        || access.Permissions.Contains("MANAGE_FULL_FUTURE_LOOP_083"));

    private static bool CanManage(EnterpriseGovernanceAccess access) => !access.IsViewAs && (
        access.CanManageOrganization
        || access.Roles.Overlaps(ManageRoles)
        || access.Permissions.Contains("MANAGE_FULL_FUTURE_LOOP_083"));

    private static async Task<bool> RuntimeReadyAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT to_regclass('public.full_future_loop_items') IS NOT NULL
               AND to_regclass('public.full_future_loop_events') IS NOT NULL
               AND to_regclass('public.full_future_loop_artifacts') IS NOT NULL;
            """, connection);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static IResult MigrationRequired() => Results.Json(new
    {
        module = Module,
        code = "MODULE_083_MIGRATION_REQUIRED",
        migration = Migration,
        dataReady = false,
        stateChanged = false,
        message = "Migration 082 must be applied and verified before the Full Future Loop sandbox can persist test evidence."
    }, statusCode: StatusCodes.Status409Conflict);

    private static IResult Validation(string message) => Results.BadRequest(new { module = Module, code = "MODULE_083_VALIDATION", message, stateChanged = false });
    private static IResult Conflict(string message, LoopItem item) => Results.Conflict(new { module = Module, code = "MODULE_083_INVALID_TRANSITION", message, currentStage = item.Stage, revision = item.Revision, nextActions = AvailableActions(item), stateChanged = false });

    private static string[] AvailableActions(LoopItem item) => item.Stage switch
    {
        "governance_pending" => new[] { "approve_governance" },
        "private_development" => new[] { "complete_private_build" },
        "canary_ready" => new[] { "run_canary_pass", "run_canary_fail" },
        "canary_failed" => new[] { "retry_canary" },
        "promotion_ready" => new[] { "promote_sandbox" },
        "sandbox_production" => new[] { "record_production_signal", "verify_close" },
        "production_signal" => new[] { "relay_repair_issue" },
        "repair_open" => new[] { "complete_repair" },
        "repair_canary_ready" => new[] { "run_repair_canary_pass", "run_repair_canary_fail" },
        "repair_canary_failed" => new[] { "retry_repair_canary" },
        "repromotion_ready" => new[] { "promote_again" },
        "sandbox_repromoted" => new[] { "verify_close" },
        _ => Array.Empty<string>()
    };

    private static object[] ActionCatalog() => new[]
    {
        new { action = "approve_governance", label = "Approve STEER-IT decision packet" },
        new { action = "complete_private_build", label = "Complete private build and evidence" },
        new { action = "run_canary_pass", label = "Run passing isolated canary" },
        new { action = "run_canary_fail", label = "Run failing isolated canary" },
        new { action = "retry_canary", label = "Prepare initial canary retry" },
        new { action = "promote_sandbox", label = "Promote curated release to sandbox production" },
        new { action = "record_production_signal", label = "Record read-only production signal" },
        new { action = "relay_repair_issue", label = "Relay signal to private repair issue" },
        new { action = "complete_repair", label = "Complete repair review and fix evidence" },
        new { action = "run_repair_canary_pass", label = "Run passing repair canary" },
        new { action = "run_repair_canary_fail", label = "Run failing repair canary" },
        new { action = "retry_repair_canary", label = "Prepare repair canary retry" },
        new { action = "promote_again", label = "Promote curated repair again" },
        new { action = "verify_close", label = "Verify outcomes and close the loop" }
    };

    private static string ActionSummary(string action) => action switch
    {
        "approve_governance" => "Selective governance approved the decision packet for private development.",
        "complete_private_build" => "Private development completed with reviewable build and test evidence.",
        "run_canary_pass" => "The isolated initial canary passed all deterministic acceptance checks and cleaned up.",
        "run_canary_fail" => "The isolated initial canary produced a governed failure result without affecting production.",
        "retry_canary" => "The failed initial canary was returned to a ready state for another isolated run.",
        "promote_sandbox" => "A curated, private-data-free release manifest was promoted to sandbox production.",
        "record_production_signal" => "Read-only sandbox production evidence recorded a signal for the watcher.",
        "relay_repair_issue" => "The watcher normalized the signal into a private repair issue.",
        "complete_repair" => "The private repair was reviewed, fixed, tested, and prepared for repair canary validation.",
        "run_repair_canary_pass" => "The isolated repair canary passed all deterministic checks and cleaned up.",
        "run_repair_canary_fail" => "The isolated repair canary failed safely and retained its evidence.",
        "retry_repair_canary" => "The failed repair canary was returned to a ready state.",
        "promote_again" => "The curated repair release was promoted again to sandbox production.",
        "verify_close" => "Final outcomes were verified and the Full Future Loop was closed with immutable evidence.",
        _ => Humanize(action)
    };

    private static string NormalizeAction(string? value)
    {
        var normalized = Clean(value, 80).ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return SupportedActions.Contains(normalized) ? normalized : string.Empty;
    }

    private static string NormalizeChangeType(string? value)
    {
        var normalized = Clean(value, 40, "major").ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized is "standard" or "major" or "complex" or "architecture" or "security" ? normalized : "major";
    }

    private static string Clean(string? value, int maximum, string fallback = "")
    {
        var clean = (value ?? fallback).Trim();
        return clean.Length <= maximum ? clean : clean[..maximum];
    }

    private static string Humanize(string? value) => Clean(value, 200).Replace('_', ' ');

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return document.RootElement.Clone();
    }

    private const string ItemSelect = """
        SELECT item.loop_id,item.loop_number,item.title,item.description,item.change_type,
               item.selective_governance,item.environment,item.current_stage,item.current_status,
               item.source_repository,item.source_branch,item.source_commit,item.release_tag,
               item.last_canary_status,item.iteration_number,item.revision_number,
               item.created_by_user_id,item.updated_by_user_id,item.created_at,item.updated_at,item.closed_at
        FROM full_future_loop_items item
        """;

    private sealed record LoopItem(
        Guid Id,
        long Number,
        string Title,
        string Description,
        string ChangeType,
        bool SelectiveGovernance,
        string Environment,
        string Stage,
        string Status,
        string Repository,
        string Branch,
        string Commit,
        string ReleaseTag,
        string LastCanaryStatus,
        int Iteration,
        int Revision,
        Guid CreatedBy,
        Guid UpdatedBy,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? ClosedAt);

    private sealed record TransitionResult(LoopItem? Item, IResult? Error);
}
