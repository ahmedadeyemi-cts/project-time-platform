using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Makes the production approval contract the single write path for approval
/// decisions, executes IResult responses explicitly, and fails closed whenever
/// the immutable approval evidence foundation is unavailable.
/// </summary>
public static class ProductionApprovalWorkflowHardening
{
    private const string ApprovalContract = "approval-work-production-v2-2026-07-30";
    private const string NonProjectContract = "non-project-activity-create-only-v2";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    private static readonly HashSet<string> RetiredManagerApprovalPaths =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "/api/manager/approvals/approve",
            "/api/manager/approvals/bulk-approve",
            "/api/scoped-approval/delegated",
            "/api/scoped-approval/ptc-final"
        };

    public static WebApplication UseProductionApprovalWorkflowHardening(
        this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            var method = context.Request.Method;

            if (HttpMethods.IsGet(method)
                && (path.Equals("/api/approval-work/v2/pending", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/api/approval-work/pending", StringComparison.OrdinalIgnoreCase)))
            {
                SetApprovalHeaders(context);
                var result = await ProductionApprovalWorkModule.GetPendingAsync(context);
                await result.ExecuteAsync(context);
                return;
            }

            if (HttpMethods.IsPost(method)
                && (path.Equals("/api/approval-work/v2/bulk-complete", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/api/approval-work/bulk-complete", StringComparison.OrdinalIgnoreCase)))
            {
                SetApprovalHeaders(context);
                if (!await RequireImmutableApprovalEvidenceAsync(context)) return;

                var request = await ReadRequestAsync<
                    ProductionApprovalWorkModule.BulkCompleteRequest>(
                    context,
                    "The bulk approval request body is required.");
                if (request is null) return;

                var result = await ProductionApprovalWorkModule.BulkCompleteAsync(
                    request,
                    context);
                await result.ExecuteAsync(context);
                return;
            }

            if (HttpMethods.IsPost(method)
                && (path.Equals("/api/timesheet/ptc/non-project-activities", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/api/timesheet/ptc/non-project-tasks", StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.Headers["X-ProjectPulse-Non-Project-Contract"] =
                    NonProjectContract;
                context.Response.Headers.CacheControl = "no-store";
                if (!await RequireImmutableNonProjectEvidenceAsync(context)) return;

                var request = await ReadRequestAsync<
                    ProductionApprovalWorkModule.NonProjectActivityRequest>(
                    context,
                    "The non-project activity request body is required.");
                if (request is null) return;

                var result = await ProductionApprovalWorkModule.CreateNonProjectActivityAsync(
                    request,
                    context);
                await result.ExecuteAsync(context);
                return;
            }

            if (HttpMethods.IsPost(method)
                && RetiredManagerApprovalPaths.Contains(path))
            {
                await WriteRetiredApprovalRouteAsync(context);
                return;
            }

            if (HttpMethods.IsPost(method)
                && path.Equals(
                    "/api/workflow/approval-items/action",
                    StringComparison.OrdinalIgnoreCase)
                && await IsLegacyProjectManagerApprovalAsync(context))
            {
                await WriteRetiredApprovalRouteAsync(context);
                return;
            }

            await next();
        });

        return app;
    }

    private static void SetApprovalHeaders(HttpContext context)
    {
        context.Response.Headers["X-ProjectPulse-Approval-Contract"] =
            ApprovalContract;
        context.Response.Headers.CacheControl = "no-store";
    }

    private static async Task<T?> ReadRequestAsync<T>(
        HttpContext context,
        string missingMessage)
        where T : class
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<T>(
                context.Request.Body,
                JsonOptions,
                context.RequestAborted);

            if (request is not null) return request;

            await WriteJsonAsync(
                context,
                StatusCodes.Status400BadRequest,
                new
                {
                    status = "invalid_request",
                    message = missingMessage,
                    traceId = context.TraceIdentifier
                });
            return null;
        }
        catch (JsonException)
        {
            await WriteJsonAsync(
                context,
                StatusCodes.Status400BadRequest,
                new
                {
                    status = "invalid_json",
                    message = "The request body is not valid JSON.",
                    traceId = context.TraceIdentifier
                });
            return null;
        }
    }

    private static async Task<bool> IsLegacyProjectManagerApprovalAsync(
        HttpContext context)
    {
        context.Request.EnableBuffering();
        try
        {
            using var document = await JsonDocument.ParseAsync(
                context.Request.Body,
                cancellationToken: context.RequestAborted);
            context.Request.Body.Position = 0;

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("action", out var action))
            {
                return false;
            }

            return string.Equals(
                action.GetString()?.Trim(),
                "pm_approve",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            if (context.Request.Body.CanSeek) context.Request.Body.Position = 0;
            return false;
        }
        finally
        {
            if (context.Request.Body.CanSeek) context.Request.Body.Position = 0;
        }
    }

    private static async Task WriteRetiredApprovalRouteAsync(HttpContext context)
    {
        SetApprovalHeaders(context);
        await WriteJsonAsync(
            context,
            StatusCodes.Status409Conflict,
            new
            {
                status = "legacy_approval_route_retired",
                message = "Use Pending approval work in the Approval Center. It is the only approval path and enforces Manager, project-scoped PM, and PTC routing.",
                approvalRoute = "/api/approval-work/v2/bulk-complete",
                traceId = context.TraceIdentifier
            });
    }

    private static async Task<bool> RequireImmutableApprovalEvidenceAsync(
        HttpContext context)
    {
        var readiness = await LoadImmutableAuditReadinessAsync(context);
        if (readiness.StageEvidenceReady && readiness.BatchEvidenceReady)
        {
            return true;
        }

        await WriteJsonAsync(
            context,
            StatusCodes.Status503ServiceUnavailable,
            new
            {
                status = "immutable_approval_audit_unavailable",
                message = "Approval changes are temporarily paused because immutable stage and batch evidence cannot be guaranteed.",
                readiness.StageEvidenceReady,
                readiness.BatchEvidenceReady,
                traceId = context.TraceIdentifier
            });
        return false;
    }

    private static async Task<bool> RequireImmutableNonProjectEvidenceAsync(
        HttpContext context)
    {
        var readiness = await LoadImmutableAuditReadinessAsync(context);
        if (readiness.BatchEvidenceReady) return true;

        await WriteJsonAsync(
            context,
            StatusCodes.Status503ServiceUnavailable,
            new
            {
                status = "immutable_activity_audit_unavailable",
                message = "Non-project activity creation is temporarily paused because immutable creation evidence cannot be guaranteed.",
                readiness.BatchEvidenceReady,
                traceId = context.TraceIdentifier
            });
        return false;
    }

    private static async Task<ImmutableAuditReadiness>
        LoadImmutableAuditReadinessAsync(HttpContext context)
    {
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(context.RequestAborted);
            await using var command = new NpgsqlCommand("""
                SELECT
                    to_regclass('public.scoped_approval_stage_events') IS NOT NULL,
                    EXISTS (
                        SELECT 1
                        FROM pg_trigger trigger_row
                        WHERE trigger_row.tgrelid =
                              to_regclass('public.scoped_approval_stage_events')
                          AND trigger_row.tgname =
                              'trg_projectpulse040_approval_audit_immutable'
                          AND trigger_row.tgenabled <> 'D'
                          AND NOT trigger_row.tgisinternal
                    ),
                    to_regclass('public.scoped_role_policy_audit_events') IS NOT NULL,
                    EXISTS (
                        SELECT 1
                        FROM pg_trigger trigger_row
                        WHERE trigger_row.tgrelid =
                              to_regclass('public.scoped_role_policy_audit_events')
                          AND trigger_row.tgname =
                              'trg_projectpulse040_policy_audit_immutable'
                          AND trigger_row.tgenabled <> 'D'
                          AND NOT trigger_row.tgisinternal
                    );
                """, connection);

            await using var reader = await command.ExecuteReaderAsync(
                context.RequestAborted);
            if (!await reader.ReadAsync(context.RequestAborted))
            {
                return ImmutableAuditReadiness.Unavailable;
            }

            var stageTable = reader.GetBoolean(0);
            var stageTrigger = reader.GetBoolean(1);
            var batchTable = reader.GetBoolean(2);
            var batchTrigger = reader.GetBoolean(3);
            return new ImmutableAuditReadiness(
                stageTable && stageTrigger,
                batchTable && batchTrigger);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"ProjectPulse immutable approval audit readiness failed. traceId={context.TraceIdentifier} exception={exception}");
            return ImmutableAuditReadiness.Unavailable;
        }
    }

    private static async Task WriteJsonAsync(
        HttpContext context,
        int statusCode,
        object payload)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(
            payload,
            cancellationToken: context.RequestAborted);
    }

    private static string ConnectionString()
    {
        foreach (var name in new[]
        {
            "ConnectionStrings__DefaultConnection",
            "ConnectionStrings__ProjectPulse",
            "ConnectionStrings__ProjectTime",
            "PROJECTPULSE_CONNECTION_STRING",
            "PROJECTTIME_DATABASE_CONNECTION"
        })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        throw new InvalidOperationException(
            "ProjectPulse database connection is not configured.");
    }

    private sealed record ImmutableAuditReadiness(
        bool StageEvidenceReady,
        bool BatchEvidenceReady)
    {
        public static ImmutableAuditReadiness Unavailable { get; } =
            new(false, false);
    }
}
