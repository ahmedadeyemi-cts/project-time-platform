from pathlib import Path
import re

api_file = Path('/opt/project-time-platform/app/project-time-platform/src/backend/ProjectTime.Api/Program.cs')
api = api_file.read_text()
api = re.sub(r'version = "0\.[0-9]+\.[0-9]+"', 'version = "0.5.8"', api)
api = api.replace('const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.local";', 'const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.com";')
api = api.replace('manager@ussignal.local', 'ahmed.adeyemi@ussignal.com')

def block(lines):
    return "\n".join(lines) + "\n"

security_endpoints = block([
'app.MapGet("/api/security/dev-users", async () =>',
'{',
'    var config = DatabaseConfig.FromEnvironment();',
'    var missingResult = ValidateConfig(config);',
'    if (missingResult is not null) return missingResult;',
'    await using var connection = new NpgsqlConnection(config.ConnectionString);',
'    await connection.OpenAsync();',
'    var users = new List<object>();',
'    await using var command = new NpgsqlCommand("""',
"        SELECT u.email, u.display_name, COALESCE(u.job_title,''), COALESCE(u.department,''),",
'               COALESCE(array_agg(r.role_code ORDER BY r.display_order) FILTER (WHERE r.role_code IS NOT NULL AND ura.is_active = TRUE), ARRAY[]::text[]) AS role_codes,',
'               COALESCE(array_agg(r.role_name ORDER BY r.display_order) FILTER (WHERE r.role_name IS NOT NULL AND ura.is_active = TRUE), ARRAY[]::text[]) AS role_names',
'        FROM app_users u',
'        LEFT JOIN app_user_role_assignments ura ON ura.user_id = u.user_id AND ura.is_active = TRUE',
'        LEFT JOIN app_roles r ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE',
'        WHERE u.is_active = TRUE',
'        GROUP BY u.email, u.display_name, u.job_title, u.department',
'        HAVING COUNT(r.role_code) > 0',
'        ORDER BY u.display_name, u.email;',
'        """, connection);',
'    await using var reader = await command.ExecuteReaderAsync();',
'    while (await reader.ReadAsync())',
'    {',
'        users.Add(new { email = reader.GetString(0), displayName = reader.GetString(1), jobTitle = reader.GetString(2), department = reader.GetString(3), roleCodes = reader.GetFieldValue<string[]>(4), roleNames = reader.GetFieldValue<string[]>(5) });',
'    }',
'    return Results.Ok(new { count = users.Count, users });',
'});',
'',
'app.MapGet("/api/security/me/effective", async (string? userEmail) =>',
'{',
'    var config = DatabaseConfig.FromEnvironment();',
'    var missingResult = ValidateConfig(config);',
'    if (missingResult is not null) return missingResult;',
'    await using var connection = new NpgsqlConnection(config.ConnectionString);',
'    await connection.OpenAsync();',
'    var userId = await GetEffectiveDevelopmentUserIdAsync(connection, userEmail: userEmail);',
'    return Results.Ok(new { selectedUserEmail = NormalizeRequestedEmail(userEmail), context = await BuildSecurityContextAsync(connection, userId) });',
'});',
'',
])

base_helpers = block([
'static string NormalizeRequestedEmail(string? userEmail)',
'{',
'    if (!string.IsNullOrWhiteSpace(userEmail)) return userEmail.Trim();',
'    var env = Environment.GetEnvironmentVariable("PTP_DEV_USER_EMAIL");',
'    return string.IsNullOrWhiteSpace(env) ? DevelopmentUserEmail : env.Trim();',
'}',
'',
'static string DisplayNameFromEmail(string email)',
'{',
"    var local = email.Split('@')[0];",
"    return string.Join(' ', local.Split('.', '-', '_').Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => char.ToUpperInvariant(x[0]) + x[1..]));",
'}',
'',
'static async Task<Guid> GetEffectiveDevelopmentUserIdAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction = null, string? userEmail = null)',
'{',
'    var email = NormalizeRequestedEmail(userEmail);',
'    await using (var existing = new NpgsqlCommand("SELECT user_id FROM app_users WHERE lower(email)=lower(@email) AND is_active=TRUE;", connection, transaction))',
'    {',
'        existing.Parameters.AddWithValue("email", email);',
'        var value = await existing.ExecuteScalarAsync();',
'        if (value is Guid id) return id;',
'    }',
'    await using var command = new NpgsqlCommand("""',
'        INSERT INTO app_users (email, display_name, job_title, department, is_active)',
"        VALUES (@email, @display_name, 'Development Switcher User', 'Project Time Platform', TRUE)",
'        ON CONFLICT (email) DO UPDATE SET is_active=TRUE, updated_at=NOW()',
'        RETURNING user_id;',
'        """, connection, transaction);',
'    command.Parameters.AddWithValue("email", email);',
'    command.Parameters.AddWithValue("display_name", DisplayNameFromEmail(email));',
'    return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException($"Unable to resolve development user {email}."));',
'}',
'',
'static async Task<bool> UserHasPermissionAsync(NpgsqlConnection connection, Guid userId, string permissionCode, NpgsqlTransaction? transaction = null)',
'{',
'    await using var command = new NpgsqlCommand("""',
'        SELECT EXISTS (',
'            SELECT 1 FROM app_user_role_assignments ura',
'            JOIN app_roles r ON r.app_role_id=ura.app_role_id',
'            JOIN app_role_permissions rp ON rp.app_role_id=r.app_role_id',
'            JOIN app_permissions p ON p.app_permission_id=rp.app_permission_id',
'            WHERE ura.user_id=@user_id AND ura.is_active=TRUE AND r.is_active=TRUE',
"              AND (p.permission_code=@permission_code OR p.permission_code='MANAGE_ALL')",
'        );',
'        """, connection, transaction);',
'    command.Parameters.AddWithValue("user_id", userId);',
'    command.Parameters.AddWithValue("permission_code", permissionCode);',
'    return (bool)(await command.ExecuteScalarAsync() ?? false);',
'}',
'',
'static async Task<IResult?> RequirePermissionAsync(NpgsqlConnection connection, Guid userId, string permissionCode, NpgsqlTransaction? transaction = null)',
'{',
'    if (await UserHasPermissionAsync(connection, userId, permissionCode, transaction)) return null;',
'    return Results.Json(new { status = "forbidden", requiredPermission = permissionCode, message = $"The selected user does not have {permissionCode}." }, statusCode: StatusCodes.Status403Forbidden);',
'}',
'',
])

ctx_helper = block([
'static async Task<object> BuildSecurityContextAsync(NpgsqlConnection connection, Guid userId)',
'{',
'    string? email = null; string? displayName = null;',
'    await using (var uc = new NpgsqlCommand("SELECT email, display_name FROM app_users WHERE user_id=@user_id;", connection))',
'    {',
'        uc.Parameters.AddWithValue("user_id", userId);',
'        await using var r = await uc.ExecuteReaderAsync();',
'        if (await r.ReadAsync()) { email = r.GetString(0); displayName = r.GetString(1); }',
'    }',
'    var roles = new List<object>();',
'    await using (var rc = new NpgsqlCommand("""',
'        SELECT r.role_code, r.role_name, r.role_description',
'        FROM app_user_role_assignments ura JOIN app_roles r ON r.app_role_id=ura.app_role_id',
'        WHERE ura.user_id=@user_id AND ura.is_active=TRUE AND r.is_active=TRUE',
'        ORDER BY r.display_order;',
'        """, connection))',
'    {',
'        rc.Parameters.AddWithValue("user_id", userId);',
'        await using var r = await rc.ExecuteReaderAsync();',
'        while (await r.ReadAsync()) roles.Add(new { roleCode = r.GetString(0), roleName = r.GetString(1), description = r.IsDBNull(2) ? null : r.GetString(2) });',
'    }',
'    var permissions = new List<string>();',
'    await using (var pc = new NpgsqlCommand("""',
'        SELECT DISTINCT p.permission_code',
'        FROM app_user_role_assignments ura',
'        JOIN app_roles r ON r.app_role_id=ura.app_role_id',
'        JOIN app_role_permissions rp ON rp.app_role_id=r.app_role_id',
'        JOIN app_permissions p ON p.app_permission_id=rp.app_permission_id',
'        WHERE ura.user_id=@user_id AND ura.is_active=TRUE AND r.is_active=TRUE',
'        ORDER BY p.permission_code;',
'        """, connection))',
'    {',
'        pc.Parameters.AddWithValue("user_id", userId);',
'        await using var r = await pc.ExecuteReaderAsync();',
'        while (await r.ReadAsync()) permissions.Add(r.GetString(0));',
'    }',
'    var features = new List<object>();',
'    await using (var fc = new NpgsqlCommand("""',
'        SELECT feature_code, feature_name, module_code, route_anchor, required_permission_code, feature_description',
'        FROM app_feature_catalog',
"        WHERE is_active=TRUE AND (required_permission_code IS NULL OR required_permission_code=ANY(@permissions) OR 'MANAGE_ALL'=ANY(@permissions))",
'        ORDER BY display_order;',
'        """, connection))',
'    {',
'        fc.Parameters.AddWithValue("permissions", permissions.ToArray());',
'        await using var r = await fc.ExecuteReaderAsync();',
'        while (await r.ReadAsync()) features.Add(new { featureCode = r.GetString(0), featureName = r.GetString(1), moduleCode = r.GetString(2), routeAnchor = r.IsDBNull(3) ? null : r.GetString(3), requiredPermissionCode = r.IsDBNull(4) ? null : r.GetString(4), description = r.IsDBNull(5) ? null : r.GetString(5) });',
'    }',
'    return new { userId, email, displayName, roles, permissions, features };',
'}',
'',
])

if 'app.MapGet("/api/security/dev-users"' not in api:
    api = api.replace('\napp.Run();', '\n' + security_endpoints + 'app.Run();', 1)
if 'static string NormalizeRequestedEmail' not in api:
    api = api.replace('static IResult? ValidateConfig(DatabaseConfig config)', base_helpers + 'static IResult? ValidateConfig(DatabaseConfig config)', 1)
if 'static async Task<object> BuildSecurityContextAsync' not in api:
    api = api.replace('static IResult? ValidateConfig(DatabaseConfig config)', ctx_helper + 'static IResult? ValidateConfig(DatabaseConfig config)', 1)

api = api.replace('app.MapGet("/api/timesheets/week", async (DateOnly? weekStart) =>', 'app.MapGet("/api/timesheets/week", async (DateOnly? weekStart, string? userEmail) =>')
api = api.replace('app.MapPost("/api/timesheets/week/draft", async (TimesheetSaveRequest request) =>', 'app.MapPost("/api/timesheets/week/draft", async (TimesheetSaveRequest request, string? userEmail) =>')
api = api.replace('app.MapPost("/api/timesheets/week/submit", async (TimesheetSaveRequest request) =>', 'app.MapPost("/api/timesheets/week/submit", async (TimesheetSaveRequest request, string? userEmail) =>')
api = api.replace('''    var userId = await GetOrCreateDevelopmentUserIdAsync(connection);
    var payload = await BuildTimesheetWeekPayloadAsync(connection, userId, start);''', '''    var userId = await GetEffectiveDevelopmentUserIdAsync(connection, userEmail: userEmail);
    var permissionResult = await RequirePermissionAsync(connection, userId, "VIEW_TIME_ENTRY");
    if (permissionResult is not null) return permissionResult;
    var payload = await BuildTimesheetWeekPayloadAsync(connection, userId, start);''')
api = api.replace('''        var userId = await GetOrCreateDevelopmentUserIdAsync(connection, transaction);
        var start = GetSundayForDate(request.WeekStart);''', '''        var userId = await GetEffectiveDevelopmentUserIdAsync(connection, transaction, userEmail);
        var permissionResult = await RequirePermissionAsync(connection, userId, "EDIT_OWN_TIME", transaction);
        if (permissionResult is not null) return permissionResult;
        var start = GetSundayForDate(request.WeekStart);''', 1)
api = api.replace('''        var userId = await GetOrCreateDevelopmentUserIdAsync(connection, transaction);
        var start = GetSundayForDate(request.WeekStart);''', '''        var userId = await GetEffectiveDevelopmentUserIdAsync(connection, transaction, userEmail);
        var permissionResult = await RequirePermissionAsync(connection, userId, "SUBMIT_OWN_TIME", transaction);
        if (permissionResult is not null) return permissionResult;
        var start = GetSundayForDate(request.WeekStart);''', 1)

api_file.write_text(api)
