namespace ProjectTime.Api.Modules;

/// <summary>
/// Group 4 route ownership for Modules 022, 023, 032, and notification portions
/// of Modules 018 and 041. Configuration and delivery are implemented by the
/// module-owned service; Module 065 remains the only mail-provider authority.
/// </summary>
public static class ProjectNotificationAutomationModule
{
    public static IEndpointRouteBuilder MapProjectNotificationAutomationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/project-notifications/routing-rules",
            (Func<HttpContext, Task<IResult>>)ProjectNotificationAutomationService.GetRoutingRulesAsync);
        endpoints.MapPut(
            "/api/project-notifications/routing-rules/{ruleId:guid}",
            (Func<Guid, ProjectCostRoutingRuleUpdateRequest, HttpContext, Task<IResult>>)ProjectNotificationAutomationService.UpdateRoutingRuleAsync);
        endpoints.MapGet(
            "/api/project-notifications/schedules",
            (Func<HttpContext, Task<IResult>>)ProjectNotificationAutomationService.GetSchedulesAsync);
        endpoints.MapPut(
            "/api/project-notifications/schedules/{scheduleId:guid}",
            (Func<Guid, ProjectNotificationScheduleUpdateRequest, HttpContext, Task<IResult>>)ProjectNotificationAutomationService.UpdateScheduleAsync);
        endpoints.MapGet(
            "/api/project-notifications/module-065-readiness",
            (Func<HttpContext, Task<IResult>>)ProjectNotificationAutomationService.GetModule065ReadinessAsync);
        endpoints.MapPost(
            "/api/project-notifications/evaluate",
            (Func<ProjectNotificationEvaluationRequest, HttpContext, Task<IResult>>)ProjectNotificationAutomationService.EvaluateAsync);
        endpoints.MapGet(
            "/api/project-notifications/dispatches",
            (Func<HttpContext, Task<IResult>>)ProjectNotificationAutomationService.GetDispatchesAsync);
        endpoints.MapGet(
            "/api/project-notifications/delivery-monitor",
            (Func<HttpContext, Task<IResult>>)ProjectNotificationAutomationService.GetDeliveryMonitorAsync);
        endpoints.MapPost(
            "/api/project-notifications/dispatches/{dispatchId:guid}/release",
            (Func<Guid, ProjectNotificationReleaseRequest, HttpContext, Task<IResult>>)ProjectNotificationAutomationService.ReleaseDispatchAsync);
        endpoints.MapPost(
            "/api/project-notifications/dispatches/{dispatchId:guid}/retry",
            (Func<Guid, ProjectNotificationReleaseRequest, HttpContext, Task<IResult>>)ProjectNotificationAutomationService.RetryDispatchAsync);
        endpoints.MapPost(
            "/api/project-notifications/run-due",
            (Func<HttpContext, Task<IResult>>)ProjectNotificationQuietHoursService.RunDueAsync);
        endpoints.MapPost(
            "/api/project-notifications/closeout/queue",
            (Func<ProjectCloseoutNotificationRequest, HttpContext, Task<IResult>>)ProjectNotificationAutomationService.QueueCloseoutAsync);

        if (endpoints is WebApplication application)
            ProjectNotificationScheduler.Start(application);

        return endpoints;
    }

    /// <summary>
    /// The historic Module 041 route remains compatible, but the browser-provided
    /// recipient list and legacy SMTP/sendmail implementation are bypassed. The
    /// service resolves the project team from authoritative server data and routes
    /// every delivery decision through Module 065.
    /// </summary>
    public static WebApplication UseProjectNotificationCloseoutCompatibility(
        this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsPost(context.Request.Method)
                || !context.Request.Path.Equals(
                    "/api/project-closeout/email/send",
                    StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            ProjectCloseoutNotificationRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<ProjectCloseoutNotificationRequest>(
                    cancellationToken: context.RequestAborted);
            }
            catch
            {
                await Results.BadRequest(new
                {
                    module = "041",
                    status = "invalid_closeout_notification_request",
                    message = "A valid project closeout notification request is required."
                }).ExecuteAsync(context);
                return;
            }

            var result = await ProjectNotificationAutomationService.QueueCloseoutAsync(
                request ?? new(null, null, null, null, null, null, null, null, null),
                context);
            await result.ExecuteAsync(context);
        });
        return app;
    }
}
