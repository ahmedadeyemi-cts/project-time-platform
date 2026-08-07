using Npgsql;

namespace ProjectTime.Api.Modules;

internal sealed record EnterpriseGovernanceAccess(
    Guid ActualUserId,
    Guid EffectiveUserId,
    string DisplayName,
    string Email,
    string TeamName,
    bool IsViewAs,
    bool IsBroadScope,
    bool CanManageOrganization,
    bool CanManageProjects,
    bool CanManageTeam,
    bool CanUpdateAssignedActions,
    IReadOnlySet<string> Roles,
    IReadOnlySet<string> Permissions)
{
    internal bool CanViewLabEquipment => IsBroadScope
        || Roles.Overlaps(EnterpriseGovernanceAccessResolver.LabViewRoles)
        || Permissions.Contains("VIEW_LAB_EQUIPMENT_081");

    internal bool CanManageLabEquipment => !IsViewAs && (CanManageOrganization
        || CanManageTeam
        || Permissions.Contains("MANAGE_LAB_EQUIPMENT_081"));

    internal bool CanImportLabEquipment => !IsViewAs && (CanManageOrganization
        || Permissions.Contains("IMPORT_LAB_EQUIPMENT_081"));

    internal bool CanViewRiskRegister => IsBroadScope
        || Roles.Overlaps(EnterpriseGovernanceAccessResolver.RiskViewRoles)
        || Permissions.Contains("VIEW_PROJECT_RISKS_082");

    internal bool CanManageRiskRegister => !IsViewAs && (CanManageOrganization
        || CanManageProjects
        || CanManageTeam
        || Permissions.Contains("MANAGE_PROJECT_RISKS_082"));

    internal bool CanExport(string moduleCode) => !IsViewAs && moduleCode switch
    {
        "081" => CanViewLabEquipment && (CanManageLabEquipment || Permissions.Contains("EXPORT_LAB_EQUIPMENT_081")),
        "082" => CanViewRiskRegister && (CanManageRiskRegister || Permissions.Contains("EXPORT_PROJECT_RISKS_082")),
        _ => false
    };
}

internal static class EnterpriseGovernanceAccessResolver
{
    internal static readonly HashSet<string> LabViewRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "PROJECT_TEAM_COORDINATOR",
        "MANAGER", "ENGINEERING_MANAGER", "ENGINEERING_LEAD", "ENGINEERING_TEAM_LEAD",
        "ENGINEER", "ENGINEERING", "SYSTEMS_ENGINEER", "NETWORK_ENGINEER",
        "ENTERPRISE_NETWORK_ENGINEER", "PROJECT_MANAGER", "PROJECT_MANAGEMENT",
        "SOLUTION_ARCHITECT", "SA", "SAA"
    };

    internal static readonly HashSet<string> RiskViewRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "PROJECT_TEAM_COORDINATOR", "PROJECT_COORDINATOR",
        "PROJECT_MANAGER", "PROJECT_MANAGEMENT", "PROJECT_MANAGEMENT_LEAD",
        "PROJECT_MANAGEMENT_TEAM_LEAD", "PM_TEAM_LEAD", "MANAGER", "PEOPLE_MANAGER",
        "ENGINEERING_MANAGER", "ENGINEERING_LEAD", "ENGINEERING_TEAM_LEAD", "ENGINEER",
        "ENGINEERING", "SOLUTION_ARCHITECT", "SA", "SAA", "ACCOUNT_EXECUTIVE",
        "EXECUTIVE", "EXECUTIVE_LEADERSHIP", "ACCOUNTING", "SALES"
    };

    private static readonly HashSet<string> BroadRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "SYSTEM_ADMINISTRATOR", "PROJECT_TEAM_COORDINATOR"
    };

    private static readonly HashSet<string> ProjectManageRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "PROJECT_MANAGER", "PROJECT_MANAGEMENT", "PROJECT_MANAGEMENT_LEAD",
        "PROJECT_MANAGEMENT_TEAM_LEAD", "PM_TEAM_LEAD", "PROJECT_COORDINATOR"
    };

    private static readonly HashSet<string> TeamManageRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "MANAGER", "PEOPLE_MANAGER", "ENGINEERING_MANAGER", "ENGINEERING_LEAD", "ENGINEERING_TEAM_LEAD",
        "PROJECT_MANAGEMENT_LEAD", "PROJECT_MANAGEMENT_TEAM_LEAD", "PM_TEAM_LEAD"
    };

    internal static async Task<EnterpriseGovernanceAccess?> ResolveAsync(
        HttpContext context,
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var actualUserId = ProjectPulseActualSessionAuthority.ReadUserId(
            context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        var effectiveUserId = ProjectPulseActualSessionAuthority.ReadUserId(
            context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId");
        if (!actualUserId.HasValue || !effectiveUserId.HasValue) return null;

        var isViewAs = ProjectPulseActualSessionAuthority.IsViewAs(context);
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string displayName = string.Empty, email = string.Empty, teamName = string.Empty;

        await using (var command = new NpgsqlCommand("""
            SELECT COALESCE(NULLIF(app_user.display_name, ''), app_user.email),
                   app_user.email,
                   COALESCE(app_user.team_name, ''),
                   COALESCE(string_agg(DISTINCT upper(role.role_code), ','), ''),
                   COALESCE(string_agg(DISTINCT upper(permission.permission_code), ','), '')
            FROM app_users app_user
            LEFT JOIN app_user_role_assignments assignment
              ON assignment.user_id=app_user.user_id AND assignment.is_active=TRUE
            LEFT JOIN app_roles role
              ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
            LEFT JOIN app_role_permissions role_permission
              ON role_permission.app_role_id=role.app_role_id
            LEFT JOIN app_permissions permission
              ON permission.app_permission_id=role_permission.app_permission_id
            WHERE app_user.user_id=@user_id AND app_user.is_active=TRUE
            GROUP BY app_user.user_id,app_user.display_name,app_user.email,app_user.team_name;
            """, connection))
        {
            command.Parameters.AddWithValue("user_id", effectiveUserId.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            displayName = reader.GetString(0);
            email = reader.GetString(1);
            teamName = reader.GetString(2);
            foreach (var role in reader.GetString(3).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                roles.Add(role);
            foreach (var permission in reader.GetString(4).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                permissions.Add(permission);
        }

        var actualSuperAdministrator = !isViewAs && await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(
            context, connection, cancellationToken: cancellationToken);
        var broad = !isViewAs && (actualSuperAdministrator || roles.Overlaps(BroadRoles));

        return new EnterpriseGovernanceAccess(
            actualUserId.Value,
            effectiveUserId.Value,
            displayName,
            email,
            teamName,
            isViewAs,
            broad,
            broad,
            !isViewAs && (broad || roles.Overlaps(ProjectManageRoles)),
            !isViewAs && (broad || roles.Overlaps(TeamManageRoles)),
            !isViewAs && (roles.Contains("ENGINEER") || roles.Contains("ENGINEERING") || roles.Contains("SOLUTION_ARCHITECT")),
            roles,
            permissions);
    }

    internal static async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("The Pulse database connection is not configured.");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    internal static void AddScopeParameters(NpgsqlCommand command, EnterpriseGovernanceAccess access)
    {
        command.Parameters.AddWithValue("user_id", access.EffectiveUserId);
        command.Parameters.AddWithValue("broad_scope", access.IsBroadScope);
        command.Parameters.AddWithValue("team_scope", access.CanManageTeam);
        command.Parameters.AddWithValue("team_name", access.TeamName ?? string.Empty);
    }

    internal const string TeamMembersCte = """
        scoped_team_members AS (
            SELECT DISTINCT member.user_id
            FROM app_users member
            WHERE member.is_active=TRUE
              AND (
                    (COALESCE(@team_name,'')<>'' AND lower(COALESCE(member.team_name,''))=lower(@team_name))
                 OR EXISTS (
                      SELECT 1 FROM reporting_relationships relationship
                      WHERE relationship.employee_user_id=member.user_id
                        AND (relationship.manager_user_id=@user_id OR relationship.team_lead_user_id=@user_id)
                        AND relationship.effective_start_date<=CURRENT_DATE
                        AND (relationship.effective_end_date IS NULL OR relationship.effective_end_date>=CURRENT_DATE)
                 )
                 OR EXISTS (
                      SELECT 1 FROM projectpulse_team_scope_assignments scope_assignment
                      WHERE scope_assignment.scoped_user_id=@user_id
                        AND scope_assignment.is_active=TRUE
                        AND scope_assignment.team_name IS NOT NULL
                        AND lower(COALESCE(member.team_name,''))=lower(scope_assignment.team_name)
                 )
              )
        )
        """;

    internal const string ProjectScopePredicate = """
        (
            @broad_scope=TRUE
            OR project.project_manager_user_id=@user_id
            OR EXISTS (
                SELECT 1 FROM project_assignments self_assignment
                WHERE self_assignment.project_id=project.project_id
                  AND self_assignment.user_id=@user_id
                  AND (self_assignment.effective_end_date IS NULL OR self_assignment.effective_end_date>=CURRENT_DATE)
            )
            OR (
                @team_scope=TRUE
                AND (
                    project.project_manager_user_id IN (SELECT user_id FROM scoped_team_members)
                    OR EXISTS (
                        SELECT 1 FROM project_assignments team_assignment
                        WHERE team_assignment.project_id=project.project_id
                          AND team_assignment.user_id IN (SELECT user_id FROM scoped_team_members)
                          AND (team_assignment.effective_end_date IS NULL OR team_assignment.effective_end_date>=CURRENT_DATE)
                    )
                )
            )
        )
        """;

    internal static string BuildConnectionString()
    {
        foreach (var name in new[]
                 {
                     "ConnectionStrings__DefaultConnection", "ConnectionStrings__ProjectPulse",
                     "ConnectionStrings__ProjectTime", "PROJECTPULSE_CONNECTION_STRING",
                     "PROJECTTIME_DATABASE_CONNECTION"
                 })
        {
            var configured = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
        }

        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return string.Empty;
        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            Pooling = true,
            IncludeErrorDetail = false,
            MaxPoolSize = 20
        }.ConnectionString;
    }
}

internal static class EnterpriseGovernanceResults
{
    internal static IResult Unavailable(string module, Exception exception, HttpContext context, string operation)
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger($"Module{module}EnterpriseGovernance")
            .LogWarning(exception, "Module {Module} could not {Operation}.", module, operation);
        return Results.Json(new
        {
            module,
            code = $"MODULE_{module}_DEPENDENCY_UNAVAILABLE",
            message = "The governed data service is temporarily unavailable. No changes were made."
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    internal static IResult Unauthorized(string module) => Results.Json(new
    {
        module,
        code = "AUTHENTICATION_REQUIRED",
        message = "Sign in to access this Pulse module."
    }, statusCode: StatusCodes.Status401Unauthorized);

    internal static IResult Forbidden(string module, string message) => Results.Json(new
    {
        module,
        code = $"MODULE_{module}_ACCESS_DENIED",
        message
    }, statusCode: StatusCodes.Status403Forbidden);

    internal static IResult ViewAsReadOnly(string module) => Results.Json(new
    {
        module,
        code = "VIEW_AS_READ_ONLY",
        message = "View-As is a read-only preview. Exit View-As to change data or create an export."
    }, statusCode: StatusCodes.Status403Forbidden);
}
