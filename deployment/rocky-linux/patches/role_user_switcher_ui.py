from pathlib import Path

app_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/App.jsx')
app = app_file.read_text()

def block(lines):
    return "\n".join(lines) + "\n"

ui_helpers = block([
'function getInitialUserEmail() {',
"  return window.localStorage.getItem('ptp-dev-user-email') || 'ahmed.adeyemi@ussignal.com';",
'}',
'',
'function addUserEmail(path, userEmail) {',
"  const separator = path.includes('?') ? '&' : '?';",
"  return `${path}${separator}userEmail=${encodeURIComponent(userEmail)}`;",
'}',
'',
'function UserSwitcher({ users, selectedUserEmail, onChange, securityContext }) {',
'  const user = users.find((item) => item.email === selectedUserEmail);',
"  const roles = user?.roleNames?.length ? user.roleNames.join(', ') : 'No active role loaded';",
'  return (',
'    <div className="user-switcher" aria-label="Development user switcher">',
'      <label htmlFor="development-user-switcher">Active role</label>',
'      <select id="development-user-switcher" value={selectedUserEmail} onChange={(event) => onChange(event.target.value)}>',
'        {users.map((item) => (',
"          <option value={item.email} key={item.email}>{item.displayName} — {(item.roleNames ?? []).join(', ')}</option>",
'        ))}',
'      </select>',
"      <small>{roles} • {(securityContext?.permissions ?? []).length} permissions</small>",
'    </div>',
'  );',
'}',
'',
])

if 'function getInitialUserEmail()' not in app:
    app = app.replace('function getInitialTheme() {', ui_helpers + 'function getInitialTheme() {', 1)

if 'const [selectedUserEmail, setSelectedUserEmail]' not in app:
    app = app.replace(
"  const [activitySource, setActivitySource] = useState('nonProject');",
"  const [activitySource, setActivitySource] = useState('nonProject');\n  const [selectedUserEmail, setSelectedUserEmail] = useState(getInitialUserEmail);\n  const [securityContext, setSecurityContext] = useState({ loading: true, data: null, error: null });\n  const [devUsers, setDevUsers] = useState({ loading: true, data: [], error: null });",
1)

security_effects = block([
'  useEffect(() => {',
"    window.localStorage.setItem('ptp-dev-user-email', selectedUserEmail);",
'  }, [selectedUserEmail]);',
'',
'  useEffect(() => {',
'    let cancelled = false;',
'    async function loadSecurityContext() {',
'      setSecurityContext({ loading: true, data: null, error: null });',
'      try {',
'        const [usersResult, contextResult] = await Promise.all([',
"          fetchJson('/api/security/dev-users'),",
"          fetchJson(addUserEmail('/api/security/me/effective', selectedUserEmail))",
'        ]);',
'        if (!cancelled) {',
'          setDevUsers({ loading: false, data: usersResult.users ?? [], error: null });',
'          setSecurityContext({ loading: false, data: contextResult.context, error: null });',
'        }',
'      } catch (error) {',
'        if (!cancelled) {',
"          const message = error instanceof Error ? error.message : 'Security context failed to load';",
'          setDevUsers((current) => ({ ...current, loading: false, error: message }));',
'          setSecurityContext({ loading: false, data: null, error: message });',
'        }',
'      }',
'    }',
'    loadSecurityContext();',
'    return () => { cancelled = true; };',
'  }, [selectedUserEmail]);',
'',
])

if 'async function loadSecurityContext()' not in app:
    app = app.replace('  useEffect(() => {\n    let cancelled = false;\n\n    async function loadStatus() {', security_effects + '  useEffect(() => {\n    let cancelled = false;\n\n    async function loadStatus() {', 1)

app = app.replace('fetchJson(`/api/timesheets/week?weekStart=${selectedWeekStart}`)', 'fetchJson(addUserEmail(`/api/timesheets/week?weekStart=${selectedWeekStart}`, selectedUserEmail))')
app = app.replace("postJson('/api/timesheets/week/draft', payload)", "postJson(addUserEmail('/api/timesheets/week/draft', selectedUserEmail), payload)")
app = app.replace("postJson('/api/timesheets/week/draft', buildTimesheetPayload())", "postJson(addUserEmail('/api/timesheets/week/draft', selectedUserEmail), buildTimesheetPayload())")
app = app.replace("postJson('/api/timesheets/week/submit', buildTimesheetPayload())", "postJson(addUserEmail('/api/timesheets/week/submit', selectedUserEmail), buildTimesheetPayload())")
app = app.replace("postJson('/api/timesheets/day/submit', {", "postJson(addUserEmail('/api/timesheets/day/submit', selectedUserEmail), {")
app = app.replace("postJson('/api/timesheets/day/unlock', {", "postJson(addUserEmail('/api/timesheets/day/unlock', selectedUserEmail), {")
app = app.replace('  }, [selectedWeekStart]);', '  }, [selectedWeekStart, selectedUserEmail]);', 1)

if 'const effectiveSecurityContext = securityContext.data;' not in app:
    app = app.replace(
"  const currentTimesheetStatus = timesheet.data?.status ?? 'draft';\n  const isAnyDayEditable = days.length === 0 || days.some((day) => isDayEditable(day.date));",
"  const currentTimesheetStatus = timesheet.data?.status ?? 'draft';\n  const effectiveSecurityContext = securityContext.data;\n  const effectivePermissions = effectiveSecurityContext?.permissions ?? [];\n  const canEditOwnTime = effectivePermissions.includes('MANAGE_ALL') || effectivePermissions.includes('EDIT_OWN_TIME');\n  const canSubmitOwnTime = effectivePermissions.includes('MANAGE_ALL') || effectivePermissions.includes('SUBMIT_OWN_TIME');\n  const isAnyDayEditable = canEditOwnTime && (days.length === 0 || days.some((day) => isDayEditable(day.date)));",
1)

app = app.replace('return getDayStatus(workDate).canEdit !== false;', 'return canEditOwnTime && getDayStatus(workDate).canEdit !== false;')
app = app.replace('if (!selectedCell || isSaving) return;', 'if (!selectedCell || isSaving || !canSubmitOwnTime) return;', 1)
app = app.replace('disabled={isSaving || getSelectedDayTotal() < 8}', 'disabled={isSaving || !canSubmitOwnTime || getSelectedDayTotal() < 8}')

if '<UserSwitcher' not in app:
    app = app.replace(
'''        </nav>
        <button className="theme-toggle" type="button" onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}>''',
'''        </nav>
        <UserSwitcher
          users={devUsers.data.length > 0 ? devUsers.data : [{ email: selectedUserEmail, displayName: selectedUserEmail, roleNames: [] }]}
          selectedUserEmail={selectedUserEmail}
          onChange={setSelectedUserEmail}
          securityContext={effectiveSecurityContext}
        />
        <button className="theme-toggle" type="button" onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}>''',
1)

if 'Role enforcement is active' not in app:
    app = app.replace(
'''        <p className="hero-copy">
          A focused internal platform for weekly time entry, task-based project assignment, manager approval, project validation, accounting reconciliation, and utilization reporting.
        </p>''',
'''        <p className="hero-copy">
          A focused internal platform for weekly time entry, task-based project assignment, manager approval, project validation, accounting reconciliation, and utilization reporting.
        </p>
        <p className="muted small-text">Role enforcement is active. The user switcher changes the API security context without requiring a browser restart.</p>''',
1)

app_file.write_text(app)
