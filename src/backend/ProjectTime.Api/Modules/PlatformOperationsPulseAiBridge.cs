using System.Diagnostics;
using System.Text.Json;
using Npgsql;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

public static partial class PlatformOperationsModule
{
    internal static async Task<PulseAiSystemOperationsSnapshot> BuildPulseAiSystemOperationsSnapshotAsync(
        HttpContext context,
        PulseAiSystemOperationsQuery query,
        CancellationToken cancellationToken)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var diagnosticCode = string.Empty;
        var apiInventory = BuildApiInventory(context);
        var slowThresholdMs = SlowThresholdMilliseconds();
        var allApis = apiInventory.Select(ToPulseAiApi).ToArray();
        var matching = FilterApis(apiInventory, query.Classification, query.IncludeNotObserved, slowThresholdMs)
            .Take(Math.Clamp(query.MaximumResults, 1, 500))
            .Select(ToPulseAiApi)
            .ToArray();

        var events = query.IncludeRecentEvidence
            ? FilterEvents(Evidence.ToArray(), query.Classification)
                .OrderByDescending(item => item.ObservedAt)
                .Take(Math.Clamp(query.MaximumResults * 2, 20, 500))
                .Select(item => new PulseAiSystemEventRecord(
                    item.EvidenceId,
                    item.ObservedAt,
                    item.CorrelationId,
                    item.ModuleCode,
                    item.ModuleName,
                    item.EventType,
                    item.Status,
                    item.Method,
                    item.Path,
                    item.StatusCode,
                    item.ResponseTimeMs,
                    item.ErrorCode,
                    item.Message,
                    item.ReleaseSha,
                    "module_013_runtime_telemetry"))
                .ToList()
            : [];

        PlatformSnapshot? platformSnapshot = null;
        var findings = new List<PulseAiSystemFindingRecord>();
        var connectionString = BuildConnectionString();
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                platformSnapshot = await BuildSnapshotAsync(context, connection);
                findings.AddRange(await LoadPersistentFindingsAsync(
                    connection,
                    query.Classification,
                    Math.Clamp(query.MaximumResults, 1, 250),
                    cancellationToken));
                events.AddRange(await LoadClientDiagnosticEventsAsync(
                    connection,
                    query.Classification,
                    Math.Clamp(query.MaximumResults, 1, 250),
                    cancellationToken));
            }
            catch (Exception exception)
            {
                diagnosticCode = exception switch
                {
                    PostgresException postgres => $"postgres_{postgres.SqlState}",
                    NpgsqlException => "database_transport_failure",
                    TimeoutException => "database_timeout",
                    _ => "platform_snapshot_unavailable"
                };
            }
        }
        else
        {
            diagnosticCode = "database_configuration_missing";
        }

        events = events
            .OrderByDescending(item => item.ObservedAt)
            .Take(Math.Clamp(query.MaximumResults * 2, 20, 500))
            .ToList();

        var runtime = ToPulseAiRuntime(platformSnapshot);
        var dependencies = platformSnapshot is null
            ? Array.Empty<PulseAiSystemDependencyRecord>()
            : new[]
            {
                ToPulseAiDependency(platformSnapshot.Dependencies.Database),
                ToPulseAiDependency(platformSnapshot.Dependencies.Storage)
            };
        var integrations = platformSnapshot?.Integrations
            .Select(item => new PulseAiSystemIntegrationRecord(
                item.Key,
                item.Name,
                item.Type,
                item.Status,
                item.LastCheckedAt,
                item.Owner,
                item.Capabilities))
            .ToArray()
            ?? [];
        var workers = platformSnapshot?.Workers
            .Select(item => new PulseAiSystemWorkerRecord(
                item.Key,
                item.Name,
                item.Status,
                item.Source,
                item.RestartMessage))
            .ToArray()
            ?? [];

        var status = allApis.Length == 0
            ? "api_inventory_unavailable"
            : diagnosticCode.Length == 0
                ? "live_system_operations_snapshot_ready"
                : "live_api_inventory_ready_platform_snapshot_partial";

        return new PulseAiSystemOperationsSnapshot(
            Status: status,
            Runtime: runtime,
            AllApis: allApis,
            MatchingApis: matching,
            RecentEvents: events,
            PersistentFindings: findings.OrderByDescending(item => item.ObservedAt).ToArray(),
            Dependencies: dependencies,
            Integrations: integrations,
            Workers: workers,
            TotalApiCount: allApis.Length,
            MatchingApiCount: matching.Length,
            HealthyApiCount: allApis.Count(item => item.CurrentStatus == "healthy"),
            FailedApiCount: allApis.Count(item => item.CurrentStatus == "failed"),
            RejectedApiCount: allApis.Count(item => item.CurrentStatus == "rejected"),
            NotObservedApiCount: allApis.Count(item => item.CurrentStatus == "not_observed"),
            SafeRetestApiCount: allApis.Count(item => item.RetestCapability == "supported"),
            SlowApiCount: allApis.Count(item => item.ResponseTimeMs >= slowThresholdMs),
            DataAsOf: generatedAt,
            DiagnosticCode: diagnosticCode);
    }

    internal static Task<IResult> RetestPulseAiSafeApiAsync(
        string apiId,
        HttpContext context) =>
        RetestApiAsync(apiId, context);

    private static IEnumerable<ApiInventoryItem> FilterApis(
        IReadOnlyList<ApiInventoryItem> apis,
        PulseAiSystemOperationsClassification classification,
        bool includeNotObserved,
        double slowThresholdMs)
    {
        IEnumerable<ApiInventoryItem> filtered = apis;

        if (!includeNotObserved)
            filtered = filtered.Where(item => item.CurrentStatus != "not_observed");
        if (!string.IsNullOrWhiteSpace(classification.ApiId))
            filtered = filtered.Where(item => item.ApiId.Contains(classification.ApiId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(classification.ApiPath))
            filtered = filtered.Where(item =>
                item.Path.Equals(classification.ApiPath, StringComparison.OrdinalIgnoreCase)
                || item.Path.Contains(classification.ApiPath, StringComparison.OrdinalIgnoreCase)
                || classification.ApiPath.Contains(item.Path, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(classification.ApiMethod))
            filtered = filtered.Where(item => item.Method.Equals(classification.ApiMethod, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(classification.ModuleCode))
            filtered = filtered.Where(item => item.ModuleCode.Equals(classification.ModuleCode, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(classification.DependencyFilter))
            filtered = filtered.Where(item => item.Dependencies.Any(dependency =>
                dependency.Contains(classification.DependencyFilter, StringComparison.OrdinalIgnoreCase)
                || classification.DependencyFilter.Contains(dependency, StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrWhiteSpace(classification.CorrelationId))
            filtered = filtered.Where(item => item.CorrelationId.Contains(classification.CorrelationId, StringComparison.OrdinalIgnoreCase));

        filtered = classification.StatusFilter switch
        {
            "failed_or_rejected" => filtered.Where(item => item.CurrentStatus is "failed" or "rejected"),
            "failed" => filtered.Where(item => item.CurrentStatus == "failed"),
            "rejected" => filtered.Where(item => item.CurrentStatus == "rejected"),
            "healthy" => filtered.Where(item => item.CurrentStatus == "healthy"),
            "not_observed" => filtered.Where(item => item.CurrentStatus == "not_observed"),
            _ => filtered
        };

        if (classification.WantsFailuresOnly && string.IsNullOrWhiteSpace(classification.StatusFilter))
            filtered = filtered.Where(item => item.CurrentStatus is "failed" or "rejected");
        if (classification.WantsSlowApis)
            filtered = filtered.Where(item => item.ResponseTimeMs >= slowThresholdMs);
        if (classification.Intent == "safe_retest_candidates")
            filtered = filtered.Where(item => item.RetestCapability == "supported");

        return filtered
            .OrderBy(item => item.CurrentStatus == "failed" ? 0 : item.CurrentStatus == "rejected" ? 1 : item.CurrentStatus == "healthy" ? 2 : 3)
            .ThenByDescending(item => item.ResponseTimeMs ?? -1)
            .ThenBy(item => item.ModuleCode)
            .ThenBy(item => item.Path)
            .ThenBy(item => item.Method);
    }

    private static IEnumerable<OperationalEvidence> FilterEvents(
        IEnumerable<OperationalEvidence> events,
        PulseAiSystemOperationsClassification classification)
    {
        var filtered = events;
        if (!string.IsNullOrWhiteSpace(classification.CorrelationId))
            filtered = filtered.Where(item => item.CorrelationId.Contains(classification.CorrelationId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(classification.ApiPath))
            filtered = filtered.Where(item => item.Path.Contains(classification.ApiPath, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(classification.ApiMethod))
            filtered = filtered.Where(item => item.Method.Equals(classification.ApiMethod, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(classification.ModuleCode))
            filtered = filtered.Where(item => item.ModuleCode.Equals(classification.ModuleCode, StringComparison.OrdinalIgnoreCase));
        if (classification.WantsFailuresOnly || classification.Intent is "api_failure_analysis" or "troubleshooting")
            filtered = filtered.Where(item => item.Status is "failed" or "rejected");
        return filtered;
    }

    private static PulseAiSystemApiRecord ToPulseAiApi(ApiInventoryItem item)
    {
        ApiObservations.TryGetValue(ApiKey(item.Method, item.Path), out var observation);
        return new PulseAiSystemApiRecord(
            item.ApiId,
            item.RouteGroup,
            item.Method,
            item.Path,
            item.ModuleCode,
            item.ModuleName,
            item.Purpose,
            item.AuthenticationRequirement,
            item.PermissionRequirement,
            item.Dependencies,
            item.CurrentStatus,
            item.LastCheckedAt,
            item.LastSuccessfulRequestAt,
            item.LastFailureAt,
            item.ResponseTimeMs,
            item.LastErrorCode,
            item.CorrelationId,
            item.RetestCapability,
            item.RetestReason,
            item.IntroducedRelease,
            item.CurrentRelease,
            observation?.RequestCount ?? 0,
            observation?.FailureCount ?? 0);
    }

    private static PulseAiSystemRuntimeRecord ToPulseAiRuntime(PlatformSnapshot? snapshot)
    {
        if (snapshot is not null)
        {
            return new PulseAiSystemRuntimeRecord(
                snapshot.Platform.Provider,
                snapshot.Platform.DisplayName,
                snapshot.Platform.Adapter,
                snapshot.Platform.AdapterStatus,
                snapshot.Platform.Environment,
                snapshot.Platform.Region,
                snapshot.Platform.WorkloadKind,
                snapshot.Platform.Instance,
                snapshot.Runtime.ApplicationVersion,
                snapshot.Runtime.ReleaseSha,
                snapshot.Runtime.ProcessStartedAt,
                snapshot.Runtime.UptimeSeconds,
                snapshot.Runtime.Deployment,
                snapshot.Runtime.LastDeploymentAt,
                snapshot.Resources.CpuPercent,
                snapshot.Resources.ProcessWorkingSetBytes,
                snapshot.Resources.ProcessPrivateMemoryBytes,
                snapshot.Resources.ManagedHeapBytes,
                snapshot.Resources.ContainerMemoryCurrentBytes,
                snapshot.Resources.ContainerMemoryLimitBytes,
                snapshot.Resources.TotalMemoryBytes,
                snapshot.Resources.AvailableMemoryBytes);
        }

        var adapter = DetectAdapter();
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var uptime = DateTimeOffset.UtcNow - ProcessStartedAt;
        return new PulseAiSystemRuntimeRecord(
            adapter.Provider,
            adapter.DisplayName,
            adapter.Adapter,
            adapter.AdapterStatus,
            RuntimeEnvironment(),
            adapter.Region,
            adapter.WorkloadKind,
            adapter.Instance,
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "not_recorded",
            ReleaseSha(),
            ProcessStartedAt,
            Math.Round(uptime.TotalSeconds),
            adapter.Deployment,
            LastDeploymentAt(),
            0,
            process.WorkingSet64,
            process.PrivateMemorySize64,
            GC.GetTotalMemory(false),
            null,
            null,
            null,
            null);
    }

    private static PulseAiSystemDependencyRecord ToPulseAiDependency(DependencyCheck item) =>
        new(item.Key, item.Name, item.Status, item.LatencyMs, item.CheckedAt, item.Message, item.ErrorCode);

    private static async Task<IReadOnlyList<PulseAiSystemFindingRecord>> LoadPersistentFindingsAsync(
        NpgsqlConnection connection,
        PulseAiSystemOperationsClassification classification,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = new List<PulseAiSystemFindingRecord>();
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT f.diagnostic_finding_id, f.diagnostic_session_id,
                       f.check_code, f.category, f.status, f.severity,
                       f.summary, f.observed_at, s.target_kind, s.target_reference
                FROM projectpulse_diagnostic_findings f
                JOIN projectpulse_diagnostic_sessions s
                  ON s.diagnostic_session_id = f.diagnostic_session_id
                WHERE f.status IN ('warning','failed','unknown')
                  AND s.status <> 'closed'
                ORDER BY f.observed_at DESC
                LIMIT @limit;
                """, connection);
            command.Parameters.AddWithValue("limit", limit);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new PulseAiSystemFindingRecord(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetFieldValue<DateTimeOffset>(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    "module_998_persistent_diagnostic");
                if (MatchesFinding(row, classification)) rows.Add(row);
            }
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703")
        {
            return [];
        }
        return rows;
    }

    private static bool MatchesFinding(
        PulseAiSystemFindingRecord item,
        PulseAiSystemOperationsClassification classification)
    {
        if (!string.IsNullOrWhiteSpace(classification.CorrelationId)
            && !item.Summary.Contains(classification.CorrelationId, StringComparison.OrdinalIgnoreCase)
            && !item.TargetReference.Contains(classification.CorrelationId, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(classification.ApiPath)
            && !item.Summary.Contains(classification.ApiPath, StringComparison.OrdinalIgnoreCase)
            && !item.TargetReference.Contains(classification.ApiPath, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(classification.ModuleCode)
            && !item.Summary.Contains(classification.ModuleCode, StringComparison.OrdinalIgnoreCase)
            && !item.TargetReference.Contains(classification.ModuleCode, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static async Task<IReadOnlyList<PulseAiSystemEventRecord>> LoadClientDiagnosticEventsAsync(
        NpgsqlConnection connection,
        PulseAiSystemOperationsClassification classification,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = new List<PulseAiSystemEventRecord>();
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT COALESCE(new_value::text, '{}'), created_at
                FROM audit_logs
                WHERE action = 'client_api_error'
                ORDER BY created_at DESC
                LIMIT @limit;
                """, connection);
            command.Parameters.AddWithValue("limit", limit);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                using var json = JsonDocument.Parse(reader.GetString(0));
                var root = json.RootElement;
                var correlation = JsonString(root, "referenceId");
                var path = JsonString(root, "endpointPath");
                var statusCode = JsonInt(root, "statusCode");
                var status = statusCode >= 500 ? "failed" : "rejected";
                var module = InferModule(path);
                var eventRow = new PulseAiSystemEventRecord(
                    $"client-{correlation}",
                    reader.GetFieldValue<DateTimeOffset>(1),
                    correlation,
                    module.Code,
                    module.Name,
                    "client_api_error",
                    status,
                    "CLIENT",
                    path,
                    statusCode,
                    0,
                    JsonString(root, "technicalCode"),
                    JsonString(root, "userMessage"),
                    ReleaseSha(),
                    "sanitized_client_diagnostic");
                if (MatchesEvent(eventRow, classification)) rows.Add(eventRow);
            }
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703")
        {
            return [];
        }
        catch (JsonException)
        {
            return rows;
        }
        return rows;
    }

    private static bool MatchesEvent(
        PulseAiSystemEventRecord item,
        PulseAiSystemOperationsClassification classification)
    {
        if (!string.IsNullOrWhiteSpace(classification.CorrelationId)
            && !item.CorrelationId.Contains(classification.CorrelationId, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(classification.ApiPath)
            && !item.Path.Contains(classification.ApiPath, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(classification.ModuleCode)
            && !item.ModuleCode.Equals(classification.ModuleCode, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static string JsonString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static int JsonInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : 0;

    private static double SlowThresholdMilliseconds()
    {
        var value = Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_SLOW_API_THRESHOLD_MS");
        return double.TryParse(value, out var parsed)
            ? Math.Clamp(parsed, 100, 60_000)
            : 1_000;
    }
}
