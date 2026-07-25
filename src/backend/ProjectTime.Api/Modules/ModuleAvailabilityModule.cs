using System.Collections.Concurrent;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Persistent, audited availability controls for registered ProjectPulse modules.
/// Missing rows are treated as enabled so introduction of this feature never hides a module by default.
/// </summary>
public static class ModuleAvailabilityModule
{
    private const string MigrationFile = "042_module_availability_controls.sql";
    private const string ModuleHeader = "X-ProjectPulse-Module-Number";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(10);
    private static readonly ConcurrentDictionary<string, AvailabilityCacheEntry> AvailabilityCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, ModuleDefinition> Definitions =
        new Dictionary<string, ModuleDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["001"] = Module("001", "timesheet", "Timesheet", "Time Management"),
            ["002"] = Module("002", "manager-approval", "Approval Inbox", "Approvals"),
            ["003"] = Module("003", "utilization", "Utilization", "Resource Management"),
            ["004"] = Module("004", "holiday-admin", "Holiday Administration", "Time Management"),
            ["005"] = Module("005", "project-allocation-info", "Project Allocation Information", "Project Management"),
            ["006"] = Module("006", "psa-modules", "PSA Modules", "Platform Operations"),
            ["007"] = Module("007", "workflow", "Approval / Export / Audit Workflow", "Approvals"),
            ["008"] = Module("008", "audit-history", "Audit History", "Security & Audit"),
            ["009"] = Module("009", "user-admin", "User Administration", "Administration"),
            ["010"] = Module("010", "azure-admin", "Azure / Entra Administration", "Administration"),
            ["011"] = Module("011", "work-task-builder", "Work Task Builder", "Project Delivery"),
            ["012"] = Module("012", "role-admin", "Role Administration", "Administration"),
            ["013"] = Module("013", "service-control", "Service Control", "Platform Operations"),
            ["014"] = Module("014", "backup-dr", "Backup & Disaster Recovery", "Platform Operations"),
            ["015"] = Module("015", "restore-validation", "Restore Validation", "Platform Operations"),
            ["016"] = Module("016", "backup-retention", "Backup Retention", "Platform Operations"),
            ["017"] = Module("017", "replication-sync", "Replication & Sync", "Platform Operations"),
            ["018"] = Module("018", "project-workload", "Project Workload", "Project Management"),
            ["019"] = Module("019", "project-workspace", "Project Workspace", "Project Delivery"),
            ["020"] = Module("020", "project-intake", "Project Intake", "Project Delivery"),
            ["021"] = Module("021", "customer-directory", "Customer Directory", "Customers"),
            ["022"] = Module("022", "cost-alerts", "Cost Alerts", "Reports & Workflow"),
            ["023"] = Module("023", "time-compliance", "Time Compliance", "Time Management"),
            ["024"] = Module("024", "sales-intake", "Sales Intake", "Sales & Opportunities"),
            ["025"] = Module("025", "sow-generator", "SOW Generator", "Sales & Opportunities"),
            ["026"] = Module("026", "crm-integration", "CRM / ERP Integration Center", "Integrations"),
            ["027"] = Module("027", "signed-handoff", "Signed Handoff", "Project Delivery"),
            ["028"] = Module("028", "ai-time-entry", "AI Time Entry", "Time Management"),
            ["029"] = Module("029", "uat-validation", "UAT Validation", "Platform Operations"),
            ["030"] = Module("030", "reporting", "Reporting", "Reports & Workflow"),
            ["036"] = Module("036", "sales-insights", "Sales Insights Dashboard", "Sales & Opportunities"),
            ["037"] = Module("037", "roles-permissions-matrix", "Roles & Permissions Matrix", "Administration"),
            ["038"] = Module("038", "certify-integration", "Certinia Integration", "Integrations"),
            ["039"] = Module("039", "billing-readiness", "Billing Readiness", "Reports & Workflow"),
            ["040"] = Module("040", "project-closeout", "Project Closeout", "Reports & Workflow"),
            ["041"] = Module("041", "closeout-email", "Closeout Email Automation", "Reports & Workflow"),
            ["042"] = Module("042", "invoice-billing-center", "Invoice & Billing Center", "Reports & Workflow"),
            ["055B"] = Module("055B", "rate-card-administration", "Rate Card Administration", "Project Operations"),
            ["055C"] = Module("055C", "work-register", "Manage Existing Projects", "Project Operations"),
            ["055D"] = Module("055D", "create-work-register", "Create New Project", "Project Operations"),
            ["057"] = Module("057", "calendar-capacity", "Calendar & Capacity", "Resource Management"),
            ["058"] = Module("058", "cicd-pipeline", "CI/CD Pipeline", "Platform Operations"),
            ["060"] = Module("060", "contracts", "Contracts", "Project Operations"),
            ["063"] = Module("063", "opportunities", "Opportunities", "Sales & Opportunities"),
            ["064"] = Module("064", "ai-provider-configuration", "AI Provider Configuration Center", "Security"),
            ["065"] = Module("065", "entra-secret-administration", "Entra Secret Administration", "Security"),
            ["066"] = Module("066", "project-flowhive", "Project FlowHive", "Project Delivery"),
            ["067"] = Module("067", "global-mail-configuration", "Global Mail Configuration Center", "Platform Operations"),
            ["068"] = Module("068", "system-architecture", "System Architecture & Dependency Map", "Platform Operations"),
            ["069"] = Module("069", "qualifications-certifications", "Qualifications & Certification Matrix", "Resources"),
            ["070"] = Module("070", "capacity-pipeline-forecast", "Capacity & Pipeline Forecasting", "Resource Management"),
            ["071"] = Module("071", "oncall-scheduling", "On-Call Scheduling", "Platform Operations"),
            ["072"] = Module("072", "oneassist-routing-directory", "OneAssist Routing Directory", "Platform Operations"),
            ["073"] = Module("073", "sales-coverage-alignment", "Sales Coverage Alignment", "Sales & Opportunities"),
            ["074"] = Module("074", "oem-vendor-directory", "OEM & Vendor Directory", "Sales & Opportunities"),
            ["075"] = Module("075", "integration-event-gateway", "Integration Automation & Event Gateway", "Platform Operations"),
            ["076"] = Module("076", "defect-tracker", "Defect Intake & Resolution Tracker", "Help & Documentation"),
            ["077"] = Module("077", "release-deployment-control", "Release, Deployment & Rollback Control Center", "Platform Operations"),
            ["078"] = Module("078", "observability-slo-health", "Observability, SLO & Application Health Center", "Platform Operations"),
            ["079"] = Module("079", "data-governance-retention", "Data Governance, Retention & Privacy Center", "Security & Audit"),
            ["080"] = Module("080", "customer-delivery-acceptance", "Customer Delivery & Acceptance Portal", "Project Operations"),
            ["997"] = Module("997", "security-operations", "Security Operations, Threat Intelligence & Response Center", "Security & Audit"),
            ["998"] = Module("998", "system-diagnostics", "System Diagnostic & Controlled Remediation Center", "Platform Operations"),
            ["999"] = Module("999", "user-guide", "ProjectPulse Complete User Guide", "Help & Documentation")
        };

    public static WebApplication MapModuleAvailabilityEndpoints(this WebApplication app)
    {
        app.MapGet("/api/module-availability", GetAvailabilityAsync);
        app.MapGet("/api/module-availability/audit", GetAuditAsync);
        app.MapPut("/api/module-availability/{moduleNumber}", UpdateAvailabilityAsync);
        return app;
    }

    public static WebApplication UseModuleAvailabilityEnforcement(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/module-availability"))
            {
                await next();
                return;
            }

            var moduleNumber = context.Request.Headers[ModuleHeader].FirstOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(moduleNumber) || !Definitions.ContainsKey(moduleNumber))
            {
                await next();
                return;
            }

            var connectionString = BuildConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                await WriteUnavailableAsync(context, moduleNumber);
                return;
            }

            try
            {
                if (await IsEnabledAsync(connectionString, moduleNumber))
                {
                    await next();
                    return;
                }

                var effectiveUserId = SessionUserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId");
                if (effectiveUserId is null)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        module = moduleNumber,
                        status = "session_required"
                    });
                    return;
                }

                var effectiveRoles = await ReadRolesAsync(connectionString, effectiveUserId.Value);
                if (effectiveRoles.Contains("SUPER_ADMINISTRATOR"))
                {
                    await next();
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    module = moduleNumber,
                    status = "module_disabled",
                    message = "This module is disabled and is available only to the Super Administrator role."
                });
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                await WriteMigrationPendingAsync(context, moduleNumber);
            }
            catch (Exception exception)
            {
                LogFailure(context, exception, moduleNumber, "enforce module availability");
                await WriteUnavailableAsync(context, moduleNumber);
            }
        });

        return app;
    }

    private static async Task<IResult> GetAvailabilityAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context);
        if (access.Failure is not null) return access.Failure;

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return DependencyUnavailable();

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            var states = new Dictionary<string, StoredAvailability>(StringComparer.OrdinalIgnoreCase);
            await using (var command = new NpgsqlCommand("""
                SELECT module_number, is_enabled, revision_number, reason, updated_by, updated_at
                FROM projectpulse_module_availability;
                """, connection))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    states[reader.GetString(0)] = new StoredAvailability(
                        reader.GetBoolean(1),
                        reader.GetInt32(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetGuid(4),
                        reader.GetFieldValue<DateTimeOffset>(5));
                }
            }

            var modules = Definitions.Values
                .OrderBy(definition => ModuleSortKey(definition.ModuleNumber))
                .Select(definition =>
                {
                    states.TryGetValue(definition.ModuleNumber, out var stored);
                    return new
                    {
                        moduleNumber = definition.ModuleNumber,
                        route = definition.Route,
                        displayName = definition.DisplayName,
                        group = definition.Group,
                        isEnabled = stored?.IsEnabled ?? true,
                        revision = stored?.Revision ?? 0,
                        reason = stored?.Reason,
                        updatedBy = stored?.UpdatedBy,
                        updatedAt = stored?.UpdatedAt,
                        defaultState = stored is null ? "enabled" : "persisted"
                    };
                });

            return Results.Ok(new
            {
                modules,
                access = new
                {
                    actualRoles = access.Context!.ActualRoles.OrderBy(value => value),
                    effectiveRoles = access.Context.EffectiveRoles.OrderBy(value => value),
                    isSuperAdministrator = access.Context.EffectiveRoles.Contains("SUPER_ADMINISTRATOR"),
                    canManage = access.Context.CanManage,
                    isViewAs = access.Context.IsViewAs
                },
                policy = new
                {
                    disabledVisibility = "SUPER_ADMINISTRATOR_ONLY",
                    defaultState = "ENABLED",
                    deletionBehavior = "NO_SOURCE_OR_DATA_DELETION",
                    migration = MigrationFile
                }
            });
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return MigrationPending();
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "shared", "read module availability");
            return DependencyUnavailable();
        }
    }

    private static async Task<IResult> UpdateAvailabilityAsync(
        string moduleNumber,
        ModuleAvailabilityUpdateRequest request,
        HttpContext context)
    {
        if (!Definitions.TryGetValue(moduleNumber, out var definition))
        {
            return Results.NotFound(new { module = moduleNumber, status = "module_not_registered" });
        }

        var access = await ResolveAccessAsync(context);
        if (access.Failure is not null) return access.Failure;
        if (!access.Context!.CanManage)
        {
            return Results.Json(new
            {
                module = moduleNumber,
                status = access.Context.IsViewAs ? "actual_session_required" : "super_administrator_required",
                message = access.Context.IsViewAs
                    ? "Exit Administrator View-As before changing module availability."
                    : "Only the Super Administrator role may change module availability."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var reason = (request.Reason ?? string.Empty).Trim();
        if (reason.Length > 1000)
        {
            return Results.BadRequest(new
            {
                module = moduleNumber,
                status = "reason_too_long",
                message = "Reason must be 1,000 characters or fewer."
            });
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return DependencyUnavailable();

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            bool previousEnabled = true;
            int previousRevision = 0;
            await using (var select = new NpgsqlCommand("""
                SELECT is_enabled, revision_number
                FROM projectpulse_module_availability
                WHERE module_number = @module_number
                FOR UPDATE;
                """, connection, transaction))
            {
                select.Parameters.AddWithValue("module_number", definition.ModuleNumber);
                await using var reader = await select.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    previousEnabled = reader.GetBoolean(0);
                    previousRevision = reader.GetInt32(1);
                }
            }

            if (request.ExpectedRevision != previousRevision)
            {
                await transaction.RollbackAsync();
                return Results.Conflict(new
                {
                    module = moduleNumber,
                    status = "module_availability_revision_conflict",
                    expectedRevision = request.ExpectedRevision,
                    actualRevision = previousRevision,
                    message = "Module availability changed after this page was loaded. Refresh and try again."
                });
            }

            var nextRevision = previousRevision + 1;
            await using (var upsert = new NpgsqlCommand("""
                INSERT INTO projectpulse_module_availability
                    (module_number, route, display_name, is_enabled, revision_number, reason, updated_by, updated_at)
                VALUES
                    (@module_number, @route, @display_name, @is_enabled, @revision_number, @reason, @updated_by, now())
                ON CONFLICT (module_number) DO UPDATE
                SET route = EXCLUDED.route,
                    display_name = EXCLUDED.display_name,
                    is_enabled = EXCLUDED.is_enabled,
                    revision_number = EXCLUDED.revision_number,
                    reason = EXCLUDED.reason,
                    updated_by = EXCLUDED.updated_by,
                    updated_at = now();
                """, connection, transaction))
            {
                upsert.Parameters.AddWithValue("module_number", definition.ModuleNumber);
                upsert.Parameters.AddWithValue("route", definition.Route);
                upsert.Parameters.AddWithValue("display_name", definition.DisplayName);
                upsert.Parameters.AddWithValue("is_enabled", request.IsEnabled);
                upsert.Parameters.AddWithValue("revision_number", nextRevision);
                upsert.Parameters.AddWithValue("reason", string.IsNullOrWhiteSpace(reason) ? DBNull.Value : reason);
                upsert.Parameters.AddWithValue("updated_by", access.Context.ActualUserId);
                await upsert.ExecuteNonQueryAsync();
            }

            await using (var audit = new NpgsqlCommand("""
                INSERT INTO projectpulse_module_availability_audit
                    (module_number, route, display_name, previous_enabled, new_enabled, previous_revision,
                     new_revision, reason, changed_by, changed_at)
                VALUES
                    (@module_number, @route, @display_name, @previous_enabled, @new_enabled, @previous_revision,
                     @new_revision, @reason, @changed_by, now());
                """, connection, transaction))
            {
                audit.Parameters.AddWithValue("module_number", definition.ModuleNumber);
                audit.Parameters.AddWithValue("route", definition.Route);
                audit.Parameters.AddWithValue("display_name", definition.DisplayName);
                audit.Parameters.AddWithValue("previous_enabled", previousEnabled);
                audit.Parameters.AddWithValue("new_enabled", request.IsEnabled);
                audit.Parameters.AddWithValue("previous_revision", previousRevision);
                audit.Parameters.AddWithValue("new_revision", nextRevision);
                audit.Parameters.AddWithValue("reason", string.IsNullOrWhiteSpace(reason) ? DBNull.Value : reason);
                audit.Parameters.AddWithValue("changed_by", access.Context.ActualUserId);
                await audit.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            AvailabilityCache.TryRemove(definition.ModuleNumber, out _);

            return Results.Ok(new
            {
                moduleNumber = definition.ModuleNumber,
                definition.Route,
                definition.DisplayName,
                isEnabled = request.IsEnabled,
                revision = nextRevision,
                reason = string.IsNullOrWhiteSpace(reason) ? null : reason,
                message = request.IsEnabled
                    ? $"{definition.DisplayName} is enabled and will follow normal role and permission rules."
                    : $"{definition.DisplayName} is disabled and is visible only to Super Administrators."
            });
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return MigrationPending();
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, moduleNumber, "update module availability");
            return DependencyUnavailable();
        }
    }

    private static async Task<IResult> GetAuditAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context);
        if (access.Failure is not null) return access.Failure;
        if (!access.Context!.ActualRoles.Contains("SUPER_ADMINISTRATOR"))
        {
            return Results.Json(new
            {
                status = "super_administrator_required",
                message = "Only Super Administrators may view module availability audit history."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return DependencyUnavailable();

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT audit_id, module_number, route, display_name, previous_enabled, new_enabled,
                       previous_revision, new_revision, reason, changed_by, changed_at
                FROM projectpulse_module_availability_audit
                ORDER BY changed_at DESC
                LIMIT 200;
                """, connection);
            await using var reader = await command.ExecuteReaderAsync();
            var events = new List<object>();
            while (await reader.ReadAsync())
            {
                events.Add(new
                {
                    auditId = reader.GetGuid(0),
                    moduleNumber = reader.GetString(1),
                    route = reader.GetString(2),
                    displayName = reader.GetString(3),
                    previousEnabled = reader.GetBoolean(4),
                    newEnabled = reader.GetBoolean(5),
                    previousRevision = reader.GetInt32(6),
                    newRevision = reader.GetInt32(7),
                    reason = reader.IsDBNull(8) ? null : reader.GetString(8),
                    changedBy = reader.GetGuid(9),
                    changedAt = reader.GetFieldValue<DateTimeOffset>(10)
                });
            }

            return Results.Ok(new { count = events.Count, events });
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return MigrationPending();
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "shared", "read module availability audit");
            return DependencyUnavailable();
        }
    }

    private static async Task<AccessOutcome> ResolveAccessAsync(HttpContext context)
    {
        var actualUserId = SessionUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        var effectiveUserId = SessionUserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId");
        if (actualUserId is null || effectiveUserId is null)
        {
            return new(null, Results.Json(new { status = "session_required" }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new(null, DependencyUnavailable());
        }

        try
        {
            var actualRoles = await ReadRolesAsync(connectionString, actualUserId.Value);
            var effectiveRoles = actualUserId == effectiveUserId
                ? actualRoles
                : await ReadRolesAsync(connectionString, effectiveUserId.Value);
            var isViewAs = IsViewAs(context, actualUserId.Value, effectiveUserId.Value);
            var canManage = actualRoles.Contains("SUPER_ADMINISTRATOR") && !isViewAs;

            return new(new AccessContext(
                actualUserId.Value,
                effectiveUserId.Value,
                actualRoles,
                effectiveRoles,
                isViewAs,
                canManage), null);
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "shared", "authorize module availability");
            return new(null, DependencyUnavailable());
        }
    }

    private static async Task<HashSet<string>> ReadRolesAsync(string connectionString, Guid userId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT upper(COALESCE(r.role_code, ''))
            FROM app_user_role_assignments ura
            JOIN app_roles r
              ON r.app_role_id = ura.app_role_id
             AND r.is_active = TRUE
            WHERE ura.user_id = @user_id
              AND ura.is_active = TRUE;
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);

        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) roles.Add(reader.GetString(0));
        return roles;
    }

    private static async Task<bool> IsEnabledAsync(string connectionString, string moduleNumber)
    {
        if (AvailabilityCache.TryGetValue(moduleNumber, out var cached)
            && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.IsEnabled;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT is_enabled
            FROM projectpulse_module_availability
            WHERE module_number = @module_number;
            """, connection);
        command.Parameters.AddWithValue("module_number", moduleNumber);
        var value = await command.ExecuteScalarAsync();
        var enabled = value is null || value is DBNull || Convert.ToBoolean(value);
        AvailabilityCache[moduleNumber] = new AvailabilityCacheEntry(enabled, DateTimeOffset.UtcNow.Add(CacheLifetime));
        return enabled;
    }

    private static Guid? SessionUserId(HttpContext context, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid userId) return userId;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static bool IsViewAs(HttpContext context, Guid actualUserId, Guid effectiveUserId)
    {
        if (context.Items.TryGetValue("ProjectPulseIsViewAs", out var value)
            && value is bool isViewAs
            && isViewAs) return true;
        return actualUserId != effectiveUserId;
    }

    private static async Task WriteUnavailableAsync(HttpContext context, string moduleNumber)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new
        {
            module = moduleNumber,
            status = "module_availability_unavailable",
            message = "Module availability could not be verified."
        });
    }

    private static async Task WriteMigrationPendingAsync(HttpContext context, string moduleNumber)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new
        {
            module = moduleNumber,
            status = "module_availability_migration_pending",
            migration = MigrationFile
        });
    }

    private static IResult MigrationPending() =>
        Results.Json(new
        {
            status = "module_availability_migration_pending",
            migration = MigrationFile,
            message = "Module availability storage is not installed."
        }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult DependencyUnavailable() =>
        Results.Json(new
        {
            status = "module_availability_unavailable",
            migration = MigrationFile,
            message = "Module availability storage is unavailable."
        }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static string ModuleSortKey(string moduleNumber)
    {
        var prefix = new string(moduleNumber.TakeWhile(char.IsDigit).ToArray()).PadLeft(3, '0');
        var suffix = new string(moduleNumber.SkipWhile(char.IsDigit).ToArray()).ToUpperInvariant();
        return $"{prefix}:{suffix}";
    }

    private static ModuleDefinition Module(string number, string route, string displayName, string group) =>
        new(number, route, displayName, group);

    private static void LogFailure(HttpContext context, Exception exception, string moduleNumber, string operation)
    {
        context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("ModuleAvailabilityModule")
            .LogWarning(exception, "Module {ModuleNumber} could not {Operation}.", moduleNumber, operation);
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
            || string.IsNullOrWhiteSpace(password)) return null;

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MaxPoolSize = 10
        }.ConnectionString;
    }

    private sealed record ModuleDefinition(string ModuleNumber, string Route, string DisplayName, string Group);
    private sealed record StoredAvailability(
        bool IsEnabled,
        int Revision,
        string? Reason,
        Guid? UpdatedBy,
        DateTimeOffset UpdatedAt);
    private sealed record AccessContext(
        Guid ActualUserId,
        Guid EffectiveUserId,
        HashSet<string> ActualRoles,
        HashSet<string> EffectiveRoles,
        bool IsViewAs,
        bool CanManage);
    private sealed record AccessOutcome(AccessContext? Context, IResult? Failure);
    private sealed record AvailabilityCacheEntry(bool IsEnabled, DateTimeOffset ExpiresAt);
    public sealed record ModuleAvailabilityUpdateRequest(bool IsEnabled, int ExpectedRevision, string? Reason);
}
