using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Shared provider-neutral runtime and evidence contract used by Modules 013,
/// 016, and 068. Provider-specific values are contained by an adapter and never
/// become required fields in the consuming module experiences.
/// </summary>
public static partial class PlatformOperationsModule
{
    private const string ContractVersion = "2026-07-27.1";
    private const int MaximumEvidenceEvents = 2000;
    private static readonly DateTimeOffset ProcessStartedAt =
        DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
    private static readonly ConcurrentQueue<OperationalEvidence> Evidence = new();
    private static readonly ConcurrentDictionary<string, ApiObservation> ApiObservations =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex GuidSegment =
        new(@"^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NumericSegment =
        new(@"^\d{3,}$", RegexOptions.Compiled);

    public static IApplicationBuilder UsePlatformOperationsTelemetry(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments(
                    "/api",
                    StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            var correlationId = CleanCorrelationId(context.TraceIdentifier);
            if (!context.Response.Headers.ContainsKey("X-ProjectPulse-Correlation-Id"))
            {
                context.Response.Headers["X-ProjectPulse-Correlation-Id"] = correlationId;
            }

            Exception? failure = null;
            try
            {
                await next();
            }
            catch (Exception exception)
            {
                failure = exception;
                throw;
            }
            finally
            {
                stopwatch.Stop();
                var endpoint = context.GetEndpoint() as RouteEndpoint;
                var route = endpoint?.RoutePattern.RawText
                    ?? SanitizeRequestPath(context.Request.Path.Value);
                var method = context.Request.Method.ToUpperInvariant();
                var statusCode = failure is null
                    ? context.Response.StatusCode
                    : StatusCodes.Status500InternalServerError;
                var module = InferModule(route);
                var status = statusCode >= 500
                    ? "failed"
                    : statusCode >= 400
                        ? "rejected"
                        : "succeeded";
                var errorCode = failure?.GetType().Name
                    ?? (statusCode >= 400 ? $"HTTP_{statusCode}" : string.Empty);

                RecordObservation(new OperationalEvidence(
                    Guid.NewGuid().ToString("N"),
                    DateTimeOffset.UtcNow,
                    correlationId,
                    module.Code,
                    module.Name,
                    "api_request",
                    status,
                    method,
                    route,
                    statusCode,
                    Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                    errorCode,
                    statusCode >= 500
                        ? "The request failed. Review dependencies and correlation evidence."
                        : statusCode >= 400
                            ? "The request was rejected by authentication, authorization, validation, or availability controls."
                            : "The API request completed.",
                    ReleaseSha(),
                    false));

                var key = ApiKey(method, route);
                ApiObservations.AddOrUpdate(
                    key,
                    _ => ApiObservation.From(
                        statusCode,
                        stopwatch.Elapsed.TotalMilliseconds,
                        correlationId,
                        errorCode),
                    (_, current) => current.Update(
                        statusCode,
                        stopwatch.Elapsed.TotalMilliseconds,
                        correlationId,
                        errorCode));
            }
        });
    }

    private static async Task<AuthorizationOutcome> AuthorizeAsync(
        HttpContext context,
        bool requireOwnSession = false)
    {
        var actualUserId = ActualSessionUserId(context);
        if (actualUserId is null)
        {
            return new AuthorizationOutcome(
                null,
                Results.Json(new
                {
                    status = "session_required",
                    message = "A valid ProjectPulse session is required."
                }, statusCode: StatusCodes.Status401Unauthorized));
        }

        if (requireOwnSession && IsViewAs(context))
        {
            return new AuthorizationOutcome(
                null,
                Results.Json(new
                {
                    status = "view_as_read_only",
                    message = "Exit Administrator View-As before running an API retest."
                }, statusCode: StatusCodes.Status403Forbidden));
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new AuthorizationOutcome(
                null,
                Results.Json(new
                {
                    status = "authorization_dependency_unavailable",
                    message = "Platform Operations authorization is temporarily unavailable."
                }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(context.RequestAborted);
            await using var command = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1
                    FROM app_user_role_assignments ura
                    JOIN app_roles r
                      ON r.app_role_id = ura.app_role_id
                     AND r.is_active = TRUE
                    LEFT JOIN app_role_permissions rp
                      ON rp.app_role_id = r.app_role_id
                    LEFT JOIN app_permissions p
                      ON p.app_permission_id = rp.app_permission_id
                    WHERE ura.user_id = @user_id
                      AND ura.is_active = TRUE
                      AND (
                          upper(COALESCE(r.role_code, '')) IN (
                              'SUPER_ADMINISTRATOR',
                              'ADMINISTRATOR'
                          )
                          OR upper(COALESCE(p.permission_code, '')) IN (
                              'SYSTEM_ADMINISTRATION',
                              'MANAGE_ALL'
                          )
                      )
                );
                """, connection);
            command.Parameters.AddWithValue("user_id", actualUserId.Value);
            var allowed = Convert.ToBoolean(
                await command.ExecuteScalarAsync(context.RequestAborted));

            if (!allowed)
            {
                await connection.DisposeAsync();
                return new AuthorizationOutcome(
                    null,
                    Results.Json(new
                    {
                        status = "administrator_access_required",
                        message = "Platform Operations is restricted to authorized administrators."
                    }, statusCode: StatusCodes.Status403Forbidden));
            }

            return new AuthorizationOutcome(connection, null);
        }
        catch (Exception exception)
        {
            await connection.DisposeAsync();
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("PlatformOperationsModule")
                .LogWarning(
                    "Platform Operations authorization dependency unavailable ({ExceptionType}).",
                    exception.GetType().Name);

            return new AuthorizationOutcome(
                null,
                Results.Json(new
                {
                    status = "authorization_dependency_unavailable",
                    message = "Platform Operations authorization is temporarily unavailable."
                }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }

    private static Guid? ActualSessionUserId(HttpContext context)
    {
        foreach (var key in new[]
                 {
                     "ProjectPulseActualUserId",
                     "ProjectPulseSessionUserId"
                 })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid userId) return userId;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }

        return null;
    }

    private static bool IsViewAs(HttpContext context) =>
        context.Items.TryGetValue("ProjectPulseIsViewAs", out var value)
        && value is bool isViewAs
        && isViewAs;

    private static bool SameOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin)) return true;

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && string.Equals(
                uri.Host,
                context.Request.Host.Host,
                StringComparison.OrdinalIgnoreCase);
    }

    private static object AccessContract(HttpContext context) => new
    {
        classification = "administrators_only",
        serverAuthorized = true,
        authoritySource = "actual_projectpulse_session",
        viewAsTransfersMutationAuthority = false,
        isViewAs = IsViewAs(context)
    };

    private static object SecurityContract() => new
    {
        secretValuesReturned = false,
        rawExceptionMessagesReturned = false,
        requestBodiesCaptured = false,
        queryStringsCaptured = false,
        providerCredentialsCaptured = false,
        restartExecutionEnabled = false,
        productionChangingActionsEnabled = false
    };

    private static IPlatformAdapter DetectAdapter()
    {
        var requested = FirstEnvironment(
                "PROJECTPULSE_PLATFORM_PROVIDER",
                "PROJECTPULSE_HOSTING_PROVIDER")
            .Trim()
            .ToLowerInvariant();

        if (requested.Contains("opencloud", StringComparison.OrdinalIgnoreCase))
        {
            return new GenericAdapter(
                "opencloud",
                "OpenCloud",
                "opencloud_adapter",
                "configured_contract",
                Region(),
                WorkloadKind(),
                InstanceName(),
                DeploymentName(),
                true,
                false,
                false,
                ProviderDetails("opencloud"));
        }

        var azureSignal = requested is "azure" or "microsoft_azure"
            || !string.IsNullOrWhiteSpace(FirstEnvironment(
                "CONTAINER_APP_NAME",
                "CONTAINER_APP_REVISION",
                "WEBSITE_SITE_NAME",
                "WEBSITE_INSTANCE_ID",
                "IDENTITY_ENDPOINT"));

        if (azureSignal)
        {
            return new GenericAdapter(
                "azure",
                "Microsoft Azure",
                "azure_adapter",
                "active",
                Region(),
                WorkloadKind(),
                InstanceName(),
                DeploymentName(),
                true,
                true,
                true,
                ProviderDetails("azure"));
        }

        if (!string.IsNullOrWhiteSpace(requested)
            && requested is not "generic"
            && requested is not "local")
        {
            return new GenericAdapter(
                SafeId(requested),
                TitleCase(requested),
                "generic_cloud_adapter",
                "configured_contract",
                Region(),
                WorkloadKind(),
                InstanceName(),
                DeploymentName(),
                true,
                false,
                false,
                ProviderDetails(requested));
        }

        var container = File.Exists("/.dockerenv")
            || !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST"));

        return new GenericAdapter(
            container ? "container" : "local",
            container ? "Generic container platform" : "Local or server runtime",
            container ? "generic_container_adapter" : "local_runtime_adapter",
            "active",
            Region(),
            container ? "container" : "process",
            InstanceName(),
            DeploymentName(),
            false,
            false,
            false,
            ProviderDetails(container ? "container" : "local"));
    }

    private static string Region()
    {
        var value = FirstEnvironment(
            "PROJECTPULSE_PLATFORM_REGION",
            "REGION_NAME",
            "LOCATION",
            "AZURE_REGION",
            "WEBSITE_LOCATION",
            "CLOUD_REGION");
        return string.IsNullOrWhiteSpace(value) ? "not_reported" : Limit(value, 120);
    }

    private static string WorkloadKind()
    {
        if (!string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("CONTAINER_APP_NAME")))
            return "container_application";
        if (!string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME")))
            return "application_service";
        if (!string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST")))
            return "orchestrated_container";
        if (File.Exists("/.dockerenv")) return "container";
        return "process";
    }

    private static string InstanceName()
    {
        var value = FirstEnvironment(
            "PROJECTPULSE_PLATFORM_INSTANCE",
            "CONTAINER_APP_REPLICA_NAME",
            "WEBSITE_INSTANCE_ID",
            "HOSTNAME");
        return string.IsNullOrWhiteSpace(value) ? "not_reported" : Limit(value, 120);
    }

    private static string DeploymentName()
    {
        var value = FirstEnvironment(
            "PROJECTPULSE_DEPLOYMENT_NAME",
            "CONTAINER_APP_REVISION",
            "WEBSITE_DEPLOYMENT_ID",
            "SOURCE_VERSION");
        return string.IsNullOrWhiteSpace(value) ? ReleaseSha() : Limit(value, 160);
    }

    private static Dictionary<string, string> ProviderDetails(string provider)
    {
        var details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["provider"] = provider,
            ["adapterContract"] = ContractVersion,
            ["workloadKind"] = WorkloadKind()
        };

        AddIfPresent(
            details,
            "resourceGroup",
            FirstEnvironment("AZURE_RESOURCE_GROUP", "WEBSITE_RESOURCE_GROUP"));
        AddIfPresent(
            details,
            "containerApplication",
            FirstEnvironment("CONTAINER_APP_NAME"));
        AddIfPresent(
            details,
            "revision",
            FirstEnvironment("CONTAINER_APP_REVISION"));
        AddIfPresent(
            details,
            "applicationService",
            FirstEnvironment("WEBSITE_SITE_NAME"));
        AddIfPresent(
            details,
            "orchestratorNamespace",
            FirstEnvironment("POD_NAMESPACE"));

        return details;
    }

    private static MemorySnapshot ReadMemory()
    {
        long? total = null;
        long? available = null;

        try
        {
            if (File.Exists("/proc/meminfo"))
            {
                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    if (line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase))
                        total = ParseKilobytes(line);
                    if (line.StartsWith("MemAvailable:", StringComparison.OrdinalIgnoreCase))
                        available = ParseKilobytes(line);
                }
            }
        }
        catch
        {
            // Nullable metrics are intentional when the provider blocks host memory.
        }

        var containerCurrent = ReadLongFile("/sys/fs/cgroup/memory.current");
        var containerLimit = ReadLongFile("/sys/fs/cgroup/memory.max");
        if (containerLimit is > 0 and < long.MaxValue / 2)
        {
            total = containerLimit;
            available = containerCurrent.HasValue
                ? Math.Max(0, containerLimit.Value - containerCurrent.Value)
                : available;
        }

        return new MemorySnapshot(
            total,
            available,
            containerCurrent,
            containerLimit);
    }

    private static DriveSnapshot[] ReadDrives()
    {
        var rows = new List<DriveSnapshot>();
        try
        {
            var index = 0;
            foreach (var drive in DriveInfo.GetDrives().Where(item => item.IsReady))
            {
                index += 1;
                rows.Add(new DriveSnapshot(
                    $"volume-{index}",
                    drive.DriveType.ToString().ToLowerInvariant(),
                    drive.DriveFormat,
                    drive.TotalSize,
                    drive.TotalFreeSpace,
                    Math.Max(0, drive.TotalSize - drive.TotalFreeSpace)));
            }
        }
        catch
        {
            // Disk inventory remains empty when the provider blocks metrics.
        }

        return rows.ToArray();
    }

    private static long? ParseKilobytes(string line)
    {
        var value = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .FirstOrDefault();
        return long.TryParse(value, out var parsed) ? parsed * 1024 : null;
    }

    private static long? ReadLongFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var value = File.ReadAllText(path).Trim();
            return long.TryParse(value, out var parsed) ? parsed : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? BuildConnectionString()
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

        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");

        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(
                Environment.GetEnvironmentVariable("PTP_DB_PORT"),
                out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = 5,
            Timeout = 4,
            CommandTimeout = 5
        }.ConnectionString;
    }

    private static string ReleaseSha()
    {
        var value = FirstEnvironment(
            "PROJECTPULSE_RELEASE_SHA",
            "SOURCE_COMMIT",
            "SOURCE_VERSION",
            "GITHUB_SHA").Trim();

        return value.Length is >= 7 and <= 64 && value.All(Uri.IsHexDigit)
            ? value.ToLowerInvariant()
            : "not_recorded";
    }

    private static DateTimeOffset? LastDeploymentAt()
    {
        foreach (var name in new[]
                 {
                     "PROJECTPULSE_DEPLOYED_AT",
                     "DEPLOYMENT_TIMESTAMP",
                     "RELEASE_DEPLOYED_AT"
                 })
        {
            if (DateTimeOffset.TryParse(
                    Environment.GetEnvironmentVariable(name),
                    out var parsed))
                return parsed;
        }

        return null;
    }

    private static string RuntimeEnvironment()
    {
        var value = FirstEnvironment(
                "PROJECTPULSE_ENVIRONMENT",
                "ASPNETCORE_ENVIRONMENT",
                "DOTNET_ENVIRONMENT")
            .Trim()
            .ToLowerInvariant();

        if (value.Contains("prod", StringComparison.Ordinal)) return "production";
        if (value.Contains("test", StringComparison.Ordinal)
            || value.Contains("qa", StringComparison.Ordinal)
            || value.Contains("uat", StringComparison.Ordinal)) return "test";
        if (value.Contains("dev", StringComparison.Ordinal)) return "development";
        if (value.Contains("local", StringComparison.Ordinal)) return "local";
        return "runtime_managed";
    }

    private static string RuntimeInformation() =>
        $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} · "
        + System.Runtime.InteropServices.RuntimeInformation.OSDescription;

    private static bool MicrosoftConfigured() =>
        !string.IsNullOrWhiteSpace(FirstEnvironment(
            "PROJECTPULSE_SSO_TENANT_ID",
            "PROJECTPULSE_ENTRA_TENANT_ID",
            "PROJECTPULSE_M365_TENANT_ID"))
        && !string.IsNullOrWhiteSpace(FirstEnvironment(
            "PROJECTPULSE_SSO_CLIENT_ID",
            "PROJECTPULSE_ENTRA_CLIENT_ID",
            "PROJECTPULSE_M365_CLIENT_ID"));

    private static bool MailConfigured() =>
        !string.IsNullOrWhiteSpace(FirstEnvironment(
            "PROJECTPULSE_MAIL_PROVIDER",
            "PROJECTPULSE_GLOBAL_MAIL_PROVIDER",
            "PROJECTPULSE_SMTP_HOST",
            "PROJECTPULSE_MAIL_SENDER"));

    private static void RecordObservation(OperationalEvidence evidence)
    {
        Evidence.Enqueue(evidence);
        while (Evidence.Count > MaximumEvidenceEvents && Evidence.TryDequeue(out _))
        {
        }
    }

    private static string EvidenceSearchText(OperationalEvidence item) =>
        $"{item.ModuleCode} {item.ModuleName} {item.EventType} {item.Status} "
        + $"{item.Method} {item.Path} {item.ErrorCode} {item.Message} {item.CorrelationId}";

    private static string SanitizeRequestPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "/";

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => GuidSegment.IsMatch(segment)
                || NumericSegment.IsMatch(segment)
                    ? "{id}"
                    : Limit(segment, 80));

        return "/" + string.Join('/', segments);
    }

    private static string CleanCorrelationId(string? value)
    {
        var clean = Regex.Replace(
            value ?? string.Empty,
            @"[^A-Za-z0-9._:-]",
            string.Empty);
        return string.IsNullOrWhiteSpace(clean)
            ? Guid.NewGuid().ToString("N")
            : Limit(clean, 120);
    }

    private static string CleanSearch(string? value)
    {
        var clean = Regex.Replace(
            value ?? string.Empty,
            @"[\u0000-\u001f]",
            " ").Trim();
        return Limit(clean, 160);
    }

    private static string ApiKey(string method, string path) =>
        $"{method.ToUpperInvariant()} {path.Trim()}";

    private static string ApiId(string method, string path)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(ApiKey(method, path)));
        return Convert.ToHexString(bytes)[..20].ToLowerInvariant();
    }

    private static string SafeId(string value) =>
        Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');

    private static string FriendlyTypeName(string value) =>
        Regex.Replace(
            value.Replace("HostedService", " Hosted Service"),
            @"(?<=[a-z])(?=[A-Z])",
            " ");

    private static string Limit(string? value, int length) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Length <= length
                ? value
                : value[..length];

    private static string TitleCase(string value) =>
        string.Join(
            ' ',
            value.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0])
                    + part[1..].ToLowerInvariant()));

    private static string FirstEnvironment(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }

        return string.Empty;
    }

    private static void AddIfPresent(
        Dictionary<string, string> target,
        string key,
        string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) target[key] = Limit(value, 160);
    }

    private sealed record AuthorizationOutcome(
        NpgsqlConnection? Connection,
        IResult? Failure);

    private sealed record ModuleOwner(string Code, string Name);

    private sealed record MemorySnapshot(
        long? TotalBytes,
        long? AvailableBytes,
        long? ContainerCurrentBytes,
        long? ContainerLimitBytes);

    private interface IPlatformAdapter
    {
        string Provider { get; }
        string DisplayName { get; }
        string Adapter { get; }
        string AdapterStatus { get; }
        string Region { get; }
        string WorkloadKind { get; }
        string Instance { get; }
        string Deployment { get; }
        bool IsCloud { get; }
        bool SupportsServiceRestart { get; }
        bool SupportsIsolatedWorkerRestart { get; }
        DeploymentEntry[] Deployments { get; }
        ReplicaEntry[] Replicas { get; }
        Dictionary<string, string> ProviderSpecificDetails { get; }
    }

    private sealed record GenericAdapter(
        string Provider,
        string DisplayName,
        string Adapter,
        string AdapterStatus,
        string Region,
        string WorkloadKind,
        string Instance,
        string Deployment,
        bool IsCloud,
        bool SupportsServiceRestart,
        bool SupportsIsolatedWorkerRestart,
        Dictionary<string, string> ProviderSpecificDetails) : IPlatformAdapter
    {
        public DeploymentEntry[] Deployments =>
        [
            new(
                Deployment,
                ReleaseSha(),
                LastDeploymentAt(),
                "observed_from_runtime_metadata")
        ];

        public ReplicaEntry[] Replicas =>
        [
            new(
                Instance,
                Region,
                "observed",
                "Runtime instance reported by the active adapter.")
        ];
    }

    private sealed record PlatformSnapshot(
        PlatformIdentity Platform,
        RuntimeIdentity Runtime,
        ResourceSnapshot Resources,
        DependencySnapshot Dependencies,
        IntegrationStatus[] Integrations,
        WorkerStatus[] Workers,
        DeploymentEntry[] Deployments,
        ReplicaEntry[] Replicas,
        PlatformCapability[] Capabilities,
        Dictionary<string, string> ProviderSpecificDetails);

    private sealed record PlatformIdentity(
        string Provider,
        string DisplayName,
        string Adapter,
        string AdapterStatus,
        string Environment,
        string Region,
        string WorkloadKind,
        string Instance,
        bool IsCloud);

    private sealed record RuntimeIdentity(
        string ApplicationVersion,
        string ReleaseSha,
        DateTimeOffset ProcessStartedAt,
        double UptimeSeconds,
        string Deployment,
        DateTimeOffset? LastDeploymentAt,
        int LogicalProcessorCount,
        string Runtime);

    private sealed record ResourceSnapshot(
        double CpuPercent,
        long ProcessWorkingSetBytes,
        long ProcessPrivateMemoryBytes,
        long ManagedHeapBytes,
        long? ContainerMemoryCurrentBytes,
        long? ContainerMemoryLimitBytes,
        long? TotalMemoryBytes,
        long? AvailableMemoryBytes,
        DriveSnapshot[] Drives);

    private sealed record DriveSnapshot(
        string Volume,
        string Type,
        string FileSystem,
        long TotalBytes,
        long AvailableBytes,
        long UsedBytes);

    private sealed record DependencySnapshot(
        DependencyCheck Database,
        DependencyCheck Storage);

    private sealed record DependencyCheck(
        string Key,
        string Name,
        string Status,
        double? LatencyMs,
        DateTimeOffset CheckedAt,
        string Message,
        string ErrorCode);

    private sealed record IntegrationStatus(
        string Key,
        string Name,
        string Type,
        string Status,
        DateTimeOffset? LastCheckedAt,
        string Owner,
        string[] Capabilities,
        bool SecretValueReturned);

    private sealed record WorkerStatus(
        string Key,
        string Name,
        string Status,
        string Source,
        string RestartMessage);

    private sealed record DeploymentEntry(
        string Name,
        string ReleaseSha,
        DateTimeOffset? DeployedAt,
        string EvidenceSource);

    private sealed record ReplicaEntry(
        string Name,
        string Region,
        string Status,
        string Evidence);

    private sealed record PlatformCapability(
        string Key,
        string Name,
        string State,
        string Message);

    private sealed record ApiInventoryItem(
        string ApiId,
        string RouteGroup,
        string Method,
        string Path,
        string ModuleCode,
        string ModuleName,
        string Purpose,
        string AuthenticationRequirement,
        string PermissionRequirement,
        string[] Dependencies,
        string CurrentStatus,
        DateTimeOffset? LastCheckedAt,
        DateTimeOffset? LastSuccessfulRequestAt,
        DateTimeOffset? LastFailureAt,
        double? ResponseTimeMs,
        string LastErrorCode,
        string CorrelationId,
        string RetestCapability,
        string RetestReason,
        string IntroducedRelease,
        string CurrentRelease);

    private sealed record OperationalEvidence(
        string EvidenceId,
        DateTimeOffset ObservedAt,
        string CorrelationId,
        string ModuleCode,
        string ModuleName,
        string EventType,
        string Status,
        string Method,
        string Path,
        int StatusCode,
        double ResponseTimeMs,
        string ErrorCode,
        string Message,
        string ReleaseSha,
        bool SecretValueIncluded);

    private sealed record ApiObservation(
        DateTimeOffset LastCheckedAt,
        DateTimeOffset? LastSuccessAt,
        DateTimeOffset? LastFailureAt,
        int LastStatusCode,
        double LastLatencyMs,
        string LastCorrelationId,
        string LastErrorCode,
        long RequestCount,
        long FailureCount)
    {
        public static ApiObservation From(
            int statusCode,
            double latencyMs,
            string correlationId,
            string errorCode)
        {
            var now = DateTimeOffset.UtcNow;
            return new ApiObservation(
                now,
                statusCode < 400 ? now : null,
                statusCode >= 400 ? now : null,
                statusCode,
                Math.Round(latencyMs, 2),
                correlationId,
                errorCode,
                1,
                statusCode >= 400 ? 1 : 0);
        }

        public ApiObservation Update(
            int statusCode,
            double latencyMs,
            string correlationId,
            string errorCode)
        {
            var now = DateTimeOffset.UtcNow;
            return this with
            {
                LastCheckedAt = now,
                LastSuccessAt = statusCode < 400 ? now : LastSuccessAt,
                LastFailureAt = statusCode >= 400 ? now : LastFailureAt,
                LastStatusCode = statusCode,
                LastLatencyMs = Math.Round(latencyMs, 2),
                LastCorrelationId = correlationId,
                LastErrorCode = errorCode,
                RequestCount = RequestCount + 1,
                FailureCount = FailureCount + (statusCode >= 400 ? 1 : 0)
            };
        }
    }

    private sealed record ArchitectureContract(
        ArchitectureLayer[] Layers,
        ArchitectureNode[] Nodes,
        ArchitectureConnection[] Connections,
        TrustBoundary[] TrustBoundaries,
        LegendEntry[] Legend,
        ExternalDataFlow[] ExternalDataFlows,
        ModuleApiRelationship[] ModuleApiRelationships,
        RegionEntry[] Regions,
        RedundancyContract Redundancy);

    private sealed record ArchitectureLayer(string Id, string Name, int Order);
    private sealed record ArchitectureNode(
        string Id,
        string Name,
        string Layer,
        string Kind,
        string Description);
    private sealed record ArchitectureConnection(
        string From,
        string To,
        string Protocol,
        string Data,
        string Classification);
    private sealed record TrustBoundary(string Id, string Name, string Control);
    private sealed record LegendEntry(string Code, string Description);
    private sealed record ExternalDataFlow(
        string System,
        string ProjectPulseComponent,
        string Data,
        string Status,
        string Owner);
    private sealed record ModuleApiRelationship(
        string ModuleCode,
        string ModuleName,
        int ApiCount,
        ApiRelationship[] Apis);
    private sealed record ApiRelationship(
        string ApiId,
        string Method,
        string Path,
        string Purpose);
    private sealed record RegionEntry(
        string Region,
        string Provider,
        string Environment);
    private sealed record RedundancyContract(
        int ObservedReplicaCount,
        string Status,
        ReplicaEntry[] Replicas,
        string Message);
}
