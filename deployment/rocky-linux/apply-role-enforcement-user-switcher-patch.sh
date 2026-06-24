#!/usr/bin/env bash
set -euo pipefail
REPO_DIR="/opt/project-time-platform/app/project-time-platform"
API_FILE="$REPO_DIR/src/backend/ProjectTime.Api/Program.cs"
APP_FILE="$REPO_DIR/src/frontend/project-time-web/src/App.jsx"
DIST_DIR="$REPO_DIR/src/frontend/project-time-web/dist"
[ -f "$API_FILE" ] || { echo "ERROR: Missing $API_FILE"; exit 1; }
[ -f "$APP_FILE" ] || { echo "ERROR: Missing $APP_FILE"; exit 1; }
python3 - <<'PY'
from pathlib import Path
import re
p=Path('/opt/project-time-platform/app/project-time-platform/src/backend/ProjectTime.Api/Program.cs')
s=p.read_text()
s=re.sub(r'version = "0\.[0-9]+\.[0-9]+"','version = "0.5.8"',s)
s=s.replace('const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.local";','const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.com";').replace('manager@ussignal.local','ahmed.adeyemi@ussignal.com')
endpoints=r'''
app.MapGet("/api/security/dev-users", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;
    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    var users = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT u.email, u.display_name, COALESCE(u.job_title,''), COALESCE(u.department,''),
               COALESCE(array_agg(r.role_code ORDER BY r.display_order) FILTER (WHERE r.role_code IS NOT NULL AND ura.is_active = TRUE), ARRAY[]::text[]) AS role_codes,
               COALESCE(array_agg(r.role_name ORDER BY r.display_order) FILTER (WHERE r.role_name IS NOT NULL AND ura.is_active = TRUE), ARRAY[]::text[]) AS role_names
        FROM app_users u
        LEFT JOIN app_user_role_assignments ura ON ura.user_id = u.user_id AND ura.is_active = TRUE
        LEFT JOIN app_roles r ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
        WHERE u.is_active = TRUE
        GROUP BY u.email, u.display_name, u.job_title, u.department
        HAVING COUNT(r.role_code) > 0
        ORDER BY u.display_name, u.email;
        """, connection);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) users.Add(new { email=reader.GetString(0), displayName=reader.GetString(1), jobTitle=reader.GetString(2), department=reader.GetString(3), roleCodes=reader.GetFieldValue<string[]>(4), roleNames=reader.GetFieldValue<string[]>(5) });
    return Results.Ok(new { count = users.Count, users });
});

app.MapGet("/api/security/me/effective", async (string? userEmail) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;
    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    var userId = await GetEffectiveDevelopmentUserIdAsync(connection, userEmail: userEmail);
    return Results.Ok(new { selectedUserEmail = NormalizeRequestedEmail(userEmail), context = await BuildSecurityContextAsync(connection, userId) });
});
'''
if 'app.MapGet("/api/security/dev-users"' not in s:
    s=s.replace('\napp.Run();','\n'+endpoints+'app.Run();',1)
base_helpers=r'''
static string NormalizeRequestedEmail(string? userEmail)
{
    if (!string.IsNullOrWhiteSpace(userEmail)) return userEmail.Trim();
    var env = Environment.GetEnvironmentVariable("PTP_DEV_USER_EMAIL");
    return string.IsNullOrWhiteSpace(env) ? DevelopmentUserEmail : env.Trim();
}

static string DisplayNameFromEmail(string email)
{
    var local = email.Split('@')[0];
    return string.Join(' ', local.Split('.', '-', '_').Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => char.ToUpperInvariant(x[0]) + x[1..]));
}

static async Task<Guid> GetEffectiveDevelopmentUserIdAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction = null, string? userEmail = null)
{
    var email = NormalizeRequestedEmail(userEmail);
    await using (var existing = new NpgsqlCommand("SELECT user_id FROM app_users WHERE lower(email)=lower(@email) AND is_active=TRUE;", connection, transaction))
    {
        existing.Parameters.AddWithValue("email", email);
        var value = await existing.ExecuteScalarAsync();
        if (value is Guid id) return id;
    }
    await using var command = new NpgsqlCommand("""
        INSERT INTO app_users (email, display_name, job_title, department, is_active)
        VALUES (@email, @display_name, 'Development Switcher User', 'Project Time Platform', TRUE)
        ON CONFLICT (email) DO UPDATE SET is_active=TRUE, updated_at=NOW()
        RETURNING user_id;
        """, connection, transaction);
    command.Parameters.AddWithValue("email", email);
    command.Parameters.AddWithValue("display_name", DisplayNameFromEmail(email));
    return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException($"Unable to resolve development user {email}."));
}

static async Task<bool> UserHasPermissionAsync(NpgsqlConnection connection, Guid userId, string permissionCode, NpgsqlTransaction? transaction = null)
{
    await using var command = new NpgsqlCommand("""
        SELECT EXISTS (
            SELECT 1 FROM app_user_role_assignments ura
            JOIN app_roles r ON r.app_role_id=ura.app_role_id
            JOIN app_role_permissions rp ON rp.app_role_id=r.app_role_id
            JOIN app_permissions p ON p.app_permission_id=rp.app_permission_id
            WHERE ura.user_id=@user_id AND ura.is_active=TRUE AND r.is_active=TRUE
              AND (p.permission_code=@permission_code OR p.permission_code='MANAGE_ALL')
        );
        """, connection, transaction);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("permission_code", permissionCode);
    return (bool)(await command.ExecuteScalarAsync() ?? false);
}

static async Task<IResult?> RequirePermissionAsync(NpgsqlConnection connection, Guid userId, string permissionCode, NpgsqlTransaction? transaction = null)
{
    if (await UserHasPermissionAsync(connection, userId, permissionCode, transaction)) return null;
    return Results.Json(new { status="forbidden", requiredPermission=permissionCode, message=$"The selected user does not have {permissionCode}." }, statusCode: StatusCodes.Status403Forbidden);
}
'''
ctx_helper=r'''
static async Task<object> BuildSecurityContextAsync(NpgsqlConnection connection, Guid userId)
{
    string? email=null; string? displayName=null;
    await using (var uc = new NpgsqlCommand("SELECT email, display_name FROM app_users WHERE user_id=@user_id;", connection))
    {
        uc.Parameters.AddWithValue("user_id", userId);
        await using var r = await uc.ExecuteReaderAsync();
        if (await r.ReadAsync()) { email=r.GetString(0); displayName=r.GetString(1); }
    }
    var roles=new List<object>();
    await using (var rc = new NpgsqlCommand("""
        SELECT r.role_code, r.role_name, r.role_description
        FROM app_user_role_assignments ura JOIN app_roles r ON r.app_role_id=ura.app_role_id
        WHERE ura.user_id=@user_id AND ura.is_active=TRUE AND r.is_active=TRUE
        ORDER BY r.display_order;
        """, connection))
    {
        rc.Parameters.AddWithValue("user_id", userId);
        await using var r = await rc.ExecuteReaderAsync();
        while (await r.ReadAsync()) roles.Add(new { roleCode=r.GetString(0), roleName=r.GetString(1), description=r.IsDBNull(2)?null:r.GetString(2) });
    }
    var permissions=new List<string>();
    await using (var pc = new NpgsqlCommand("""
        SELECT DISTINCT p.permission_code
        FROM app_user_role_assignments ura
        JOIN app_roles r ON r.app_role_id=ura.app_role_id
        JOIN app_role_permissions rp ON rp.app_role_id=r.app_role_id
        JOIN app_permissions p ON p.app_permission_id=rp.app_permission_id
        WHERE ura.user_id=@user_id AND ura.is_active=TRUE AND r.is_active=TRUE
        ORDER BY p.permission_code;
        """, connection))
    {
        pc.Parameters.AddWithValue("user_id", userId);
        await using var r = await pc.ExecuteReaderAsync();
        while (await r.ReadAsync()) permissions.Add(r.GetString(0));
    }
    var features=new List<object>();
    await using (var fc = new NpgsqlCommand("""
        SELECT feature_code, feature_name, module_code, route_anchor, required_permission_code, feature_description
        FROM app_feature_catalog
        WHERE is_active=TRUE AND (required_permission_code IS NULL OR required_permission_code=ANY(@permissions) OR 'MANAGE_ALL'=ANY(@permissions))
        ORDER BY display_order;
        """, connection))
    {
        fc.Parameters.AddWithValue("permissions", permissions.ToArray());
        await using var r = await fc.ExecuteReaderAsync();
        while (await r.ReadAsync()) features.Add(new { featureCode=r.GetString(0), featureName=r.GetString(1), moduleCode=r.GetString(2), routeAnchor=r.IsDBNull(3)?null:r.GetString(3), requiredPermissionCode=r.IsDBNull(4)?null:r.GetString(4), description=r.IsDBNull(5)?null:r.GetString(5) });
    }
    return new { userId, email, displayName, roles, permissions, features };
}
'''
if 'static string NormalizeRequestedEmail' not in s:
    s=s.replace('static IResult? ValidateConfig(DatabaseConfig config)', base_helpers+'\nstatic IResult? ValidateConfig(DatabaseConfig config)',1)
if 'static async Task<object> BuildSecurityContextAsync' not in s:
    s=s.replace('static IResult? ValidateConfig(DatabaseConfig config)', ctx_helper+'\nstatic IResult? ValidateConfig(DatabaseConfig config)',1)
s=s.replace('app.MapGet("/api/timesheets/week", async (DateOnly? weekStart) =>','app.MapGet("/api/timesheets/week", async (DateOnly? weekStart, string? userEmail) =>')
s=s.replace('app.MapPost("/api/timesheets/week/draft", async (TimesheetSaveRequest request) =>','app.MapPost("/api/timesheets/week/draft", async (TimesheetSaveRequest request, string? userEmail) =>')
s=s.replace('app.MapPost("/api/timesheets/week/submit", async (TimesheetSaveRequest request) =>','app.MapPost("/api/timesheets/week/submit", async (TimesheetSaveRequest request, string? userEmail) =>')
s=s.replace('''    var userId = await GetOrCreateDevelopmentUserIdAsync(connection);
    var payload = await BuildTimesheetWeekPayloadAsync(connection, userId, start);''','''    var userId = await GetEffectiveDevelopmentUserIdAsync(connection, userEmail: userEmail);
    var permissionResult = await RequirePermissionAsync(connection, userId, "VIEW_TIME_ENTRY");
    if (permissionResult is not null) return permissionResult;
    var payload = await BuildTimesheetWeekPayloadAsync(connection, userId, start);''')
s=s.replace('''        var userId = await GetOrCreateDevelopmentUserIdAsync(connection, transaction);
        var start = GetSundayForDate(request.WeekStart);''','''        var userId = await GetEffectiveDevelopmentUserIdAsync(connection, transaction, userEmail);
        var permissionResult = await RequirePermissionAsync(connection, userId, "EDIT_OWN_TIME", transaction);
        if (permissionResult is not null) return permissionResult;
        var start = GetSundayForDate(request.WeekStart);''',1)
s=s.replace('''        var userId = await GetOrCreateDevelopmentUserIdAsync(connection, transaction);
        var start = GetSundayForDate(request.WeekStart);''','''        var userId = await GetEffectiveDevelopmentUserIdAsync(connection, transaction, userEmail);
        var permissionResult = await RequirePermissionAsync(connection, userId, "SUBMIT_OWN_TIME", transaction);
        if (permissionResult is not null) return permissionResult;
        var start = GetSundayForDate(request.WeekStart);''',1)
p.write_text(s)
PY
python3 - <<'PY'
from pathlib import Path
p=Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/App.jsx')
s=p.read_text()
helpers=r'''
function getInitialUserEmail() {
  return window.localStorage.getItem('ptp-dev-user-email') || 'ahmed.adeyemi@ussignal.com';
}
function addUserEmail(path, userEmail) {
  const separator = path.includes('?') ? '&' : '?';
  return `${path}${separator}userEmail=${encodeURIComponent(userEmail)}`;
}
function UserSwitcher({ users, selectedUserEmail, onChange, securityContext }) {
  const user = users.find((item) => item.email === selectedUserEmail);
  const roles = user?.roleNames?.length ? user.roleNames.join(', ') : 'No active role loaded';
  return (
    <div className="user-switcher" aria-label="Development user switcher">
      <label htmlFor="development-user-switcher">Active role</label>
      <select id="development-user-switcher" value={selectedUserEmail} onChange={(event) => onChange(event.target.value)}>
        {users.map((item) => <option value={item.email} key={item.email}>{item.displayName} — {(item.roleNames ?? []).join(', ')}</option>)}
      </select>
      <small>{roles} • {(securityContext?.permissions ?? []).length} permissions</small>
    </div>
  );
}
'''
if 'function getInitialUserEmail()' not in s:
    s=s.replace('function getInitialTheme() {',helpers+'\nfunction getInitialTheme() {',1)
if 'const [selectedUserEmail, setSelectedUserEmail]' not in s:
    s=s.replace("  const [activitySource, setActivitySource] = useState('nonProject');", "  const [activitySource, setActivitySource] = useState('nonProject');\n  const [selectedUserEmail, setSelectedUserEmail] = useState(getInitialUserEmail);\n  const [securityContext, setSecurityContext] = useState({ loading: true, data: null, error: null });\n  const [devUsers, setDevUsers] = useState({ loading: true, data: [], error: null });",1)
effect=r'''
  useEffect(() => {
    window.localStorage.setItem('ptp-dev-user-email', selectedUserEmail);
  }, [selectedUserEmail]);

  useEffect(() => {
    let cancelled = false;
    async function loadSecurityContext() {
      setSecurityContext({ loading: true, data: null, error: null });
      try {
        const [usersResult, contextResult] = await Promise.all([
          fetchJson('/api/security/dev-users'),
          fetchJson(addUserEmail('/api/security/me/effective', selectedUserEmail))
        ]);
        if (!cancelled) {
          setDevUsers({ loading: false, data: usersResult.users ?? [], error: null });
          setSecurityContext({ loading: false, data: contextResult.context, error: null });
        }
      } catch (error) {
        if (!cancelled) {
          const message = error instanceof Error ? error.message : 'Security context failed to load';
          setDevUsers((current) => ({ ...current, loading: false, error: message }));
          setSecurityContext({ loading: false, data: null, error: message });
        }
      }
    }
    loadSecurityContext();
    return () => { cancelled = true; };
  }, [selectedUserEmail]);
'''
if 'async function loadSecurityContext()' not in s:
    s=s.replace('  useEffect(() => {\n    let cancelled = false;\n\n    async function loadStatus() {',effect+'\n  useEffect(() => {\n    let cancelled = false;\n\n    async function loadStatus() {',1)
s=s.replace('fetchJson(`/api/timesheets/week?weekStart=${selectedWeekStart}`)','fetchJson(addUserEmail(`/api/timesheets/week?weekStart=${selectedWeekStart}`, selectedUserEmail))')
s=s.replace("postJson('/api/timesheets/week/draft', payload)", "postJson(addUserEmail('/api/timesheets/week/draft', selectedUserEmail), payload)")
s=s.replace("postJson('/api/timesheets/week/draft', buildTimesheetPayload())", "postJson(addUserEmail('/api/timesheets/week/draft', selectedUserEmail), buildTimesheetPayload())")
s=s.replace("postJson('/api/timesheets/week/submit', buildTimesheetPayload())", "postJson(addUserEmail('/api/timesheets/week/submit', selectedUserEmail), buildTimesheetPayload())")
s=s.replace("postJson('/api/timesheets/day/submit', {", "postJson(addUserEmail('/api/timesheets/day/submit', selectedUserEmail), {")
s=s.replace("postJson('/api/timesheets/day/unlock', {", "postJson(addUserEmail('/api/timesheets/day/unlock', selectedUserEmail), {")
s=s.replace('  }, [selectedWeekStart]);','  }, [selectedWeekStart, selectedUserEmail]);',1)
if 'const effectiveSecurityContext = securityContext.data;' not in s:
    s=s.replace("  const currentTimesheetStatus = timesheet.data?.status ?? 'draft';\n  const isAnyDayEditable = days.length === 0 || days.some((day) => isDayEditable(day.date));", "  const currentTimesheetStatus = timesheet.data?.status ?? 'draft';\n  const effectiveSecurityContext = securityContext.data;\n  const effectivePermissions = effectiveSecurityContext?.permissions ?? [];\n  const canEditOwnTime = effectivePermissions.includes('MANAGE_ALL') || effectivePermissions.includes('EDIT_OWN_TIME');\n  const canSubmitOwnTime = effectivePermissions.includes('MANAGE_ALL') || effectivePermissions.includes('SUBMIT_OWN_TIME');\n  const isAnyDayEditable = canEditOwnTime && (days.length === 0 || days.some((day) => isDayEditable(day.date)));",1)
s=s.replace('return getDayStatus(workDate).canEdit !== false;','return canEditOwnTime && getDayStatus(workDate).canEdit !== false;')
s=s.replace('if (!selectedCell || isSaving) return;','if (!selectedCell || isSaving || !canSubmitOwnTime) return;',1)
s=s.replace('disabled={isSaving || getSelectedDayTotal() < 8}','disabled={isSaving || !canSubmitOwnTime || getSelectedDayTotal() < 8}')
if '<UserSwitcher' not in s:
    s=s.replace('''        </nav>
        <button className="theme-toggle" type="button" onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}>''','''        </nav>
        <UserSwitcher
          users={devUsers.data.length > 0 ? devUsers.data : [{ email: selectedUserEmail, displayName: selectedUserEmail, roleNames: [] }]}
          selectedUserEmail={selectedUserEmail}
          onChange={setSelectedUserEmail}
          securityContext={effectiveSecurityContext}
        />
        <button className="theme-toggle" type="button" onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}>''',1)
if 'Role enforcement is active' not in s:
    s=s.replace('''        <p className="hero-copy">
          A focused internal platform for weekly time entry, task-based project assignment, manager approval, project validation, accounting reconciliation, and utilization reporting.
        </p>''','''        <p className="hero-copy">
          A focused internal platform for weekly time entry, task-based project assignment, manager approval, project validation, accounting reconciliation, and utilization reporting.
        </p>
        <p className="muted small-text">Role enforcement is active. The user switcher changes the API security context without requiring a browser restart.</p>''',1)
p.write_text(s)
PY
[ -d "$DIST_DIR" ] && { echo "==> Removing stale frontend dist"; rm -rf "$DIST_DIR"; }
echo "==> Role enforcement and user switcher patch applied"
echo "==> Expected API version after redeploy: 0.5.8"
