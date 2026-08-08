namespace ProjectTime.Api.Modules;

/// <summary>
/// Runs the idempotent stale-expense readiness reconciliation only for an
/// authenticated, non-View-As billing read. Unauthenticated deployment probes
/// and View-As previews never mutate billing readiness. The same authenticated
/// boundary also serves the read-only unified billing-journey projection.
/// </summary>
public static partial class Module005ProjectExpenseUploadModule
{
    public static WebApplication UseProjectExpenseBillingReadinessContinuitySafe(
        this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (await TryHandleBillingJourneyRequestAsync(context)) return;

            var path = context.Request.Path.Value ?? string.Empty;
            var isCandidateRead = HttpMethods.IsGet(context.Request.Method)
                && (path.Equals("/api/billing/candidates", StringComparison.OrdinalIgnoreCase)
                    || (path.StartsWith("/api/billing/projects/", StringComparison.OrdinalIgnoreCase)
                        && path.EndsWith("/candidates", StringComparison.OrdinalIgnoreCase)));
            var actualUser = ProjectPulseActualSessionAuthority.ReadUserId(
                context,
                "ProjectPulseActualUserId",
                "ProjectPulseSessionUserId");

            if (isCandidateRead
                && actualUser.HasValue
                && !ProjectPulseActualSessionAuthority.IsViewAs(context))
            {
                try
                {
                    await using var connection = await OpenConnectionAsync();
                    await BlockStaleExpenseReadinessAsync(
                        connection,
                        null,
                        context.RequestAborted);
                    context.Items["ProjectPulseExpenseReadinessContinuity"] =
                        "authenticated_candidate_read_v1";
                }
                catch (Exception exception)
                {
                    context.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("ProjectExpenseBillingReadinessContinuity")
                        .LogWarning(
                            "Stale project-expense billing readiness reconciliation was unavailable ({ExceptionType}).",
                            exception.GetType().Name);
                }
            }

            await next();
        });
        return app;
    }
}
