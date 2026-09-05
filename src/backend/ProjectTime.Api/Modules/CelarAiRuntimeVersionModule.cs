using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Npgsql;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

public sealed record CelarAiRuntimeMaintenanceScheduleRequest(
    bool? Enabled,
    string? DayOfWeek,
    string? LocalTime,
    string? TimeZone);

/// <summary>
/// Module 084: administrator-only visibility and governed scheduling for the
/// private Oracle Celar runtime. Provider ordering remains Module 064 authority.
/// </summary>
public static class CelarAiRuntimeVersionModule
{
    public const string ModuleNumber = "084";
    public const string StatusRoute = "/api/celar-ai/v1/runtime-version/status";
    public const string ScheduleRoute = "/api/celar-ai/v1/runtime-version/schedule";
    public const string MaintenanceStatusPath = "/v1/maintenance/status";
    public const string MaintenanceSchedulePath = "/v1/maintenance/schedule";
    public const string MaintenanceTokenVariable = "PROJECTPULSE_CELAR_AI_MAINTENANCE_BEARER_TOKEN";
    public const string MaintenanceTokenReferenceVariable = "PROJECTPULSE_CELAR_AI_MAINTENANCE_BEARER_TOKEN_SECRET_REFERENCE";
    private const string PrivacyBoundary = "private_pulse_runtime_only";
    private const int MaximumOracleResponseBytes = 256 * 1024;
    private static readonly HashSet<string> AllowedDays = new(StringComparer.Ordinal)
    {
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"
    };

    public static IEndpointRouteBuilder MapCelarAiRuntimeVersionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(StatusRoute,
            (Func<HttpContext, CancellationToken, Task<IResult>>)GetStatusAsync);
        endpoints.MapPut(ScheduleRoute,
            (Func<CelarAiRuntimeMaintenanceScheduleRequest, HttpContext, CancellationToken, Task<IResult>>)UpdateScheduleAsync);
        return endpoints;
    }

    private static async Task<IResult> GetStatusAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var authorization = await AdminExperienceCommon.AuthorizeAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;
        var access = authorization.Context!;

        var snapshot = PulseAiExternalHttpsRuntimePolicy.Evaluate();
        if (!snapshot.Active)
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "oracle_runtime_not_active",
                message = "The protected Test Oracle Celar runtime is not currently authorized and active.",
                scheduleMutationConfigured = MaintenanceControlConfigured(),
                productionMutationAllowed = false,
                stateChanged = false
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var runtimeToken = Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN")?.Trim() ?? string.Empty;
        if (runtimeToken.Length < 32)
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "oracle_runtime_status_not_configured",
                message = "The protected Celar runtime status credential is not configured.",
                scheduleMutationConfigured = MaintenanceControlConfigured(),
                stateChanged = false
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            var oracle = await SendOracleAsync(
                snapshot,
                HttpMethod.Get,
                MaintenanceStatusPath,
                runtimeToken,
                null,
                false,
                null,
                cancellationToken);
            if (!oracle.Response.IsSuccessStatusCode)
            {
                return OracleUnavailable("oracle_runtime_status_unavailable", oracle.Response.StatusCode);
            }

            using var document = JsonDocument.Parse(oracle.Body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("module", out var module)
                || module.GetString() != ModuleNumber
                || !root.TryGetProperty("security", out var security)
                || !security.TryGetProperty("secretValuesReturned", out var secretsReturned)
                || secretsReturned.ValueKind != JsonValueKind.False)
            {
                return OracleUnavailable("oracle_runtime_status_contract_invalid", HttpStatusCode.BadGateway);
            }

            return Results.Ok(new
            {
                module = ModuleNumber,
                status = "celar_runtime_version_status_loaded",
                runtime = root.Clone(),
                control = new
                {
                    scheduleMutationConfigured = MaintenanceControlConfigured(),
                    scheduleMutationRequiresActualAdministrator = true,
                    viewAsMutationAllowed = false,
                    productionMutationAllowed = false,
                    providerOrderOwnedByModule064 = true,
                    defaultTimeZone = "America/Chicago"
                },
                access = new
                {
                    actualUserId = access.UserId,
                    isViewAs = AdminExperienceCommon.IsViewAs(context),
                    mutationAuthorityTransferred = false
                },
                stateChanged = false
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or SocketException)
        {
            Console.Error.WriteLine(
                $"Celar Module 084 status failed traceId={context.TraceIdentifier} exception={exception.GetType().Name}");
            return OracleUnavailable("oracle_runtime_status_unavailable", HttpStatusCode.ServiceUnavailable);
        }
    }

    private static async Task<IResult> UpdateScheduleAsync(
        CelarAiRuntimeMaintenanceScheduleRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var authorization = await AdminExperienceCommon.AuthorizeAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;
        var access = authorization.Context!;

        if (AdminExperienceCommon.IsViewAs(context))
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "view_as_read_only",
                message = "Return to the actual administrator session before changing the Celar maintenance window.",
                mutationAuthorityTransferred = false,
                stateChanged = false
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var release = ProjectPulseAiReleaseRuntimePolicy.Snapshot();
        if (release.IsCandidate)
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "release_candidate_read_only",
                message = "Celar runtime schedule changes are disabled on the exact-source release candidate.",
                stateChanged = false
            }, statusCode: StatusCodes.Status423Locked);
        }

        var validation = ValidateSchedule(request);
        if (validation.Error is not null) return validation.Error;
        var requested = validation.Schedule!;

        var maintenanceToken = Environment.GetEnvironmentVariable(MaintenanceTokenVariable)?.Trim() ?? string.Empty;
        var secretReference = Environment.GetEnvironmentVariable(MaintenanceTokenReferenceVariable)?.Trim() ?? string.Empty;
        if (maintenanceToken.Length < 32 || string.IsNullOrWhiteSpace(secretReference))
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "maintenance_control_not_configured",
                message = "The dedicated protected Test maintenance credential has not been activated.",
                stateChanged = false
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var snapshot = PulseAiExternalHttpsRuntimePolicy.Evaluate();
        if (!snapshot.Active)
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "oracle_runtime_not_active",
                message = "Schedule changes are allowed only against the authorized protected Test Oracle runtime.",
                productionMutationAllowed = false,
                stateChanged = false
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        JsonElement? previous = null;
        var runtimeToken = Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN")?.Trim() ?? string.Empty;
        if (runtimeToken.Length >= 32)
        {
            try
            {
                var current = await SendOracleAsync(
                    snapshot,
                    HttpMethod.Get,
                    MaintenanceStatusPath,
                    runtimeToken,
                    null,
                    false,
                    null,
                    cancellationToken);
                if (current.Response.IsSuccessStatusCode)
                {
                    using var currentDocument = JsonDocument.Parse(current.Body);
                    if (currentDocument.RootElement.TryGetProperty("maintenance", out var maintenance)
                        && maintenance.TryGetProperty("desired", out var desired))
                    {
                        previous = desired.Clone();
                    }
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or SocketException)
            {
                Console.Error.WriteLine(
                    $"Celar Module 084 pre-change status unavailable traceId={context.TraceIdentifier} exception={exception.GetType().Name}");
            }
        }

        var requestId = $"pulse-{Guid.NewGuid():N}";
        var body = JsonSerializer.Serialize(new
        {
            enabled = requested.Enabled,
            dayOfWeek = requested.DayOfWeek,
            localTime = requested.LocalTime,
            timeZone = requested.TimeZone,
            requestId
        });

        try
        {
            var oracle = await SendOracleAsync(
                snapshot,
                HttpMethod.Put,
                MaintenanceSchedulePath,
                maintenanceToken,
                body,
                true,
                requestId,
                cancellationToken);
            if (oracle.Response.StatusCode != HttpStatusCode.Accepted)
            {
                return Results.Json(new
                {
                    module = ModuleNumber,
                    status = "maintenance_schedule_rejected",
                    message = "The Oracle maintenance control plane rejected the requested schedule.",
                    oracleStatusCode = (int)oracle.Response.StatusCode,
                    stateChanged = false
                }, statusCode: StatusCodes.Status502BadGateway);
            }

            await WriteAuditAsync(
                access,
                context,
                requestId,
                previous,
                requested,
                cancellationToken);

            return Results.Json(new
            {
                module = ModuleNumber,
                status = "maintenance_schedule_accepted",
                message = "The schedule was accepted and will be reconciled on Oracle within approximately one minute.",
                schedule = requested,
                requestId,
                timeZoneBehavior = "Central local time follows CST/CDT through America/Chicago.",
                providerOrderChanged = false,
                stateChanged = true
            }, statusCode: StatusCodes.Status202Accepted);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or SocketException)
        {
            Console.Error.WriteLine(
                $"Celar Module 084 schedule update failed traceId={context.TraceIdentifier} exception={exception.GetType().Name}");
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "maintenance_schedule_unavailable",
                message = "The protected Test maintenance control plane is temporarily unavailable.",
                stateChanged = false
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static (MaintenanceSchedule? Schedule, IResult? Error) ValidateSchedule(
        CelarAiRuntimeMaintenanceScheduleRequest request)
    {
        if (request.Enabled is null)
            return (null, ValidationError("enabled", "Choose whether automatic maintenance is enabled."));
        var day = request.DayOfWeek?.Trim() ?? string.Empty;
        if (!AllowedDays.Contains(day))
            return (null, ValidationError("dayOfWeek", "Choose one day of the week."));
        var localTime = request.LocalTime?.Trim() ?? string.Empty;
        if (localTime.Length != 5
            || localTime[2] != ':'
            || !int.TryParse(localTime[..2], out var hour)
            || !int.TryParse(localTime[3..], out var minute)
            || hour is < 0 or > 23
            || minute is < 0 or > 59)
        {
            return (null, ValidationError("localTime", "Use a 24-hour local time such as 01:00."));
        }
        var timeZone = request.TimeZone?.Trim() ?? string.Empty;
        if (!string.Equals(timeZone, "America/Chicago", StringComparison.Ordinal))
            return (null, ValidationError("timeZone", "The approved Central time zone is America/Chicago."));
        return (new MaintenanceSchedule(request.Enabled.Value, day, localTime, timeZone), null);
    }

    private static IResult ValidationError(string field, string message) =>
        Results.Json(new
        {
            module = ModuleNumber,
            status = "validation_failed",
            field,
            message,
            stateChanged = false
        }, statusCode: StatusCodes.Status400BadRequest);

    private static bool MaintenanceControlConfigured()
    {
        var token = Environment.GetEnvironmentVariable(MaintenanceTokenVariable)?.Trim() ?? string.Empty;
        var reference = Environment.GetEnvironmentVariable(MaintenanceTokenReferenceVariable)?.Trim() ?? string.Empty;
        return token.Length >= 32 && !string.IsNullOrWhiteSpace(reference);
    }

    private static async Task<(HttpResponseMessage Response, string Body)> SendOracleAsync(
        PulseAiExternalHttpsRuntimePolicy.Snapshot snapshot,
        HttpMethod method,
        string path,
        string bearerToken,
        string? body,
        bool maintenanceMutation,
        string? requestId,
        CancellationToken cancellationToken)
    {
        if (!snapshot.Active
            || !string.Equals(snapshot.Host, PulseAiExternalHttpsRuntimePolicy.ApprovedHost, StringComparison.Ordinal)
            || !path.StartsWith("/v1/maintenance/", StringComparison.Ordinal))
        {
            throw new HttpRequestException("The Oracle maintenance endpoint is not authorized.");
        }

        var addresses = (await Dns.GetHostAddressesAsync(snapshot.Host, cancellationToken))
            .Select(address => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address)
            .Distinct()
            .ToArray();
        if (!PulseAiExternalHttpsRuntimePolicy.AddressesApproved(snapshot, addresses))
            throw new HttpRequestException("The Oracle maintenance hostname did not resolve only to approved addresses.");

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromSeconds(30)
        };
        handler.ConnectCallback = async (connectContext, token) =>
        {
            Exception? last = null;
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, connectContext.DnsEndPoint.Port), token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception exception) when (exception is SocketException or IOException)
                {
                    last = exception;
                    socket.Dispose();
                }
            }
            throw new HttpRequestException("Unable to connect to the approved Oracle address set.", last);
        };

        using var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        using var message = new HttpRequestMessage(method, new Uri($"https://{snapshot.Host}{path}"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        message.Headers.TryAddWithoutValidation("X-Pulse-AI-Privacy-Boundary", PrivacyBoundary);
        if (maintenanceMutation)
        {
            message.Headers.TryAddWithoutValidation("X-Celar-Maintenance-Intent", "schedule_update");
            if (!string.IsNullOrWhiteSpace(requestId))
                message.Headers.TryAddWithoutValidation("X-Celar-Maintenance-Request-Id", requestId);
        }
        if (body is not null)
            message.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseBody = await ReadBoundedBodyAsync(response.Content, cancellationToken);
        return (response, responseBody);
    }

    private static async Task<string> ReadBoundedBodyAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumOracleResponseBytes)
            throw new HttpRequestException("The Oracle maintenance response exceeded the governed size limit.");
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaximumOracleResponseBytes)
                throw new HttpRequestException("The Oracle maintenance response exceeded the governed size limit.");
            output.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static async Task WriteAuditAsync(
        AdminExperienceCommon.AccessContext access,
        HttpContext context,
        string requestId,
        JsonElement? previous,
        MaintenanceSchedule requested,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(access.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            var detailed = await AdminExperienceCommon.WriteAuditAsync(
                connection,
                null,
                category: "platform_operations",
                status: "accepted",
                eventType: "celar_runtime_maintenance_schedule_changed",
                actorUserId: access.UserId,
                actorEmail: access.Email,
                targetType: "celar_ai_runtime",
                targetId: "oracle-protected-test",
                targetLabel: "Celar AI Runtime & Version Center",
                sourceModule: ModuleNumber,
                sourceTable: "oracle_maintenance_desired_state",
                sourceRecordId: requestId,
                summary: "Administrator changed the protected Test Celar automatic maintenance schedule.",
                details: new
                {
                    previousSchedule = previous,
                    requestedSchedule = requested,
                    providerOrderChanged = false,
                    productionChanged = false,
                    secretValuesRecorded = false
                },
                ipAddress: AdminExperienceCommon.ClientIp(context),
                correlationId: context.TraceIdentifier,
                cancellationToken: cancellationToken);

            if (!detailed)
            {
                await using var command = new NpgsqlCommand("""
                    INSERT INTO audit_logs (
                        actor_user_id, action, entity_type, entity_id,
                        old_value, new_value, ip_address, user_agent)
                    VALUES (
                        @actor_user_id, @action, @entity_type, NULL,
                        @old_value::jsonb, @new_value::jsonb,
                        NULLIF(@ip_address, '')::inet, @user_agent);
                    """, connection);
                command.Parameters.AddWithValue("actor_user_id", access.UserId);
                command.Parameters.AddWithValue("action", "CELAR_RUNTIME_MAINTENANCE_SCHEDULE_CHANGED");
                command.Parameters.AddWithValue("entity_type", "celar_ai_runtime");
                command.Parameters.AddWithValue("old_value", previous.HasValue ? previous.Value.GetRawText() : "{}");
                command.Parameters.AddWithValue("new_value", JsonSerializer.Serialize(requested));
                command.Parameters.AddWithValue("ip_address", AdminExperienceCommon.ClientIp(context));
                command.Parameters.AddWithValue("user_agent", context.Request.Headers.UserAgent.ToString());
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or JsonException)
        {
            // The maintenance request has already been accepted by Oracle. Do not
            // reveal database details to the browser; emit a correlation-safe
            // operational signal so the missing audit evidence can be repaired.
            Console.Error.WriteLine(
                $"Celar Module 084 audit write failed traceId={context.TraceIdentifier} exception={exception.GetType().Name}");
        }
    }

    private static IResult OracleUnavailable(string status, HttpStatusCode oracleStatus) =>
        Results.Json(new
        {
            module = ModuleNumber,
            status,
            message = "Celar runtime version information is temporarily unavailable.",
            oracleStatusCode = (int)oracleStatus,
            stateChanged = false
        }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private sealed record MaintenanceSchedule(
        bool Enabled,
        string DayOfWeek,
        string LocalTime,
        string TimeZone);
}
