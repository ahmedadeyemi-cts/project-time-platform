using System.Text.Json;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Keeps the PR #284 public routes compatible while ensuring every caller uses
/// the production workflow implementation. The earlier endpoint handlers remain
/// compiled for rollback/reference purposes but are no longer reachable.
/// </summary>
public static class ProductionApprovalWorkCompatibility
{
    public static WebApplication UseProductionApprovalWorkCompatibility(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? string.Empty;

            if (HttpMethods.IsGet(context.Request.Method)
                && path.Equals("/api/approval-work/pending", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers["X-ProjectPulse-Approval-Contract"] =
                    "approval-work-production-v2-2026-07-30";
                var result = await ProductionApprovalWorkModule.GetPendingAsync(context);
                await result.ExecuteAsync(context);
                return;
            }

            if (HttpMethods.IsPost(context.Request.Method)
                && path.Equals("/api/approval-work/bulk-complete", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers["X-ProjectPulse-Approval-Contract"] =
                    "approval-work-production-v2-2026-07-30";
                try
                {
                    var request = await context.Request.ReadFromJsonAsync<
                        ProductionApprovalWorkModule.BulkCompleteRequest>(
                        cancellationToken: context.RequestAborted);
                    if (request is null)
                    {
                        await WriteBadRequestAsync(
                            context,
                            "invalid_request",
                            "The bulk approval request body is required.");
                        return;
                    }

                    var result = await ProductionApprovalWorkModule.BulkCompleteAsync(request, context);
                    await result.ExecuteAsync(context);
                    return;
                }
                catch (JsonException)
                {
                    await WriteBadRequestAsync(
                        context,
                        "invalid_json",
                        "The bulk approval request is not valid JSON.");
                    return;
                }
            }

            if (HttpMethods.IsPost(context.Request.Method)
                && path.Equals("/api/timesheet/ptc/non-project-tasks", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers["X-ProjectPulse-Non-Project-Contract"] =
                    "non-project-activity-create-only-v2";
                try
                {
                    var request = await context.Request.ReadFromJsonAsync<
                        ProductionApprovalWorkModule.NonProjectActivityRequest>(
                        cancellationToken: context.RequestAborted);
                    if (request is null)
                    {
                        await WriteBadRequestAsync(
                            context,
                            "invalid_request",
                            "The non-project activity request body is required.");
                        return;
                    }

                    var result = await ProductionApprovalWorkModule.CreateNonProjectActivityAsync(
                        request,
                        context);
                    await result.ExecuteAsync(context);
                    return;
                }
                catch (JsonException)
                {
                    await WriteBadRequestAsync(
                        context,
                        "invalid_json",
                        "The non-project activity request is not valid JSON.");
                    return;
                }
            }

            await next();
        });

        return app;
    }

    private static async Task WriteBadRequestAsync(
        HttpContext context,
        string status,
        string message)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status,
            message,
            traceId = context.TraceIdentifier
        }, cancellationToken: context.RequestAborted);
    }
}