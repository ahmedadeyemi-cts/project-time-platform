from __future__ import annotations

from pathlib import Path
import re


def replace_once(path: Path, old: str, new: str, label: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected one {label}, found {count}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


def regex_once(path: Path, pattern: str, replacement: str, label: str, flags: int = 0) -> None:
    text = path.read_text(encoding="utf-8")
    updated, count = re.subn(pattern, replacement, text, count=1, flags=flags)
    if count != 1:
        raise RuntimeError(f"{path}: expected one {label}, found {count}")
    path.write_text(updated, encoding="utf-8")


# Module 082: a missing residual score must not break the risks response.
risk = Path("src/backend/ProjectTime.Api/Modules/ProjectRiskRegisterModule.cs")
replace_once(
    risk,
    '          risk.residual_impact_score,risk.residual_exposure,\n          {RatingSql("risk.residual_exposure")} AS residual_rating,risk.escalation_level,risk.escalation_decision,',
    '          risk.residual_impact_score,risk.residual_exposure,\n          COALESCE({RatingSql("risk.residual_exposure")},\'\') AS residual_rating,risk.escalation_level,risk.escalation_decision,',
    "nullable residual rating SQL projection",
)
replace_once(
    risk,
    "residualExposure = NullableShort(reader, 46), residualRating = reader.GetString(47), escalationLevel = reader.GetString(48)",
    "residualExposure = NullableShort(reader, 46), residualRating = reader.IsDBNull(47) ? string.Empty : reader.GetString(47), escalationLevel = reader.GetString(48)",
    "nullable residual rating reader",
)

# Module 081: governed team directory.
lab_module = Path("src/backend/ProjectTime.Api/Modules/LabEquipmentTrackerModule.cs")
replace_once(
    lab_module,
    '        group.MapGet(\n            "/summary",\n            (Func<HttpContext, Task<IResult>>)GetSummaryAsync);\n        group.MapGet("/equipment", ListEquipmentAsync);',
    '        group.MapGet(\n            "/summary",\n            (Func<HttpContext, Task<IResult>>)GetSummaryAsync);\n        group.MapGet(\n            "/teams",\n            (Func<HttpContext, Task<IResult>>)ListManagingTeamsAsync);\n        group.MapGet("/equipment", ListEquipmentAsync);',
    "Module 081 teams route",
)
teams_method = '''
    private static async Task<IResult> ListManagingTeamsAsync(HttpContext context)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await RequireAccessAsync(context, connection, manage: false);
            if (access.Error is not null) return access.Error;

            await using var command = new NpgsqlCommand("""
                SELECT DISTINCT btrim(COALESCE(to_jsonb(app_user)->>'team_name','')) AS team_name
                FROM app_users app_user
                WHERE app_user.is_active=TRUE
                  AND btrim(COALESCE(to_jsonb(app_user)->>'team_name',''))<>''
                  AND (@broad_scope=TRUE OR lower(btrim(COALESCE(to_jsonb(app_user)->>'team_name','')))=lower(@team_name))
                ORDER BY team_name;
                """, connection);
            command.Parameters.AddWithValue("broad_scope", access.Value!.IsBroadScope);
            command.Parameters.AddWithValue("team_name", access.Value.TeamName ?? string.Empty);
            var teams = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted)) teams.Add(reader.GetString(0));
            if (!string.IsNullOrWhiteSpace(access.Value.TeamName)
                && !teams.Contains(access.Value.TeamName, StringComparer.OrdinalIgnoreCase))
                teams.Insert(0, access.Value.TeamName);
            return Results.Ok(new { module=Module, teams, scope=Scope(access.Value) });
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "list managing teams");
        }
    }

'''
replace_once(
    lab_module,
    "    private static async Task<IResult> ListEquipmentAsync(\n",
    teams_method + "    private static async Task<IResult> ListEquipmentAsync(\n",
    "Module 081 managing-team method",
)

# Module 081 frontend: bulk import shortcut and scoped team dropdown.
lab_ui = Path("src/frontend/project-time-web/src/LabEquipmentTrackerCenter.jsx")
replace_once(
    lab_ui,
    "import { useCallback, useEffect, useMemo, useState } from 'react';",
    "import { useCallback, useEffect, useMemo, useRef, useState } from 'react';",
    "Module 081 useRef import",
)
replace_once(
    lab_ui,
    "  const [message, setMessage] = useState('');\n",
    "  const [message, setMessage] = useState('');\n  const [teams, setTeams] = useState([]);\n  const importFileRef = useRef(null);\n",
    "Module 081 team state",
)
replace_once(
    lab_ui,
    "        ['summary', request('/summary')],\n        ['equipment', request(query ? `/equipment?${query}` : '/equipment')],",
    "        ['summary', request('/summary')],\n        ['teams', request('/teams')],\n        ['equipment', request(query ? `/equipment?${query}` : '/equipment')],",
    "Module 081 teams surface",
)
replace_once(
    lab_ui,
    "        if (surface === 'summary') setSummary(payload);\n        if (surface === 'equipment') loaded.equipment = payload.equipment || [];",
    "        if (surface === 'summary') setSummary(payload);\n        if (surface === 'teams') setTeams(payload.teams || []);\n        if (surface === 'equipment') loaded.equipment = payload.equipment || [];",
    "Module 081 teams result",
)
replace_once(
    lab_ui,
    '<div className="eg-toolbar-group"><button className="eg-button" onClick={refresh} disabled={busy}>Refresh</button>',
    '<div className="eg-toolbar-group"><button className="eg-button" type="button" onClick={() => { setTab(\'imports\'); window.requestAnimationFrame(() => importFileRef.current?.focus()); }} disabled={!dataReady || !permissions.canImport || busy}>Bulk upload spreadsheet</button><button className="eg-button" onClick={refresh} disabled={busy}>Refresh</button>',
    "Module 081 bulk upload action",
)
text = lab_ui.read_text(encoding="utf-8")
field_pattern = re.compile(r'<Field label="Managing team"><Input required value=\{form\.managingTeam\} onChange=\{\(value\) => setValue\(\'managingTeam\', value\)\} /></Field>')
field_replacement = '<Field label="Managing team"><Select required value={form.managingTeam} onChange={(value) => setValue(\'managingTeam\', value)}><option value="">Select managing team</option>{[...new Set([form.managingTeam, ...teams].filter(Boolean))].map((team) => <option key={team} value={team}>{team}</option>)}</Select></Field>'
text, field_count = field_pattern.subn(field_replacement, text)
if field_count < 1:
    raise RuntimeError("Module 081 managing-team field was not found")
lab_ui.write_text(text, encoding="utf-8")
replace_once(
    lab_ui,
    '<ImportView rows={data.imports} preview={importPreview} canImport={permissions.canImport} busy={busy} onPreview={previewImport} onCommit={commitImport} />',
    '<ImportView rows={data.imports} preview={importPreview} canImport={permissions.canImport} busy={busy} onPreview={previewImport} onCommit={commitImport} inputRef={importFileRef} />',
    "Module 081 import ref prop",
)
regex_once(
    lab_ui,
    r"function ImportView\(\{\s*rows,\s*preview,\s*canImport,\s*busy,\s*onPreview,\s*onCommit\s*\}\)",
    "function ImportView({ rows, preview, canImport, busy, onPreview, onCommit, inputRef })",
    "Module 081 ImportView signature",
)
regex_once(
    lab_ui,
    r'<input([^>]*?)name="file"([^>]*?)/>',
    r'<input\1name="file"\2 ref={inputRef} accept=".csv,.xlsx" />',
    "Module 081 import file input",
)

# Module 039: only the authoritative billing-candidate source blocks the page.
billing = Path("src/frontend/project-time-web/src/BillingReadinessCenter.jsx")
replace_once(
    billing,
    "  const [payload, setPayload] = useState({ loading: true, error: null, workspace: null, intake: null, customers: null, certifyExpenses: null, certifyExceptions: null, billingCandidates: [] });",
    "  const [payload, setPayload] = useState({ loading: true, error: null, degradedSources: [], workspace: null, intake: null, customers: null, certifyExpenses: null, certifyExceptions: null, billingCandidates: [] });",
    "Module 039 degraded source state",
)
replace_once(
    billing,
    """    const failures = results
      .filter((result) => result.status === 'rejected')
      .map((result) => result.reason instanceof Error ? result.reason.message : 'Unknown loading error.');

    setPayload({
      loading: false,
      error: failures.length > 0 ? failures.join(' | ') : null,""",
    """    const sourceNames = ['Project Workspace', 'Project Intake', 'Customer Directory', 'Certify staged expenses', 'Certify exceptions', 'Billing candidates'];
    const degradedSources = results
      .map((result, index) => result.status === 'rejected'
        ? { source: sourceNames[index], message: result.reason instanceof Error ? result.reason.message : 'Source unavailable.' }
        : null)
      .filter(Boolean);
    const billingCandidateFailure = billingCandidates.status === 'rejected'
      ? (billingCandidates.reason instanceof Error ? billingCandidates.reason.message : 'Billing candidates are unavailable.')
      : null;

    setPayload({
      loading: false,
      error: billingCandidateFailure,
      degradedSources,""",
    "Module 039 independent source handling",
)
regex_once(
    billing,
    r'(return\s*\(\s*<div className="billing-readiness-center"[^>]*>)',
    r'''\1
      {payload.degradedSources?.length ? <div className="billing-source-warning" role="status"><div><strong>Supporting source status</strong><span>{payload.degradedSources.map((item) => item.source).join(', ')} {payload.degradedSources.length === 1 ? 'is' : 'are'} temporarily unavailable. Healthy billing data remains available.</span></div><button type="button" onClick={loadBillingReadinessData}>Retry sources</button></div> : null}''',
    "Module 039 degraded-source notice",
)

# Module 019: hide terminal projects for all roles and reconcile legacy paths under the canonical root.
workspace = Path("src/backend/ProjectTime.Api/Modules/ProjectWorkspaceModule.cs")
text = workspace.read_text(encoding="utf-8")
conditional = "(@hide_closed_projects = FALSE OR LOWER(COALESCE(p.status, '')) NOT IN ('closed', 'completed', 'cancelled', 'canceled', 'archived'))"
conditional_count = text.count(conditional)
if conditional_count < 4:
    raise RuntimeError(f"Module 019 terminal-state filter count was {conditional_count}")
text = text.replace(conditional, "LOWER(COALESCE(p.status, '')) NOT IN ('closed', 'completed', 'cancelled', 'canceled', 'archived')")
old_download = '''        if (!File.Exists(storagePath))
        {
            return Results.NotFound(new
            {
                status = "file_missing",
                message = "Document metadata exists, but the stored file was not found."
            });
        }

        return Results.File(storagePath, contentType, originalFileName);'''
new_download = '''        var resolvedStoragePath = ResolveProjectDocumentStoragePath(storagePath);
        if (resolvedStoragePath is null)
        {
            return Results.NotFound(new
            {
                status = "file_reconciliation_required",
                message = "Document metadata is available, but the stored file must be reconciled with the persistent upload volume before it can be downloaded."
            });
        }

        return Results.File(resolvedStoragePath, contentType, originalFileName);'''
if text.count(old_download) != 1:
    raise RuntimeError("Module 019 download block was not found")
text = text.replace(old_download, new_download, 1)
resolver = '''
    private static string? ResolveProjectDocumentStoragePath(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return null;
        var root = ProjectTime.Api.Ai.ProjectPulseUploadStorage.ResolveRoot();
        var candidates = new List<string>();
        try
        {
            var raw = storedPath.Trim();
            if (Path.IsPathFullyQualified(raw)) candidates.Add(Path.GetFullPath(raw));
            else candidates.Add(Path.GetFullPath(Path.Combine(root, raw)));

            var normalized = raw.Replace('\\\\', '/');
            var uploadMarker = normalized.IndexOf("/uploads/", StringComparison.OrdinalIgnoreCase);
            if (uploadMarker >= 0)
                candidates.Add(Path.GetFullPath(Path.Combine(root, normalized[(uploadMarker + 9)..].Replace('/', Path.DirectorySeparatorChar))));

            var fileName = Path.GetFileName(raw);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                candidates.Add(Path.GetFullPath(Path.Combine(root, fileName)));
                if (Directory.Exists(root))
                {
                    foreach (var match in Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).Take(2))
                        candidates.Add(Path.GetFullPath(match));
                }
            }
        }
        catch
        {
            return null;
        }

        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!candidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                && !candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !candidate.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!File.Exists(candidate)) continue;
            var info = new FileInfo(candidate);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            return candidate;
        }
        return null;
    }

'''
marker = "    private static void AddScopeParameters(NpgsqlCommand command, ProjectWorkspaceAccessContext access)\n"
if text.count(marker) != 1:
    raise RuntimeError("Module 019 scope method marker was not found")
workspace.write_text(text.replace(marker, resolver + marker, 1), encoding="utf-8")

# Header theme changes persist to the signed-in user's profile.
shell = Path("src/frontend/project-time-web/src/pulse-shell-frontend-compatibility.js")
text = shell.read_text(encoding="utf-8")
if text.count("function applyTheme(theme) {") != 1:
    raise RuntimeError("Theme application function was not found")
text = text.replace("function applyTheme(theme) {", "function applyTheme(theme, persistProfile = false) {", 1)
marker = "  synchronizeThemeButtons();\n}\n\nfunction themeButton"
insert = '''  synchronizeThemeButtons();
  if (persistProfile) persistSignedInThemePreference(normalized);
}

let themePreferenceTimer = 0;
function persistSignedInThemePreference(theme) {
  window.clearTimeout(themePreferenceTimer);
  themePreferenceTimer = window.setTimeout(async () => {
    try {
      const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
      const token = session?.sessionToken || session?.token || session?.accessToken || '';
      if (!token) return;
      const username = String(session?.username || session?.email || 'anonymous').toLowerCase();
      const key = `projectPulseUserPreferences:${username}`;
      let preferences = {};
      try { preferences = JSON.parse(window.localStorage.getItem(key) || '{}') || {}; } catch { preferences = {}; }
      const next = { ...preferences, theme };
      window.localStorage.setItem(key, JSON.stringify(next));
      await window.fetch('/api/profile/preferences', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`,
          'X-ProjectPulse-Session': token
        },
        credentials: 'include',
        cache: 'no-store',
        body: JSON.stringify(next)
      });
    } catch {
      // Browser preference remains authoritative while profile persistence is unavailable.
    }
  }, 180);
}

function themeButton'''
if text.count(marker) != 1:
    raise RuntimeError("Theme synchronization marker was not found")
text = text.replace(marker, insert, 1)
text = text.replace("    applyTheme(button.dataset.pulseThemeChoice);", "    applyTheme(button.dataset.pulseThemeChoice, true);", 1)
shell.write_text(text, encoding="utf-8")

main = Path("src/frontend/project-time-web/src/main.jsx")
replace_once(
    main,
    "import './pulse-shell-frontend-compatibility.css';\nimport './enterprise-contrast-guard.css';",
    "import './pulse-shell-frontend-compatibility.css';\nimport './profile-settings-enterprise.css';\nimport './enterprise-contrast-guard.css';",
    "enterprise profile stylesheet import",
)

Path("src/frontend/project-time-web/src/profile-settings-enterprise.css").write_text('''/* Enterprise profile and personal-preference presentation. */
.profile-settings-backdrop{padding:clamp(1rem,3vw,3rem)!important;background:rgba(3,15,31,.64)!important;backdrop-filter:blur(14px) saturate(125%)!important}.profile-settings-modal.strong-profile-modal{width:min(960px,calc(100vw - 2rem))!important;max-height:calc(100vh - 2rem)!important;padding:0!important;overflow:hidden!important;border:1px solid color-mix(in srgb,var(--border) 76%,#8bb8d8)!important;border-radius:24px!important;background:var(--surface)!important;color:var(--text)!important;box-shadow:0 30px 90px rgba(2,16,32,.34)!important}.profile-settings-header{display:flex!important;align-items:flex-start!important;justify-content:space-between!important;gap:1rem!important;padding:1.5rem 1.6rem 1.25rem!important;border-bottom:1px solid var(--border)!important;background:linear-gradient(135deg,color-mix(in srgb,var(--surface) 92%,#dff3ff),var(--surface))!important}.profile-settings-header h2{margin:.15rem 0 .35rem!important;font-size:clamp(1.65rem,3vw,2.15rem)!important;color:var(--text)!important}.profile-settings-header p:last-child{margin:0!important;color:var(--muted)!important}.profile-settings-header>button{width:44px!important;height:44px!important;border:1px solid var(--border)!important;border-radius:14px!important;background:var(--surface-strong)!important;color:var(--text)!important}.profile-settings-tabs{display:inline-flex!important;gap:.3rem!important;margin:1.15rem 1.6rem 0!important;padding:.3rem!important;border:1px solid var(--border)!important;border-radius:14px!important;background:var(--surface-strong)!important}.profile-settings-tabs button{min-height:40px!important;padding:.55rem 1rem!important;border:0!important;border-radius:10px!important;background:transparent!important;color:var(--muted)!important;font-weight:850!important}.profile-settings-tabs button.active{background:var(--surface)!important;color:var(--brand-blue-strong)!important;box-shadow:0 5px 15px rgba(9,42,74,.1)!important}.profile-settings-form{display:grid!important;gap:1rem!important;max-height:calc(100vh - 220px)!important;padding:1.25rem 1.6rem 5.75rem!important;overflow:auto!important}.theme-choice-grid{display:grid!important;grid-template-columns:repeat(2,minmax(0,1fr))!important;gap:.85rem!important;padding:0!important;border:0!important}.theme-choice{position:relative!important;display:grid!important;grid-template-columns:auto 1fr!important;grid-template-areas:'radio title' 'radio detail'!important;align-items:center!important;gap:.25rem .75rem!important;min-height:112px!important;padding:1rem!important;border:1px solid var(--border)!important;border-radius:16px!important;background:var(--surface-strong)!important;color:var(--text)!important;cursor:pointer!important}.theme-choice input{grid-area:radio!important;width:22px!important;height:22px!important;margin:0!important;accent-color:var(--brand-blue)!important}.theme-choice strong{grid-area:title!important;display:block!important;color:var(--text)!important;font-size:1rem!important}.theme-choice span{grid-area:detail!important;display:block!important;color:var(--muted)!important;line-height:1.4!important}.theme-choice.active{border-color:var(--brand-blue)!important;background:color-mix(in srgb,var(--accent) 62%,var(--surface))!important;box-shadow:0 0 0 3px color-mix(in srgb,var(--brand-blue) 15%,transparent)!important}.profile-settings-actions{position:sticky!important;bottom:-5.75rem!important;display:flex!important;justify-content:flex-end!important;gap:.65rem!important;margin:1rem -1.6rem -5.75rem!important;padding:1rem 1.6rem!important;border-top:1px solid var(--border)!important;background:color-mix(in srgb,var(--surface) 94%,transparent)!important;backdrop-filter:blur(14px)!important}.profile-settings-form input,.profile-settings-form select,.profile-settings-form textarea{color:var(--text)!important;background:var(--surface-strong)!important;border-color:var(--border)!important}:root[data-theme='dark'] .profile-settings-modal.strong-profile-modal,body[data-theme='dark'] .profile-settings-modal.strong-profile-modal{background:#0f1d2f!important;color:#f4f8ff!important}:root[data-theme='dark'] .profile-settings-header,body[data-theme='dark'] .profile-settings-header{background:linear-gradient(135deg,#14263d,#0f1d2f)!important}.billing-source-warning{display:flex;align-items:center;justify-content:space-between;gap:1rem;padding:.9rem 1rem;border:1px solid #e6b85c;border-radius:14px;background:#fff7df;color:#6a4600}.billing-source-warning div{display:grid;gap:.2rem}.billing-source-warning span{font-size:.88rem}.billing-source-warning button{border:1px solid currentColor;border-radius:999px;padding:.55rem .8rem;background:transparent;color:inherit;font-weight:800;cursor:pointer}:root[data-theme='dark'] .billing-source-warning{background:#33260c;color:#ffe2a1}@media(max-width:720px){.theme-choice-grid{grid-template-columns:1fr!important}.profile-settings-backdrop{padding:.5rem!important}.profile-settings-modal.strong-profile-modal{width:calc(100vw - 1rem)!important;max-height:calc(100vh - 1rem)!important}.billing-source-warning{align-items:flex-start;flex-direction:column}}
''', encoding="utf-8")

Path("src/frontend/project-time-web/scripts/validate-enterprise-ui-documents-governance.mjs").write_text('''import fs from 'node:fs';
const read=(path)=>fs.readFileSync(path,'utf8');const passed=[];const test=(name,ok)=>{if(!ok)throw new Error(name);passed.push(name)};
const risk=read('src/backend/ProjectTime.Api/Modules/ProjectRiskRegisterModule.cs');const lab=read('src/backend/ProjectTime.Api/Modules/LabEquipmentTrackerModule.cs');const labUi=read('src/frontend/project-time-web/src/LabEquipmentTrackerCenter.jsx');const workspace=read('src/backend/ProjectTime.Api/Modules/ProjectWorkspaceModule.cs');const billing=read('src/frontend/project-time-web/src/BillingReadinessCenter.jsx');const shell=read('src/frontend/project-time-web/src/pulse-shell-frontend-compatibility.js');const main=read('src/frontend/project-time-web/src/main.jsx');
test('MODULE_082_NULL_SAFE_RESIDUAL_RATING',risk.includes("COALESCE({RatingSql(\\\"risk.residual_exposure\\\")},'')")&&risk.includes('reader.IsDBNull(47)'));test('MODULE_081_TEAMS_ENDPOINT',lab.includes('ListManagingTeamsAsync')&&lab.includes('"/teams"'));test('MODULE_081_BULK_UPLOAD',labUi.includes('Bulk upload spreadsheet')&&labUi.includes('accept=".csv,.xlsx"'));test('MODULE_081_TEAM_DROPDOWN',labUi.includes('Select managing team')&&labUi.includes("request('/teams')"));test('MODULE_019_TERMINAL_PROJECTS_HIDDEN',!workspace.includes('@hide_closed_projects = FALSE OR LOWER(COALESCE(p.status'));test('MODULE_019_PERSISTENT_PATH_RECONCILIATION',workspace.includes('ResolveProjectDocumentStoragePath')&&workspace.includes('file_reconciliation_required'));test('MODULE_039_SOURCE_ISOLATION',billing.includes('degradedSources')&&billing.includes('billingCandidateFailure'));test('PROFILE_THEME_PERSISTENCE',shell.includes('persistSignedInThemePreference')&&shell.includes("'/api/profile/preferences'"));test('ENTERPRISE_PROFILE_STYLES',main.includes("import './profile-settings-enterprise.css';"));console.log(JSON.stringify({passed:passed.length,tests:passed},null,2));
''', encoding="utf-8")

print("Enterprise corrections applied.")
