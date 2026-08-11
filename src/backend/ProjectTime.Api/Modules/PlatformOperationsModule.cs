using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class PlatformOperationsModule
{
    public static IEndpointRouteBuilder MapPlatformOperationsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/platform-operations/overview",
            (Func<HttpContext, Task<IResult>>)GetOverviewAsync);
        endpoints.MapGet(
            "/api/platform-operations/apis",
            (Func<HttpContext, Task<IResult>>)GetApisAsync);
        endpoints.MapGet(
            "/api/platform-operations/apis/{apiId}",
            (Func<string, HttpContext, Task<IResult>>)GetApiDetailAsync);
        endpoints.MapPost(
            "/api/platform-operations/apis/{apiId}/retest",
            (Func<string, HttpContext, Task<IResult>>)RetestApiAsync);
        endpoints.MapGet(
            "/api/platform-operations/evidence",
            (Func<HttpContext, Task<IResult>>)GetEvidenceAsync);
        endpoints.MapGet(
            "/api/platform-operations/evidence/export",
            (Func<HttpContext, Task<IResult>>)ExportEvidenceAsync);
        endpoints.MapGet(
            "/api/platform-operations/architecture",
            (Func<HttpContext, Task<IResult>>)GetArchitectureAsync);
        endpoints.MapGet(
            "/api/platform-operations/architecture/export",
            (Func<HttpContext, Task<IResult>>)ExportArchitectureAsync);

        return endpoints;
    }

    private static async Task<IResult> GetOverviewAsync(HttpContext context)
    {
        var authorization = await AuthorizeAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;
        var snapshot = await BuildSnapshotAsync(context, connection);

        return Results.Ok(new
        {
            module = "013",
            status = "platform_operations_overview_loaded",
            contractVersion = ContractVersion,
            generatedAt = DateTimeOffset.UtcNow,
            access = AccessContract(context),
            platform = snapshot.Platform,
            runtime = snapshot.Runtime,
            versions = await BuildVersionInventoryAsync(connection, context.RequestAborted),
            serviceOperations = new
            {
                controlSurface = "#system-diagnostics",
                workflow = "diagnose_prepare_separate_approve_stage_execute_verify",
                directProcessRestartEnabled = false,
                viewAsReadOnly = IsViewAs(context)
            },
            resources = snapshot.Resources,
            dependencies = snapshot.Dependencies,
            integrations = snapshot.Integrations,
            workers = snapshot.Workers,
            deployments = snapshot.Deployments,
            replicas = snapshot.Replicas,
            capabilities = snapshot.Capabilities,
            providerSpecificDetails = snapshot.ProviderSpecificDetails,
            security = SecurityContract()
        });
    }

    private static async Task<IResult> GetApisAsync(HttpContext context)
    {
        var authorization = await AuthorizeAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;
        await using var connection = authorization.Connection!;

        var apis = BuildApiInventory(context);
        return Results.Ok(new
        {
            module = "013",
            status = "api_inventory_loaded",
            contractVersion = ContractVersion,
            generatedAt = DateTimeOffset.UtcNow,
            access = AccessContract(context),
            summary = new
            {
                total = apis.Count,
                healthy = apis.Count(item => item.CurrentStatus == "healthy"),
                failed = apis.Count(item => item.CurrentStatus == "failed"),
                rejected = apis.Count(item => item.CurrentStatus == "rejected"),
                notObserved = apis.Count(item => item.CurrentStatus == "not_observed"),
                safeRetestSupported = apis.Count(item => item.RetestCapability == "supported")
            },
            apis,
            security = SecurityContract()
        });
    }

    private static async Task<IResult> GetApiDetailAsync(
        string apiId,
        HttpContext context)
    {
        var authorization = await AuthorizeAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;
        await using var connection = authorization.Connection!;

        var api = BuildApiInventory(context)
            .FirstOrDefault(item => string.Equals(
                item.ApiId,
                apiId,
                StringComparison.OrdinalIgnoreCase));

        if (api is null)
        {
            return Results.NotFound(new
            {
                module = "013",
                status = "api_not_found",
                message = "That API inventory item is not registered in the running application."
            });
        }

        var recent = Evidence
            .Where(item => string.Equals(
                ApiKey(item.Method, item.Path),
                ApiKey(api.Method, api.Path),
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.ObservedAt)
            .Take(20)
            .ToArray();

        return Results.Ok(new
        {
            module = "013",
            status = "api_diagnostic_loaded",
            contractVersion = ContractVersion,
            generatedAt = DateTimeOffset.UtcNow,
            api,
            recentFailures = recent
                .Where(item => item.Status is "failed" or "rejected")
                .ToArray(),
            relatedLogs = recent,
            dependentServices = api.Dependencies,
            suggestedTroubleshooting = TroubleshootingFor(api),
            supportedActions = ApiActions(api),
            release = new
            {
                currentRelease = ReleaseSha(),
                introducedRelease = api.IntroducedRelease,
                introducedReleaseEvidence = api.IntroducedRelease == "not_recorded"
                    ? "Repository history has not yet been indexed into the runtime contract."
                    : "Runtime endpoint metadata"
            },
            security = SecurityContract()
        });
    }

    private static async Task<IResult> RetestApiAsync(
        string apiId,
        HttpContext context)
    {
        var authorization = await AuthorizeAsync(context, requireOwnSession: true);
        if (authorization.Failure is not null) return authorization.Failure;
        await using var connection = authorization.Connection!;

        if (!SameOrigin(context))
        {
            return Results.Json(new
            {
                module = "013",
                status = "origin_rejected",
                message = "API retest requires a same-origin ProjectPulse request."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var api = BuildApiInventory(context)
            .FirstOrDefault(item => string.Equals(
                item.ApiId,
                apiId,
                StringComparison.OrdinalIgnoreCase));

        if (api is null)
        {
            return Results.NotFound(new
            {
                module = "013",
                status = "api_not_found",
                message = "That API inventory item is not registered in the running application."
            });
        }

        if (api.RetestCapability != "supported")
        {
            return Results.Conflict(new
            {
                module = "013",
                status = "api_retest_not_supported",
                api.ApiId,
                api.Method,
                api.Path,
                message = api.RetestReason
            });
        }

        var stopwatch = Stopwatch.StartNew();
        var correlationId = $"retest-{Guid.NewGuid():N}";

        try
        {
            var factory = context.RequestServices
                .GetRequiredService<IHttpClientFactory>();
            using var client = factory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);

            var target = new UriBuilder(
                context.Request.Scheme,
                context.Request.Host.Host,
                context.Request.Host.Port ?? -1,
                api.Path).Uri;
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            ForwardSessionHeaders(context, request);
            request.Headers.TryAddWithoutValidation(
                "X-ProjectPulse-Diagnostic-Retest",
                correlationId);

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted);
            stopwatch.Stop();

            var resultStatus = response.IsSuccessStatusCode
                ? "healthy"
                : "failed";
            var errorCode = response.IsSuccessStatusCode
                ? string.Empty
                : $"HTTP_{(int)response.StatusCode}";

            RecordObservation(new OperationalEvidence(
                Guid.NewGuid().ToString("N"),
                DateTimeOffset.UtcNow,
                correlationId,
                api.ModuleCode,
                api.ModuleName,
                "api_retest",
                resultStatus,
                "GET",
                api.Path,
                (int)response.StatusCode,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                errorCode,
                response.IsSuccessStatusCode
                    ? "The safe read-only API retest completed."
                    : "The safe read-only API retest returned a non-success status.",
                ReleaseSha(),
                false));

            return Results.Ok(new
            {
                module = "013",
                status = "api_retest_completed",
                api.ApiId,
                api.Method,
                api.Path,
                result = resultStatus,
                statusCode = (int)response.StatusCode,
                responseTimeMs = Math.Round(
                    stopwatch.Elapsed.TotalMilliseconds,
                    2),
                correlationId,
                checkedAt = DateTimeOffset.UtcNow,
                message = response.IsSuccessStatusCode
                    ? "The API responded successfully."
                    : "The API responded, but its status requires review.",
                responseBodyRead = false,
                secretValuesReturned = false
            });
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            var errorCode = exception is TaskCanceledException
                ? "RETEST_TIMEOUT"
                : exception.GetType().Name;

            RecordObservation(new OperationalEvidence(
                Guid.NewGuid().ToString("N"),
                DateTimeOffset.UtcNow,
                correlationId,
                api.ModuleCode,
                api.ModuleName,
                "api_retest",
                "failed",
                "GET",
                api.Path,
                0,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                errorCode,
                "The API retest could not complete. Review dependencies and correlation evidence.",
                ReleaseSha(),
                false));

            return Results.Json(new
            {
                module = "013",
                status = "api_retest_failed",
                api.ApiId,
                api.Method,
                api.Path,
                responseTimeMs = Math.Round(
                    stopwatch.Elapsed.TotalMilliseconds,
                    2),
                correlationId,
                errorCode,
                message = "The safe API retest could not complete. Raw provider and exception details are not returned."
            }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> GetEvidenceAsync(HttpContext context)
    {
        var authorization = await AuthorizeAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;
        await using var connection = authorization.Connection!;

        var query = context.Request.Query;
        var search = CleanSearch(query["search"]);
        var module = CleanSearch(query["module"]).ToUpperInvariant();
        var status = CleanSearch(query["status"]).ToLowerInvariant();
        var correlationId = CleanSearch(query["correlationId"]);
        var limit = int.TryParse(query["limit"], out var requested)
            ? Math.Clamp(requested, 1, 500)
            : 200;

        var events = Evidence
            .Where(item => string.IsNullOrWhiteSpace(module)
                || string.Equals(
                    item.ModuleCode,
                    module,
                    StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(status)
                || string.Equals(
                    item.Status,
                    status,
                    StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(correlationId)
                || item.CorrelationId.Contains(
                    correlationId,
                    StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(search)
                || EvidenceSearchText(item).Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.ObservedAt)
            .Take(limit)
            .ToArray();

        var workers = LoadWorkers(context);
        return Results.Ok(new
        {
            module = "016",
            moduleName = "Operational Evidence & Diagnostic History",
            status = "operational_evidence_loaded",
            contractVersion = ContractVersion,
            generatedAt = DateTimeOffset.UtcNow,
            access = AccessContract(context),
            filters = new
            {
                search,
                module,
                status,
                correlationId,
                limit
            },
            summary = new
            {
                returned = events.Length,
                failed = events.Count(item => item.Status == "failed"),
                rejected = events.Count(item => item.Status == "rejected"),
                succeeded = events.Count(item =>
                    item.Status == "succeeded" || item.Status == "healthy"),
                workerCount = workers.Length
            },
            events,
            workers,
            scheduledJobs = ScheduledJobs(),
            dependencyTimeline = BuildDependencyTimeline(events),
            export = new
            {
                json = "/api/platform-operations/evidence/export",
                secretValuesIncluded = false
            },
            legacyBackupRetention = new
            {
                preserved = true,
                statusEndpoint = "/api/system/backup-retention/status",
                route = "#backup-retention",
                note = "Existing backup-retention inventory and guarded deletion remain preserved as a legacy operational subsection."
            },
            security = SecurityContract()
        });
    }

    private static async Task<IResult> ExportEvidenceAsync(HttpContext context)
    {
        var authorization = await AuthorizeAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;
        await using var connection = authorization.Connection!;

        var rows = Evidence
            .OrderByDescending(item => item.ObservedAt)
            .Take(1000)
            .ToArray();

        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                title = "ProjectPulse Operational Evidence Export",
                module = "016",
                generatedAt = DateTimeOffset.UtcNow,
                provider = DetectAdapter().Provider,
                environment = RuntimeEnvironment(),
                releaseSha = ReleaseSha(),
                events = rows,
                secretValuesIncluded = false,
                rawExceptionMessagesIncluded = false
            },
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

        return Results.File(
            payload,
            "application/json",
            $"projectpulse-operational-evidence-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
    }

    private static async Task<PlatformSnapshot> BuildSnapshotAsync(
        HttpContext context,
        NpgsqlConnection connection)
    {
        var adapter = DetectAdapter();
        using var process = Process.GetCurrentProcess();
        process.Refresh();

        var uptime = DateTimeOffset.UtcNow - ProcessStartedAt;
        var totalProcessorMs = process.TotalProcessorTime.TotalMilliseconds;
        var cpuPercent = uptime.TotalMilliseconds > 0
            ? Math.Clamp(
                totalProcessorMs
                / uptime.TotalMilliseconds
                / Math.Max(Environment.ProcessorCount, 1)
                * 100,
                0,
                100)
            : 0;
        var memory = ReadMemory();
        var drives = ReadDrives();
        var database = await CheckDatabaseAsync(
            connection,
            context.RequestAborted);
        var storage = CheckStorage(adapter);
        var integrations = await LoadIntegrationsAsync(
            connection,
            context.RequestAborted);
        var workers = LoadWorkers(context);

        return new PlatformSnapshot(
            new PlatformIdentity(
                adapter.Provider,
                adapter.DisplayName,
                adapter.Adapter,
                adapter.AdapterStatus,
                RuntimeEnvironment(),
                adapter.Region,
                adapter.WorkloadKind,
                adapter.Instance,
                adapter.IsCloud),
            new RuntimeIdentity(
                Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                    ?? "not_recorded",
                ReleaseSha(),
                ProcessStartedAt,
                Math.Round(uptime.TotalSeconds),
                adapter.Deployment,
                LastDeploymentAt(),
                Environment.ProcessorCount,
                RuntimeInformation()),
            new ResourceSnapshot(
                Math.Round(cpuPercent, 2),
                process.WorkingSet64,
                process.PrivateMemorySize64,
                GC.GetTotalMemory(false),
                memory.ContainerCurrentBytes,
                memory.ContainerLimitBytes,
                memory.TotalBytes,
                memory.AvailableBytes,
                drives),
            new DependencySnapshot(database, storage),
            integrations,
            workers,
            adapter.Deployments,
            adapter.Replicas,
            Capabilities(adapter),
            adapter.ProviderSpecificDetails);
    }

    private static async Task<object[]> BuildVersionInventoryAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var databaseVersion = "not_reported";
        try
        {
            await using var command = new NpgsqlCommand("SHOW server_version;", connection);
            databaseVersion = Convert.ToString(
                    await command.ExecuteScalarAsync(cancellationToken))?.Trim()
                ?? "not_reported";
        }
        catch
        {
            databaseVersion = "not_reported";
        }

        static string Setting(string name, string fallback = "not_reported")
        {
            var value = Environment.GetEnvironmentVariable(name)?.Trim();
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        static bool Enabled(string name) =>
            bool.TryParse(
                Environment.GetEnvironmentVariable(name)?.Trim(),
                out var enabled)
            && enabled;

        var inferenceModel = Setting("PROJECTPULSE_PRIVATE_INFERENCE_MODEL");
        var embeddingModel = Setting("PROJECTPULSE_PRIVATE_EMBEDDING_MODEL");
        var ocrModel = Setting("PROJECTPULSE_PRIVATE_OCR_MODEL");
        var scannerMode = Setting(
            "PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCANNER_MODE");
        var signatureVersion = Setting(
            "PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SIGNATURE_VERSION");
        var externalRuntime = Enabled(
            "PROJECTPULSE_CELAR_AI_EXTERNAL_HTTPS_RUNTIME_ENABLED");

        return
        [
            new { key = "pulse_api", component = "Pulse API", version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "not_recorded", status = "running", source = "assembly", detail = "Current API application assembly." },
            new { key = "pulse_release", component = "Pulse release", version = ReleaseSha(), status = "running", source = "deployment", detail = "Immutable source marker for the active API revision." },
            new { key = "dotnet", component = ".NET runtime", version = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, status = "running", source = "runtime", detail = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString() },
            new { key = "operating_system", component = "Operating system", version = System.Runtime.InteropServices.RuntimeInformation.OSDescription, status = "running", source = "runtime", detail = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString() },
            new { key = "postgresql", component = "PostgreSQL", version = databaseVersion, status = databaseVersion == "not_reported" ? "not_reported" : "running", source = "database", detail = "Server-reported database version." },
            new { key = "private_inference", component = "Ollama private inference model", version = inferenceModel, status = inferenceModel == "not_reported" ? "not_configured" : "configured", source = "deployment_configuration", detail = "Availability remains governed by Module 064 live health." },
            new { key = "private_embeddings", component = "Ollama embedding model", version = embeddingModel, status = embeddingModel == "not_reported" ? "not_configured" : "configured", source = "deployment_configuration", detail = "Expected vector dimension remains 768." },
            new { key = "ocr", component = "Private OCR", version = ocrModel, status = ocrModel == "not_reported" ? "not_configured" : "configured", source = "deployment_configuration", detail = "Tesseract engine readiness remains governed by the authenticated private runtime." },
            new { key = "malware_scanning", component = "Private malware scanning", version = signatureVersion, status = scannerMode == "not_reported" ? "not_configured" : "configured", source = "deployment_configuration", detail = $"Mode: {scannerMode}. Exact engine version is shown only when reported by the private runtime." },
            new { key = "private_gateway", component = "Celar AI HTTPS gateway", version = Setting("PROJECTPULSE_CELAR_AI_GATEWAY_VERSION"), status = externalRuntime ? "configured" : "not_configured", source = "deployment_configuration", detail = "Raw endpoint and credential values are not returned." },
            new { key = "caddy", component = "Caddy TLS gateway", version = Setting("PROJECTPULSE_CELAR_AI_CADDY_VERSION"), status = externalRuntime ? "configured" : "not_configured", source = "deployment_configuration", detail = "Exact version is displayed only when the runtime publishes approved non-secret metadata." },
            new { key = "clamav", component = "ClamAV engine", version = Setting("PROJECTPULSE_CELAR_AI_CLAMAV_VERSION", signatureVersion), status = scannerMode == "not_reported" ? "not_configured" : "configured", source = "deployment_configuration", detail = "Signature evidence is shown when an engine version is not separately reported." }
        ];
    }

    private static List<ApiInventoryItem> BuildApiInventory(HttpContext context)
    {
        var dataSources = context.RequestServices.GetServices<EndpointDataSource>();
        var rows = new List<ApiInventoryItem>();

        foreach (var endpoint in dataSources
                     .SelectMany(source => source.Endpoints)
                     .OfType<RouteEndpoint>())
        {
            var path = endpoint.RoutePattern.RawText;
            if (string.IsNullOrWhiteSpace(path)) continue;

            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                ?? new[] { "ANY" };

            foreach (var methodValue in methods)
            {
                var method = methodValue.ToUpperInvariant();
                var module = InferModule(path);
                var observation = ApiObservations.TryGetValue(
                    ApiKey(method, path),
                    out var observed)
                    ? observed
                    : null;
                var retest = SafeRetest(path, method);

                rows.Add(new ApiInventoryItem(
                    ApiId(method, path),
                    RouteGroup(path),
                    method,
                    path,
                    module.Code,
                    module.Name,
                    PurposeFor(path, endpoint.DisplayName),
                    IsPublicPath(path) ? "public" : "projectpulse_session",
                    PermissionFor(module.Code, path),
                    DependenciesFor(path),
                    observation is null
                        ? "not_observed"
                        : observation.LastStatusCode >= 500
                            ? "failed"
                            : observation.LastStatusCode >= 400
                                ? "rejected"
                                : "healthy",
                    observation?.LastCheckedAt,
                    observation?.LastSuccessAt,
                    observation?.LastFailureAt,
                    observation?.LastLatencyMs,
                    observation?.LastErrorCode ?? string.Empty,
                    observation?.LastCorrelationId ?? string.Empty,
                    retest.Capability,
                    retest.Reason,
                    "not_recorded",
                    ReleaseSha()));
            }
        }

        return rows
            .GroupBy(item => item.ApiId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.ModuleCode)
            .ThenBy(item => item.Path)
            .ThenBy(item => item.Method)
            .ToList();
    }

    private static async Task<DependencyCheck> CheckDatabaseAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var command = new NpgsqlCommand("SELECT 1;", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            stopwatch.Stop();

            return new DependencyCheck(
                "database",
                "ProjectPulse database",
                "healthy",
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                DateTimeOffset.UtcNow,
                "Authenticated query completed.",
                string.Empty);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new DependencyCheck(
                "database",
                "ProjectPulse database",
                "failed",
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                DateTimeOffset.UtcNow,
                "Database connectivity check failed.",
                exception.GetType().Name);
        }
    }

    private static DependencyCheck CheckStorage(IPlatformAdapter adapter)
    {
        var configuredPath = FirstEnvironment(
            "PROJECTPULSE_ARTIFACT_STORAGE_PATH",
            "PROJECTPULSE_STORAGE_PATH",
            "PROJECTPULSE_UPLOAD_ROOT");
        var providerConfigured = !string.IsNullOrWhiteSpace(FirstEnvironment(
            "PROJECTPULSE_STORAGE_PROVIDER",
            "AZURE_STORAGE_ACCOUNT",
            "PROJECTPULSE_BLOB_ACCOUNT"));

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var exists = Directory.Exists(configuredPath);
            return new DependencyCheck(
                "storage",
                "Governed artifact storage",
                exists ? "healthy" : "failed",
                null,
                DateTimeOffset.UtcNow,
                exists
                    ? "Configured storage directory is present."
                    : "Configured storage directory is not present.",
                exists ? string.Empty : "STORAGE_PATH_UNAVAILABLE");
        }

        return new DependencyCheck(
            "storage",
            "Governed artifact storage",
            providerConfigured ? "configured" : "not_configured",
            null,
            DateTimeOffset.UtcNow,
            providerConfigured
                ? $"{adapter.DisplayName} storage configuration is present; a live read probe is not configured."
                : "No provider-neutral artifact storage probe is configured.",
            providerConfigured
                ? string.Empty
                : "STORAGE_PROBE_NOT_CONFIGURED");
    }

    private static async Task<IntegrationStatus[]> LoadIntegrationsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<IntegrationStatus>
        {
            new(
                "microsoft",
                "Microsoft Integration",
                "identity_mail_calendar",
                MicrosoftConfigured() ? "configured" : "not_configured",
                null,
                "Module 065",
                ["identity", "Graph", "mail"],
                false),
            new(
                "mail",
                "Global mail delivery",
                "mail",
                MailConfigured() ? "configured" : "not_configured",
                null,
                "Module 065",
                ["Graph Mail.Send", "SMTP relay"],
                false),
            new(
                "github",
                "GitHub release controls",
                "delivery",
                !string.IsNullOrWhiteSpace(FirstEnvironment(
                    "GITHUB_REPOSITORY",
                    "PROJECTPULSE_GITHUB_REPOSITORY"))
                    ? "configured"
                    : "source_managed",
                null,
                "Module 077",
                ["source", "validation", "deployment controls"],
                false)
        };

        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT provider_key, provider_name, provider_type, is_enabled,
                       availability_status, last_checked_at
                FROM crm_integration_providers
                ORDER BY lower(provider_name);
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new IntegrationStatus(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetBoolean(3) ? reader.GetString(4) : "disabled",
                    reader.IsDBNull(5)
                        ? null
                        : new DateTimeOffset(reader.GetDateTime(5)),
                    "Module 026",
                    [reader.GetString(2)],
                    false));
            }
        }
        catch (PostgresException exception)
            when (exception.SqlState is "42P01" or "42703")
        {
            rows.Add(new IntegrationStatus(
                "crm_erp_registry",
                "CRM/ERP integration registry",
                "integration",
                "schema_not_available",
                null,
                "Module 026",
                ["SELL", "Salesforce", "ServiceNow", "Certinia"],
                false));
        }

        return rows.ToArray();
    }

    private static WorkerStatus[] LoadWorkers(HttpContext context)
    {
        try
        {
            return context.RequestServices.GetServices<IHostedService>()
                .Select(service => new WorkerStatus(
                    service.GetType().Name,
                    FriendlyTypeName(service.GetType().Name),
                    "registered",
                    "ASP.NET Core hosted service",
                    "Restart is supported only when the current platform adapter exposes an isolated worker action."))
                .DistinctBy(item => item.Key)
                .OrderBy(item => item.Name)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static object[] ScheduledJobs() =>
    [
        new
        {
            key = "runtime_registered_jobs",
            name = "Runtime-registered scheduled jobs",
            status = "discovery_not_configured",
            message = "A provider-neutral scheduler adapter has not been configured. No job is represented as healthy without evidence."
        }
    ];

    private static object[] BuildDependencyTimeline(OperationalEvidence[] events) =>
        events
            .Where(item => item.Status is "failed" or "rejected")
            .GroupBy(item => item.ModuleCode)
            .Select(group => new
            {
                moduleCode = group.Key,
                failureCount = group.Count(),
                latestObservedAt = group.Max(item => item.ObservedAt),
                latestCorrelationId = group
                    .OrderByDescending(item => item.ObservedAt)
                    .First()
                    .CorrelationId,
                latestErrorCode = group
                    .OrderByDescending(item => item.ObservedAt)
                    .First()
                    .ErrorCode
            })
            .OrderByDescending(item => item.latestObservedAt)
            .Take(25)
            .Cast<object>()
            .ToArray();

    private static object[] TroubleshootingFor(ApiInventoryItem api)
    {
        var steps = new List<object>
        {
            new
            {
                order = 1,
                action = "Confirm session and permissions",
                detail = $"Required: {api.AuthenticationRequirement}; permissions: {api.PermissionRequirement}."
            },
            new
            {
                order = 2,
                action = "Review dependencies",
                detail = string.Join(", ", api.Dependencies)
            },
            new
            {
                order = 3,
                action = "Search correlation evidence",
                detail = string.IsNullOrWhiteSpace(api.CorrelationId)
                    ? "No correlation ID has been observed yet."
                    : api.CorrelationId
            },
            new
            {
                order = 4,
                action = "Retest safely",
                detail = api.RetestReason
            }
        };

        if (api.CurrentStatus == "not_observed")
        {
            steps.Add(new
            {
                order = 5,
                action = "Generate evidence",
                detail = "Open the owning module or use the safe retest action when supported."
            });
        }

        return steps.ToArray();
    }

    private static object[] ApiActions(ApiInventoryItem api) =>
    [
        new
        {
            action = "retest_api",
            state = api.RetestCapability,
            message = api.RetestReason
        },
        new
        {
            action = "restart_http_route",
            state = "not_supported",
            message = "HTTP routes share the ProjectPulse API process and cannot be restarted independently."
        },
        new
        {
            action = "restart_application_service",
            state = "adapter_required",
            message = "A guarded provider adapter and deployment approval are required to restart the complete API service."
        }
    ];

    private static PlatformCapability[] Capabilities(IPlatformAdapter adapter) =>
    [
        new(
            "retest_api",
            "Retest one safe read-only API",
            "supported",
            "Available for registered GET routes without route parameters, callbacks, downloads, or authentication transitions."),
        new(
            "refresh_configuration",
            "Refresh runtime configuration snapshot",
            "supported",
            "Refreshes the provider-neutral snapshot; it does not mutate provider configuration."),
        new(
            "rebuild_integration_client",
            "Rebuild integration client",
            "connector_required",
            "Each integration must expose an approved bounded reset adapter."),
        new(
            "reset_provider_connection",
            "Reset provider connection",
            "connector_required",
            "Provider-specific reset contracts remain explicit and audited."),
        new(
            "restart_background_worker",
            "Restart one background worker",
            adapter.SupportsIsolatedWorkerRestart
                ? "adapter_required"
                : "not_supported",
            adapter.SupportsIsolatedWorkerRestart
                ? "An isolated target, approval, verification, and rollback contract must be configured."
                : "The running deployment does not expose workers as independently restartable units."),
        new(
            "restart_application_service",
            "Restart a separately deployed service",
            adapter.SupportsServiceRestart
                ? "adapter_required"
                : "not_supported",
            adapter.SupportsServiceRestart
                ? "A provider connector, approval, audit, verification, and rollback contract are required."
                : "The current adapter does not expose a service restart action."),
        new(
            "restart_http_route",
            "Restart one HTTP route",
            "not_supported",
            "Routes share one API process and cannot be restarted independently."),
        new(
            "restart_api_process",
            "Restart the complete API application",
            adapter.SupportsServiceRestart
                ? "adapter_required"
                : "not_supported",
            "A production-changing action requires a provider adapter, approval, audit, verification, and rollback.")
    ];

    private static (string Capability, string Reason) SafeRetest(
        string path,
        string method)
    {
        if (!HttpMethods.IsGet(method))
        {
            return (
                "not_supported",
                "Only safe read-only GET routes can be retested.");
        }

        if (path.Contains('{')
            || path.Contains("/auth/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("callback", StringComparison.OrdinalIgnoreCase)
            || path.Contains("download", StringComparison.OrdinalIgnoreCase)
            || path.Contains("export", StringComparison.OrdinalIgnoreCase)
            || path.Contains("stream", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(
                "/api/platform-operations/apis/",
                StringComparison.OrdinalIgnoreCase))
        {
            return (
                "not_supported",
                "This route requires parameters, changes authentication state, downloads content, or could recurse.");
        }

        return (
            "supported",
            "A same-origin GET probe can verify response status and latency without reading or returning the response body.");
    }

    private static string RouteGroup(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"/{parts[0]}/{parts[1]}" : path;
    }

    private static readonly (string Code, string Name, string[] Prefixes)[] ModuleRoutes =
    [
        ("001", "Timesheet", ["/api/timesheet", "/api/time-entr", "/api/users/timesheet"]),
        ("002", "Approval Inbox", ["/api/manager/approval", "/api/approval-center", "/api/scoped-approval"]),
        ("005", "Project Expense Upload", ["/api/project-expense", "/api/project-allocation"]),
        ("007", "Approval, Export & Audit Workflow", ["/api/workflow", "/api/accounting", "/api/export"]),
        ("008", "Audit History", ["/api/admin/audit", "/api/audit/history"]),
        ("009", "User Administration", ["/api/admin/users"]),
        ("010", "Azure / Entra Directory Users", ["/api/admin/azure"]),
        ("012", "Role Administration", ["/api/role-policy"]),
        ("013", "System Health & API Diagnostics", ["/api/platform-operations/overview", "/api/platform-operations/apis", "/api/system/service-control", "/api/system/api-status", "/api/system/version-inventory"]),
        ("014", "Backup & Disaster Recovery", ["/api/system/backup-dr"]),
        ("015", "Restore Validation", ["/api/system/restore-validation"]),
        ("016", "Operational Evidence & Backup Retention", ["/api/platform-operations/evidence", "/api/system/backup-retention"]),
        ("017", "Replication & Sync", ["/api/system/replication-sync"]),
        ("018", "Project Workload", ["/api/project-workload"]),
        ("019", "Project Workspace", ["/api/project-workspace"]),
        ("020", "Project Intake & Resource Handoff", ["/api/project-intake"]),
        ("021", "Customer Directory", ["/api/customers"]),
        ("022", "Cost Alerts", ["/api/cost-alert"]),
        ("023", "Time Compliance", ["/api/time-compliance"]),
        ("026", "CRM / ERP Integration Center", ["/api/integrations/026"]),
        ("030", "Reporting", ["/api/report"]),
        ("038", "Certify Connection & Sync", ["/api/certify"]),
        ("039", "Billing Readiness", ["/api/billing-readiness"]),
        ("040", "Project Closeout", ["/api/project-closeout"]),
        ("041", "Closeout Email Automation", ["/api/closeout-email"]),
        ("042", "Invoice & Billing Center", ["/api/invoice", "/api/billing"]),
        ("055C", "Manage Existing Projects", ["/api/work-register"]),
        ("055D", "Create New Project", ["/api/work-register/create"]),
        ("057", "Calendar & Capacity", ["/api/calendar", "/api/capacity"]),
        ("058", "CI/CD Pipeline", ["/api/cicd"]),
        ("060", "Contracts", ["/api/contracts"]),
        ("064", "AI Provider Configuration", ["/api/ai-provider", "/api/ai/"]),
        ("065", "Microsoft Integration Connection", ["/api/microsoft-integration", "/api/global-mail", "/api/auth/sso"]),
        ("068", "System Architecture", ["/api/platform-operations/architecture", "/api/system-architecture"]),
        ("069", "Qualifications & Certifications", ["/api/qualifications"]),
        ("070", "Capacity & Pipeline Forecasting", ["/api/capacity-forecast"]),
        ("071", "On-Call Scheduling", ["/api/oncall"]),
        ("072", "OneAssist Routing Directory", ["/api/oneassist"]),
        ("073", "Sales Coverage Alignment", ["/api/sales-coverage"]),
        ("074", "OEM & Vendor Directory", ["/api/oem"]),
        ("075", "Integration Event Gateway", ["/api/integration-event"]),
        ("076", "Defect Tracker", ["/api/defect"]),
        ("077", "Release & Deployment Control", ["/api/release-deployment"]),
        ("078", "Observability & SLO Health", ["/api/observability"]),
        ("079", "Data Governance & Retention", ["/api/data-governance"]),
        ("080", "Customer Delivery Acceptance", ["/api/customer-delivery"]),
        ("997", "Security Operations", ["/api/security-operations"]),
        ("998", "System Diagnostics & Controlled Remediation", ["/api/system-diagnostics"])
    ];

    private static ModuleOwner InferModule(string path)
    {
        var value = path.ToLowerInvariant();
        foreach (var entry in ModuleRoutes)
        {
            if (entry.Prefixes.Any(prefix => value.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase)))
                return new ModuleOwner(entry.Code, entry.Name);
        }

        return new ModuleOwner("PLATFORM", "Shared platform API");
    }

    private static string PermissionFor(string moduleCode, string path) =>
        moduleCode switch
        {
            "013" or "016" or "068" => "SYSTEM_ADMINISTRATION or MANAGE_ALL",
            "009" => "VIEW_USER_ADMIN or MANAGE_USER_ADMIN",
            "012" => "published role-policy decision",
            "997" => "VIEW_SECURITY_OPERATIONS or MANAGE_SECURITY_RESPONSE",
            "998" => "VIEW_SYSTEM_DIAGNOSTICS or MANAGE_SYSTEM_REMEDIATION",
            "PLATFORM" when IsPublicPath(path) => "none",
            _ => "owning endpoint policy"
        };

    private static string[] DependenciesFor(string path)
    {
        var value = path.ToLowerInvariant();
        var dependencies = new List<string> { "ProjectPulse API runtime" };

        if (!IsPublicPath(path)) dependencies.Add("ProjectPulse session");
        if (value.Contains("azure")
            || value.Contains("microsoft")
            || value.Contains("sso"))
            dependencies.Add("Microsoft Integration");
        if (value.Contains("integrations/026")
            || value.Contains("customers/sell"))
            dependencies.Add("Module 026 provider registry");
        if (value.Contains("mail") || value.Contains("email"))
            dependencies.Add("Module 065 mail configuration");
        if (value.Contains("backup") || value.Contains("restore"))
            dependencies.Add("Artifact storage");
        if (value.Contains("cicd") || value.Contains("release"))
            dependencies.Add("GitHub release controls");
        if (!value.Contains("/health"))
            dependencies.Add("ProjectPulse database where required");

        return dependencies
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string PurposeFor(string path, string? displayName)
    {
        var module = InferModule(path);
        var action = path
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? "endpoint";
        action = System.Text.RegularExpressions.Regex.Replace(
            action,
            @"\{[^}]+\}",
            "record");
        action = action.Replace('-', ' ').Replace('_', ' ');

        return !string.IsNullOrWhiteSpace(displayName)
            ? Limit(displayName, 220)
            : $"{module.Name}: {action}";
    }

    private static bool IsPublicPath(string path) =>
        path.Equals("/health", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/api/public/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/api/bootstrap", StringComparison.OrdinalIgnoreCase);

    private static void ForwardSessionHeaders(
        HttpContext source,
        HttpRequestMessage target)
    {
        foreach (var header in new[]
                 {
                     "Authorization",
                     "X-ProjectPulse-Session",
                     "X-Project-Pulse-Session",
                     "X-Session-Token",
                     "X-ProjectPulse-View-As-User"
                 })
        {
            if (!source.Request.Headers.TryGetValue(header, out var values))
                continue;
            target.Headers.TryAddWithoutValidation(header, values.ToArray());
        }

        target.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }
}
