using System.Reflection;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Activates the migration-064 enterprise notification orchestration through an
/// application-lifetime worker and an explicitly authorized operational endpoint.
///
/// The WebApplication overload intentionally takes precedence over the historic
/// IEndpointRouteBuilder Module 026 mapper. It delegates to that mapper first, so
/// the existing CRM/ERP surface remains unchanged, and then registers the runtime
/// that was missing from the consolidated release.
/// </summary>
public static class EnterpriseNotificationRuntimeBootstrap
{
    private const string RuntimeMigrationId = "065_enterprise_notification_runtime_completion";
    private const int MaximumEventsPerRun = 100;
    private static readonly TimeSpan WorkerInterval = TimeSpan.FromSeconds(
        ReadBoundedInteger("PROJECTPULSE_ENTERPRISE_NOTIFICATION_INTERVAL_SECONDS", 300, 60, 3600));
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(
        ReadBoundedInteger("PROJECTPULSE_ENTERPRISE_NOTIFICATION_INITIAL_DELAY_SECONDS", 20, 1, 300));
    private static readonly SemaphoreSlim RunGate = new(1, 1);
    private static readonly object StateGate = new();
    private static int _workerStarted;
    private static int _runInProgress;
    private static bool _projectManagementTimeEntryContractApplied;
    private static string _lastStatus = "not_started";
    private static string _lastDiagnosticCode = string.Empty;
    private static string _lastMessage = "The enterprise notification worker has not run yet.";
    private static DateTimeOffset? _lastStartedAt;
    private static DateTimeOffset? _lastCompletedAt;

    private static readonly string[] ProjectManagementRoleCodes =
    [
        "PROJECT_MANAGER",
        "PROJECT_MANAGEMENT",
        "PROJECT_MANAGEMENT_LEAD",
        "PROJECT_MANAGEMENT_TEAM_LEAD",
        "PM_TEAM_LEAD"
    ];

    public static WebApplication MapCrmErpIntegrationEndpoints(this WebApplication app)
    {
        // Preserve the existing Module 026 endpoint map before adding the
        // enterprise notification runtime to the same application startup path.
        CrmErpIntegrationModule.MapCrmErpIntegrationEndpoints((IEndpointRouteBuilder)app);

        ApplyProjectManagementTimeEntryContract();

        app.MapGet(
            "/api/enterprise-notifications/runtime/readiness",
            (Func<HttpContext, Task<IResult>>)GetRuntimeReadinessAsync);
        app.MapPost(
            "/api/enterprise-notifications/runtime/run",
            (Func<HttpContext, Task<IResult>>)RunManuallyAsync);

        StartWorker(app);
        return app;
    }

    private static void StartWorker(WebApplication app)
    {
        if (Interlocked.Exchange(ref _workerStarted, 1) == 1) return;

        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStarted.Register(() =>
        {
            _ = Task.Run(
                () => RunWorkerAsync(app.Services, lifetime.ApplicationStopping),
                CancellationToken.None);
        });
    }

    private static async Task RunWorkerAsync(
        IServiceProvider services,
        CancellationToken stoppingToken)
    {
        var logger = services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("EnterpriseNotificationRuntime");

        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var summary = await ExecuteRunAsync(
                    context: null,
                    startedByUserId: null,
                    runType: "scheduled_worker",
                    scanAuthoritativeSources: true,
                    maximumEvents: MaximumEventsPerRun,
                    stoppingToken);

                if (summary is not null)
                {
                    logger.LogInformation(
                        "Enterprise notification worker completed with status {Status}; observed {ObservedCount}, created {CreatedCount}, processed {ProcessedCount}.",
                        summary.Status,
                        summary.ObservedCount,
                        summary.CreatedCount,
                        summary.Dispatches.Length);
                }
            }
            catch (EnterpriseNotificationRuntimeNotReadyException exception)
            {
                RecordRuntimeState(
                    "configuration_pending",
                    "MIGRATION_065_REQUIRED",
                    exception.Message,
                    _lastStartedAt,
                    DateTimeOffset.UtcNow);
                logger.LogWarning(
                    "Enterprise notification worker is waiting for migration 065 and the migration-064 runtime schema.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                RecordRuntimeState(
                    "failed",
                    "ENTERPRISE_NOTIFICATION_RUNTIME_FAILED",
                    "The enterprise notification worker encountered a dependency failure. Review sanitized server diagnostics.",
                    _lastStartedAt,
                    DateTimeOffset.UtcNow);
                logger.LogError(exception, "Enterprise notification worker execution failed.");
            }

            try
            {
                await Task.Delay(WorkerInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private static async Task<IResult> GetRuntimeReadinessAsync(HttpContext context)
    {
        var access = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => !actor.IsViewAs && CanViewRuntime(actor));
        if (access.Failure is not null) return access.Failure;

        await using var connection = access.Connection!;
        try
        {
            var migration064Ready = await EnterpriseNotificationRepository.IsReadyAsync(
                connection,
                context.RequestAborted);
            var migration065Ready = await IsRuntimeMigrationReadyAsync(
                connection,
                context.RequestAborted);
            RuntimeStateSnapshot snapshot;
            lock (StateGate)
            {
                snapshot = new(
                    _lastStatus,
                    _lastDiagnosticCode,
                    _lastMessage,
                    _lastStartedAt,
                    _lastCompletedAt);
            }

            return Results.Ok(new
            {
                module = "065",
                status = migration064Ready && migration065Ready
                    ? "enterprise_notification_runtime_ready"
                    : "enterprise_notification_runtime_not_ready",
                migration064Ready,
                migration065Ready,
                workerRegistered = Volatile.Read(ref _workerStarted) == 1,
                runInProgress = Volatile.Read(ref _runInProgress) == 1,
                projectManagementTimeEntryContractApplied = _projectManagementTimeEntryContractApplied,
                intervalSeconds = (int)WorkerInterval.TotalSeconds,
                initialDelaySeconds = (int)InitialDelay.TotalSeconds,
                lastRun = snapshot,
                deliveryAuthority = "module_065",
                directSmtpAuthorized = false,
                directBrevoAuthorized = false
            });
        }
        catch (Exception exception)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("EnterpriseNotificationRuntime")
                .LogError(exception, "Enterprise notification runtime readiness failed.");
            return Results.Json(new
            {
                module = "065",
                status = "enterprise_notification_runtime_unavailable",
                diagnosticCode = "ENTERPRISE_NOTIFICATION_RUNTIME_DEPENDENCY_UNAVAILABLE",
                message = "Enterprise notification runtime readiness is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> RunManuallyAsync(HttpContext context)
    {
        var access = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => !actor.IsViewAs && CanRunRuntime(actor));
        if (access.Failure is not null) return access.Failure;

        await using var authorizationConnection = access.Connection!;
        var actor = access.Actor!;
        try
        {
            if (!await EnterpriseNotificationRepository.IsReadyAsync(
                    authorizationConnection,
                    context.RequestAborted)
                || !await IsRuntimeMigrationReadyAsync(
                    authorizationConnection,
                    context.RequestAborted))
            {
                return Results.Json(new
                {
                    module = "065",
                    status = "enterprise_notification_runtime_not_ready",
                    diagnosticCode = "MIGRATION_065_REQUIRED",
                    message = "Apply and verify migrations 064 and 065 before running enterprise notification orchestration."
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var summary = await ExecuteRunAsync(
                context,
                actor.ActualUserId,
                "manual_run",
                scanAuthoritativeSources: true,
                MaximumEventsPerRun,
                context.RequestAborted);

            if (summary is null)
            {
                return Results.Conflict(new
                {
                    module = "065",
                    status = "enterprise_notification_runtime_already_running",
                    message = "Another enterprise notification run is already active."
                });
            }

            return Results.Ok(new
            {
                module = "065",
                status = "enterprise_notification_runtime_completed",
                summary,
                deliveryAuthority = "module_065",
                directSmtpAuthorized = false,
                directBrevoAuthorized = false
            });
        }
        catch (EnterpriseNotificationRuntimeNotReadyException)
        {
            return Results.Json(new
            {
                module = "065",
                status = "enterprise_notification_runtime_not_ready",
                diagnosticCode = "MIGRATION_065_REQUIRED",
                message = "Apply and verify migrations 064 and 065 before running enterprise notification orchestration."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception exception)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("EnterpriseNotificationRuntime")
                .LogError(exception, "Manual enterprise notification runtime execution failed.");
            return Results.Json(new
            {
                module = "065",
                status = "enterprise_notification_runtime_failed",
                diagnosticCode = "ENTERPRISE_NOTIFICATION_RUNTIME_FAILED",
                message = "The enterprise notification run failed. Review sanitized server diagnostics."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<EnterpriseNotificationRunSummary?> ExecuteRunAsync(
        HttpContext? context,
        Guid? startedByUserId,
        string runType,
        bool scanAuthoritativeSources,
        int maximumEvents,
        CancellationToken cancellationToken)
    {
        if (!await RunGate.WaitAsync(0, cancellationToken)) return null;

        Interlocked.Exchange(ref _runInProgress, 1);
        var startedAt = DateTimeOffset.UtcNow;
        RecordRuntimeState(
            "running",
            string.Empty,
            "Enterprise notification orchestration is running.",
            startedAt,
            null);

        try
        {
            await using (var connection = await EnterpriseNotificationRepository.OpenConnectionAsync(cancellationToken))
            {
                if (!await EnterpriseNotificationRepository.IsReadyAsync(connection, cancellationToken)
                    || !await IsRuntimeMigrationReadyAsync(connection, cancellationToken))
                {
                    throw new EnterpriseNotificationRuntimeNotReadyException();
                }
            }

            var summary = await EnterpriseNotificationOrchestrationService.RunAsync(
                context,
                startedByUserId,
                runType,
                scanAuthoritativeSources,
                maximumEvents,
                cancellationToken);
            RecordRuntimeState(
                summary.Status,
                summary.FailedCount > 0 ? "PARTIAL_DELIVERY_FAILURE" : string.Empty,
                summary.Message,
                summary.StartedAt,
                summary.CompletedAt);
            return summary;
        }
        finally
        {
            Interlocked.Exchange(ref _runInProgress, 0);
            RunGate.Release();
        }
    }

    private static async Task<bool> IsRuntimeMigrationReadyAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM schema_migrations
                WHERE migration_id = @migration_id
            );
            """, connection);
        command.Parameters.AddWithValue("migration_id", RuntimeMigrationId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static void ApplyProjectManagementTimeEntryContract()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var timeEntryRoles = typeof(WorkLifecycleModule)
            .GetField("TimeEntryRoles", flags)
            ?.GetValue(null) as HashSet<string>;
        var excludedRoles = typeof(WorkLifecycleModule)
            .GetField("TimeEntryExcludedRoles", flags)
            ?.GetValue(null) as HashSet<string>;

        if (timeEntryRoles is null || excludedRoles is null)
        {
            throw new InvalidOperationException(
                "The Work Lifecycle time-entry role contract could not be initialized.");
        }

        foreach (var roleCode in ProjectManagementRoleCodes)
        {
            timeEntryRoles.Add(roleCode);
            excludedRoles.Remove(roleCode);
        }

        _projectManagementTimeEntryContractApplied = ProjectManagementRoleCodes.All(
            roleCode => timeEntryRoles.Contains(roleCode) && !excludedRoles.Contains(roleCode));
        if (!_projectManagementTimeEntryContractApplied)
        {
            throw new InvalidOperationException(
                "The Project Management time-entry role contract was not applied completely.");
        }
    }

    private static bool CanViewRuntime(ProjectNotificationActor actor) =>
        actor.IsAdministrator
        || actor.Roles.Any(IsProjectTeamCoordinatorRole)
        || actor.Permissions.Contains("VIEW_ENTERPRISE_NOTIFICATIONS_065")
        || actor.Permissions.Contains("RUN_ENTERPRISE_NOTIFICATIONS_065");

    private static bool CanRunRuntime(ProjectNotificationActor actor) =>
        actor.IsAdministrator
        || actor.Roles.Any(IsProjectTeamCoordinatorRole)
        || actor.Permissions.Contains("RUN_ENTERPRISE_NOTIFICATIONS_065");

    private static bool IsProjectTeamCoordinatorRole(string roleCode) =>
        roleCode.Equals("PROJECT_TEAM_COORDINATOR", StringComparison.OrdinalIgnoreCase)
        || roleCode.Equals("PROJECT_COORDINATOR", StringComparison.OrdinalIgnoreCase)
        || roleCode.Equals("PTC", StringComparison.OrdinalIgnoreCase);

    private static void RecordRuntimeState(
        string status,
        string diagnosticCode,
        string message,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt)
    {
        lock (StateGate)
        {
            _lastStatus = status;
            _lastDiagnosticCode = diagnosticCode;
            _lastMessage = message;
            _lastStartedAt = startedAt;
            _lastCompletedAt = completedAt;
        }
    }

    private static int ReadBoundedInteger(
        string environmentVariable,
        int fallback,
        int minimum,
        int maximum)
    {
        var raw = Environment.GetEnvironmentVariable(environmentVariable);
        return int.TryParse(raw, out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
    }

    private sealed record RuntimeStateSnapshot(
        string Status,
        string DiagnosticCode,
        string Message,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt);

    private sealed class EnterpriseNotificationRuntimeNotReadyException : Exception
    {
        internal EnterpriseNotificationRuntimeNotReadyException()
            : base("Migrations 064 and 065 must be applied before the enterprise notification runtime starts.")
        {
        }
    }
}
