using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

/// <summary>Module 065 owns Teams configuration and delivery. No credentials enter the browser.</summary>
public static class MicrosoftTeamsNotificationModule
{
    internal sealed record Configuration(string Environment, bool Enabled, string DeliveryMode, Guid? TeamsAppId,
        string? WorkflowTriggerUrl, string WorkflowAudience, int Revision);
    private sealed record SaveRequest(bool Enabled, string? DeliveryMode, Guid? TeamsAppId,
        string? WorkflowTriggerUrl, string? WorkflowAudience, int ExpectedRevision);
    private sealed record TestRequest(string Recipient, string Confirmation);
    private sealed record CheckRequest(string Recipient);
    public static WebApplication MapMicrosoftTeamsNotificationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/microsoft-integration/teams", (Func<HttpContext, Task<IResult>>)GetAsync);
        app.MapPut("/api/microsoft-integration/teams", (Func<HttpContext, Task<IResult>>)SaveAsync);
        app.MapPost("/api/microsoft-integration/teams/test-delivery", (Func<HttpContext, Task<IResult>>)TestAsync);
        app.MapPost("/api/microsoft-integration/teams/check-installation", (Func<HttpContext, Task<IResult>>)CheckInstallationAsync);
        return app;
    }

    private static async Task<IResult> GetAsync(HttpContext context)
    {
        var access = await AdminExperienceCommon.AuthorizeAsync(context);
        if (access.Failure is not null) return access.Failure;
        var environment = MicrosoftEnvironmentRuntimeResolver.Resolve(context);
        if (environment is not ("test" or "production")) return Results.Conflict(new { message = "Runtime environment is unresolved." });
        await using var connection = new NpgsqlConnection(access.Context!.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);
        var configuration = await LoadAsync(connection, environment, context.RequestAborted);
        var deliveries = new List<object>();
        await using var command = new NpgsqlCommand("SELECT recipient,status,diagnostic_code,updated_at FROM module065_teams_delivery WHERE environment=@env ORDER BY updated_at DESC LIMIT 20", connection);
        command.Parameters.AddWithValue("env", environment);
        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
        while (await reader.ReadAsync(context.RequestAborted))
        {
            var evidence = MicrosoftTeamsNotificationProtocol.ReadStored(reader.GetString(2));
            deliveries.Add(new { recipient = reader.GetString(0), status = reader.GetString(1), diagnosticCode = evidence.Code,
                diagnosticMessage = evidence.Message, graphErrorCode = evidence.GraphErrorCode, graphRequestId = evidence.RequestId,
                updatedAt = reader.GetFieldValue<DateTimeOffset>(3) });
        }
        return Results.Ok(new { configuration, deliveries, readOnly = AdminExperienceCommon.IsViewAs(context),
            message = configuration.DeliveryMode == "power_automate"
                ? "Power Automate mode uses the centralized Module 065 Microsoft services connection. No Pulse Teams app installation is required for recipients."
                : "Legacy Graph-app mode requires a Teams app installed for each recipient. Saving is not a successful connection test." });
    }

    private static async Task<IResult> SaveAsync(HttpContext context)
    {
        if (!AiProviderConfigurationModule.SameOrigin(context)) return Results.Json(new { message = "A same-origin request is required." }, statusCode: 403);
        var access = await AdminExperienceCommon.AuthorizeAsync(context);
        if (access.Failure is not null) return access.Failure;
        if (AdminExperienceCommon.IsViewAs(context)) return Results.Json(new { message = "Exit View-As to change Teams configuration." }, statusCode: 403);
        var environment = MicrosoftEnvironmentRuntimeResolver.Resolve(context);
        if (environment is not ("test" or "production")) return Results.Conflict(new { message = "Runtime environment is unresolved." });
        var request = await context.Request.ReadFromJsonAsync<SaveRequest>(cancellationToken: context.RequestAborted);
        var mode = request?.DeliveryMode?.Trim().ToLowerInvariant() ?? "power_automate";
        var audience = string.IsNullOrWhiteSpace(request?.WorkflowAudience) ? "https://service.flow.microsoft.com/" : request!.WorkflowAudience!.Trim();
        var workflowUrl = request?.WorkflowTriggerUrl?.Trim();
        if (request is null || request.ExpectedRevision < 0 || mode is not ("power_automate" or "graph_app")
            || (request.Enabled && mode == "graph_app" && (request.TeamsAppId is null || request.TeamsAppId == Guid.Empty))
            || (request.Enabled && mode == "power_automate" && !MicrosoftTeamsWorkflowProtocol.ValidTriggerUrl(workflowUrl, out _)))
            return Results.BadRequest(new { message = mode == "power_automate"
                ? "A valid Power Automate HTTPS trigger URL is required before enabling Teams workflow delivery."
                : "An installed Teams app ID is required before enabling legacy Graph-app delivery." });
        await using var connection = new NpgsqlConnection(access.Context!.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);
        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        await using var command = new NpgsqlCommand("""
            INSERT INTO module065_teams_configuration(environment,enabled,delivery_mode,teams_app_id,workflow_trigger_url,workflow_audience,updated_by)
            SELECT @env,@enabled,@mode,@app,@workflow,@audience,@actor WHERE @revision=0
            ON CONFLICT(environment) DO NOTHING;
            """, connection, transaction);
        command.Parameters.AddWithValue("env", environment);
        command.Parameters.AddWithValue("enabled", request.Enabled);
        command.Parameters.AddWithValue("mode", mode);
        command.Parameters.AddWithValue("app", NpgsqlDbType.Uuid, (object?)request.TeamsAppId ?? DBNull.Value);
        command.Parameters.AddWithValue("workflow", NpgsqlDbType.Text, (object?)workflowUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("audience", audience);
        command.Parameters.AddWithValue("actor", access.Context.UserId);
        command.Parameters.AddWithValue("revision", request.ExpectedRevision);
        if (request.ExpectedRevision > 0) command.CommandText = "UPDATE module065_teams_configuration SET enabled=@enabled,delivery_mode=@mode,teams_app_id=@app,workflow_trigger_url=@workflow,workflow_audience=@audience,updated_by=@actor,revision=revision+1,updated_at=now() WHERE environment=@env AND revision=@revision";
        if (await command.ExecuteNonQueryAsync(context.RequestAborted) != 1) return Results.Conflict(new { message = "Teams configuration changed. Reload before saving." });
        var audited = await AdminExperienceCommon.WriteAuditAsync(connection, transaction, "configuration", "success", "module065_teams_configuration_saved", access.Context.UserId, access.Context.Email,
            "teams_configuration", environment, "Microsoft Teams", "065", "module065_teams_configuration", environment, "Teams notification configuration changed.",
            new { request.Enabled, DeliveryMode = mode, request.TeamsAppId, WorkflowConfigured = workflowUrl is not null, WorkflowAudience = audience, request.ExpectedRevision }, context.Connection.RemoteIpAddress?.ToString() ?? "", context.TraceIdentifier, context.RequestAborted);
        if (!audited) return Results.Conflict(new { message = "Audit storage is unavailable; configuration was not saved." });
        await transaction.CommitAsync(context.RequestAborted);
        return Results.Ok(new { message = mode == "power_automate"
            ? "Teams workflow configuration saved. Run one Test delivery to verify the centrally managed Power Automate path."
            : "Legacy Teams app configuration saved. Run a delivery test to verify permissions and app installation." });
    }

    private static async Task<IResult> TestAsync(HttpContext context)
    {
        if (!AiProviderConfigurationModule.SameOrigin(context)) return Results.Json(new { message = "A same-origin request is required." }, statusCode: 403);
        var access = await AdminExperienceCommon.AuthorizeAsync(context);
        if (access.Failure is not null) return access.Failure;
        if (AdminExperienceCommon.IsViewAs(context)) return Results.Json(new { message = "Exit View-As before testing Teams delivery." }, statusCode: 403);
        var request = await context.Request.ReadFromJsonAsync<TestRequest>(cancellationToken: context.RequestAborted);
        if (request is null || request.Confirmation != "SEND TEAMS TEST" || !ValidEmail(request.Recipient)) return Results.BadRequest(new { message = "Enter a valid tenant user email and confirm SEND TEAMS TEST." });
        var isSuperAdmin = access.Context!.Roles.Contains("SUPER_ADMINISTRATOR");
        if (!isSuperAdmin && !request.Recipient.Trim().Equals(access.Context.Email, StringComparison.OrdinalIgnoreCase)) return Results.Json(new { message = "Only a SuperAdmin can send a Teams test to another tenant user." }, statusCode: 403);
        await using var connection = new NpgsqlConnection(access.Context.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);
        var readiness = await Module065ProjectNotificationDelivery.GetReadinessAsync(context, context.RequestAborted);
        if (readiness.RuntimeEnvironment != "test" || readiness.ConfiguredEnvironment != "test" || readiness.RecipientBoundary == "locked")
            return Results.Conflict(new { message = "Teams tests require the active Test profile with an unlocked recipient boundary." });
        var configuration = await LoadAsync(connection, "test", context.RequestAborted);
        if (!configuration.Enabled) return Results.Conflict(new { message = "Save and enable a Teams delivery mode first." });
        var result = configuration.DeliveryMode == "power_automate"
            ? await DeliverWorkflowTestAsync(connection, configuration, request.Recipient.Trim(), context, context.RequestAborted)
            : configuration.TeamsAppId is null
                ? "failed"
                : await DeliverOneAsync(connection, configuration, Guid.NewGuid(), request.Recipient.Trim(), context, context.RequestAborted, manualTest: true);
        return Results.Json(new { status = result, deliveryMode = configuration.DeliveryMode,
            message = result == "sent"
                ? (configuration.DeliveryMode == "power_automate" ? "Power Automate accepted the Teams test request. Confirm the Flow run and Teams message." : "Microsoft Graph accepted the Teams test. Confirm receipt in Teams.")
                : "Teams test was not confirmed. Review the delivery diagnostic below." }, statusCode: result == "sent" ? 200 : 502);
    }

    internal static async Task TryDeliverDispatchAsync(NpgsqlConnection connection, ProjectNotificationDispatchRow dispatch, HttpContext? context, CancellationToken cancellationToken)
    {
        try { await DeliverDispatchAsync(connection, dispatch, context, cancellationToken); }
        catch (Exception exception)
        {
            // Email has already been finalized. Never replay it due to an independent Teams failure.
            System.Diagnostics.Trace.TraceError("Module 065 Teams delivery persistence failed for dispatch {0}: {1}", dispatch.DispatchId, exception.GetType().Name);
        }
    }

    private static async Task DeliverDispatchAsync(NpgsqlConnection connection, ProjectNotificationDispatchRow dispatch, HttpContext? context, CancellationToken cancellationToken)
    {
        // Same server-derived recipients and stricter boundary as email; never send from a Test-only scheduler.
        var readiness = await Module065ProjectNotificationDelivery.GetReadinessAsync(context, cancellationToken);
        if (ProjectNotificationEvaluator.MoreRestrictiveBoundary(dispatch.DeliveryBoundary, readiness.RecipientBoundary) != "production_governed"
            || readiness.RuntimeEnvironment != readiness.ConfiguredEnvironment) return;
        if (!await AdminExperienceCommon.TableExistsAsync(connection, "module065_teams_configuration", cancellationToken: cancellationToken)) return;
        var configuration = await LoadAsync(connection, readiness.RuntimeEnvironment, cancellationToken);
        if (!configuration.Enabled) return;
        if (configuration.DeliveryMode == "power_automate")
        {
            await DeliverWorkflowDispatchAsync(connection, configuration, dispatch, context, cancellationToken);
            return;
        }
        if (configuration.TeamsAppId is null) return;
        foreach (var recipient in dispatch.Recipients.Where(r => ValidEmail(r.Email)).DistinctBy(r => r.Email.ToLowerInvariant()))
            await DeliverOneAsync(connection, configuration, dispatch.DispatchId, recipient.Email, context, cancellationToken);
    }

    private static async Task<Configuration> LoadAsync(NpgsqlConnection connection, string environment, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT enabled,delivery_mode,teams_app_id,workflow_trigger_url,workflow_audience,revision FROM module065_teams_configuration WHERE environment=@env", connection);
        command.Parameters.AddWithValue("env", environment);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(environment, reader.GetBoolean(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetInt32(5))
            : new(environment, false, "power_automate", null, null, "https://service.flow.microsoft.com/", 0);
    }

    private static async Task<IResult> CheckInstallationAsync(HttpContext context)
    {
        if (!AiProviderConfigurationModule.SameOrigin(context)) return Results.Json(new { message = "A same-origin request is required." }, statusCode: 403);
        var access = await AdminExperienceCommon.AuthorizeAsync(context);
        if (access.Failure is not null) return access.Failure;
        if (AdminExperienceCommon.IsViewAs(context)) return Results.Json(new { message = "Exit View-As to check this tenant user's Teams installation." }, statusCode: 403);
        CheckRequest? request;
        try { request = await context.Request.ReadFromJsonAsync<CheckRequest>(cancellationToken: context.RequestAborted); }
        catch (JsonException) { return Results.BadRequest(new { message = "Enter one recipient sign-in address." }); }
        if (request is null || !ValidEmail(request.Recipient)) return Results.BadRequest(new { message = "Enter one valid tenant sign-in address." });
        if (!access.Context!.Roles.Contains("SUPER_ADMINISTRATOR") && !request.Recipient.Trim().Equals(access.Context.Email, StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { message = "Only a SuperAdmin may check another recipient." }, statusCode: 403);
        var environment = MicrosoftEnvironmentRuntimeResolver.Resolve(context);
        if (environment is not ("test" or "production")) return Results.Conflict(new { message = "Runtime environment is unresolved." });
        await using var connection = new NpgsqlConnection(access.Context.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);
        var configuration = await LoadAsync(connection, environment, context.RequestAborted);
        if (configuration.DeliveryMode == "power_automate")
            return Results.Ok(new { status = "not_required", diagnosticCode = "teams_app_installation_not_required",
                message = "Power Automate mode does not require PulseApp to be installed for the recipient. Use Send Teams test to validate the central workflow.", notificationSent = false });
        if (configuration.TeamsAppId is null || configuration.TeamsAppId == Guid.Empty)
            return Results.Conflict(new { message = "Save the Teams package ID first." });
        try
        {
            var services = await MicrosoftTeamsServicesSnapshot.LoadAsync(connection, environment, context.RequestAborted);
            using var client = NewClient();
            var result = await MicrosoftTeamsNotificationProtocol.ExecuteAsync(client, services.Profile.TenantId, services.Profile.ClientId,
                services.Secret, configuration.TeamsAppId.Value, request.Recipient, false, _ => Task.FromResult(false), context.RequestAborted);
            return Results.Ok(new { status = result.Status, diagnosticCode = result.Diagnostic.Code, message = result.Diagnostic.Message,
                result.Diagnostic.GraphErrorCode, graphRequestId = result.Diagnostic.RequestId, result.CatalogAppId, result.InstalledVersion,
                services.Profile.TenantId, services.Profile.ClientId, configuration.Environment, notificationSent = false });
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
        { return Results.Conflict(new { message = "The saved environment services profile or credential is incomplete. Save it in Module 065; no notification was sent." }); }
    }

    private static HttpClient NewClient() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(30) };

    private static async Task<string> DeliverOneAsync(NpgsqlConnection connection, Configuration configuration, Guid dispatchId, string recipient, HttpContext? context, CancellationToken ct, bool manualTest = false)
    {
        var id = Guid.NewGuid();
        // Durable claim before any external call. Unknown outcomes are never automatically replayed.
        await using var claim = new NpgsqlCommand("INSERT INTO module065_teams_delivery(delivery_id,dispatch_id,environment,recipient,status) VALUES(@id,@dispatch,@env,@recipient,'sending') ON CONFLICT(dispatch_id,environment,recipient) DO NOTHING", connection);
        claim.Parameters.AddWithValue("id", id); claim.Parameters.AddWithValue("dispatch", dispatchId);
        claim.Parameters.AddWithValue("env", configuration.Environment); claim.Parameters.AddWithValue("recipient", recipient.Trim().ToLowerInvariant());
        if (await claim.ExecuteNonQueryAsync(ct) != 1) return "already_claimed";
        var result = new MicrosoftTeamsNotificationProtocol.Outcome("failed", new("teams_services_configuration_incomplete", "The saved Module 065 services profile or credential is incomplete. No notification was sent."));
        try
        {
            var services = await MicrosoftTeamsServicesSnapshot.LoadAsync(connection, configuration.Environment, ct);
            using var client = NewClient();
            result = await MicrosoftTeamsNotificationProtocol.ExecuteAsync(client, services.Profile.TenantId, services.Profile.ClientId,
                services.Secret, configuration.TeamsAppId!.Value, recipient, true, async token =>
                {
                    var current = await LoadAsync(connection, configuration.Environment, token);
                    if (!current.Enabled || current != configuration) return false;
                    var latest = await MicrosoftTeamsServicesSnapshot.LoadAsync(connection, configuration.Environment, token);
                    if (!services.Matches(latest) || latest.Profile.RecipientBoundary == "locked"
                        || MicrosoftEnvironmentRuntimeResolver.Resolve(context) != configuration.Environment) return false;
                    if (!manualTest) return latest.Profile.RecipientBoundary == "production_governed";
                    if (context is null || configuration.Environment != "test" || AdminExperienceCommon.IsViewAs(context)) return false;
                    var access = await AdminExperienceCommon.AuthorizeAsync(context);
                    return access.Failure is null && (access.Context!.Roles.Contains("SUPER_ADMINISTRATOR")
                        || recipient.Equals(access.Context.Email, StringComparison.OrdinalIgnoreCase));
                }, ct);
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
        { /* Do not expose source configuration, credential or raw provider text. */ }
        using var persistTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var update = new NpgsqlCommand("UPDATE module065_teams_delivery SET status=@status,diagnostic_code=@code,updated_at=now() WHERE delivery_id=@id", connection);
        update.Parameters.AddWithValue("status", result.Status);
        update.Parameters.AddWithValue("code", MicrosoftTeamsNotificationProtocol.Store(result.Diagnostic));
        update.Parameters.AddWithValue("id", id);
        await update.ExecuteNonQueryAsync(persistTimeout.Token);
        return result.Status;
    }
    private static async Task<string> DeliverWorkflowTestAsync(NpgsqlConnection connection, Configuration configuration,
        string recipient, HttpContext context, CancellationToken ct)
    {
        var dispatchId = Guid.NewGuid();
        var envelope = new MicrosoftTeamsWorkflowProtocol.Envelope(
            dispatchId.ToString("D"), "individual", [recipient], "Pulse Teams notification test",
            "This is a Test notification from Pulse. No custom Pulse Teams app is required.",
            "information", "manual_test", "065", PublicPulseUrl(), dispatchId.ToString("D"));
        return await DeliverWorkflowEnvelopeAsync(connection, configuration, dispatchId, "workflow-test:" + recipient,
            envelope, context, ct, manualTest: true);
    }

    private static async Task DeliverWorkflowDispatchAsync(NpgsqlConnection connection, Configuration configuration,
        ProjectNotificationDispatchRow dispatch, HttpContext? context, CancellationToken ct)
    {
        var recipients = dispatch.Recipients.Where(r => ValidEmail(r.Email))
            .Select(r => r.Email.Trim().ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (recipients.Length == 0) return;
        var group = recipients.Length > 1 && (
            dispatch.NotificationType.Contains("cost", StringComparison.OrdinalIgnoreCase)
            || dispatch.Subject.Contains("cost", StringComparison.OrdinalIgnoreCase)
            || dispatch.EventKey.Contains("cost", StringComparison.OrdinalIgnoreCase));
        var envelope = new MicrosoftTeamsWorkflowProtocol.Envelope(
            dispatch.DispatchId.ToString("D"), group ? "group_chat" : "individual", recipients,
            dispatch.Subject, dispatch.TextBody, dispatch.AlertSeverity, dispatch.NotificationType,
            dispatch.SourceModule, PublicPulseUrl(), dispatch.DispatchId.ToString("D"));
        await DeliverWorkflowEnvelopeAsync(connection, configuration, dispatch.DispatchId,
            group ? "workflow-group" : "workflow-individual", envelope, context, ct, manualTest: false);
    }

    private static async Task<string> DeliverWorkflowEnvelopeAsync(NpgsqlConnection connection, Configuration configuration,
        Guid dispatchId, string deliveryKey, MicrosoftTeamsWorkflowProtocol.Envelope envelope,
        HttpContext? context, CancellationToken ct, bool manualTest)
    {
        var id = Guid.NewGuid();
        await using var claim = new NpgsqlCommand("INSERT INTO module065_teams_delivery(delivery_id,dispatch_id,environment,recipient,status) VALUES(@id,@dispatch,@env,@recipient,'sending') ON CONFLICT(dispatch_id,environment,recipient) DO NOTHING", connection);
        claim.Parameters.AddWithValue("id", id); claim.Parameters.AddWithValue("dispatch", dispatchId);
        claim.Parameters.AddWithValue("env", configuration.Environment); claim.Parameters.AddWithValue("recipient", deliveryKey);
        if (await claim.ExecuteNonQueryAsync(ct) != 1) return "already_claimed";

        var outcome = new MicrosoftTeamsWorkflowProtocol.Outcome("failed",
            new("teams_workflow_services_configuration_incomplete", "The centrally managed Microsoft services connection or Power Automate workflow is incomplete."));
        try
        {
            var services = await MicrosoftTeamsServicesSnapshot.LoadAsync(connection, configuration.Environment, ct);
            if (!string.IsNullOrWhiteSpace(configuration.WorkflowTriggerUrl))
            {
                if (manualTest)
                {
                    if (context is null || configuration.Environment != "test" || AdminExperienceCommon.IsViewAs(context))
                        throw new InvalidOperationException("Manual workflow test authority unavailable");
                    var access = await AdminExperienceCommon.AuthorizeAsync(context);
                    if (access.Failure is not null) throw new InvalidOperationException("Manual workflow test authority unavailable");
                }
                else if (services.Profile.RecipientBoundary != "production_governed") return "suppressed";

                using var client = NewClient();
                outcome = await MicrosoftTeamsWorkflowProtocol.ExecuteAsync(client, services.Profile.TenantId, services.Profile.ClientId,
                    services.Secret, configuration.WorkflowAudience, configuration.WorkflowTriggerUrl, envelope, ct);
            }
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
        { }

        using var persistTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var update = new NpgsqlCommand("UPDATE module065_teams_delivery SET status=@status,diagnostic_code=@code,updated_at=now() WHERE delivery_id=@id", connection);
        update.Parameters.AddWithValue("status", outcome.Status);
        update.Parameters.AddWithValue("code", JsonSerializer.Serialize(new { code = outcome.Diagnostic.Code, message = outcome.Diagnostic.Message, requestId = outcome.Diagnostic.RequestId, workflowRunId = outcome.WorkflowRunId }));
        update.Parameters.AddWithValue("id", id);
        await update.ExecuteNonQueryAsync(persistTimeout.Token);
        return outcome.Status;
    }

    private static string PublicPulseUrl()
    {
        var value = Environment.GetEnvironmentVariable("PROJECTPULSE_PUBLIC_BASE_URL");
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? new Uri(uri, "/#dashboard").AbsoluteUri : "https://phd-west-test.onenecklab.com/#dashboard";
    }

    private static bool ValidEmail(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 320 && MailAddress.TryCreate(value.Trim(), out var address) && address.Address == value.Trim();
}
