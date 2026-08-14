#!/usr/bin/env python3
"""Apply the guarded Module 003 utilization and authorization-stability repair.

This script is intentionally anchor-driven. It refuses partial changes when the
reviewed main source no longer matches, which protects concurrent repository work.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path.cwd()
PROGRAM = ROOT / "src/backend/ProjectTime.Api/Program.cs"
TEAM_PANEL = ROOT / "src/frontend/project-time-web/src/EngineeringTeamLeadUtilizationPanel.jsx"
VIEW_AS_DRAWER = ROOT / "src/frontend/project-time-web/src/GlobalViewAsDrawer.jsx"
APP = ROOT / "src/frontend/project-time-web/src/App.jsx"
TEST = ROOT / "tests/validate-utilization-role-scoping.mjs"
CI = ROOT / ".github/workflows/utilization-role-scoping-ci.yml"


def read(path: Path) -> str:
    if not path.is_file():
        raise RuntimeError(f"Required file is missing: {path.relative_to(ROOT)}")
    return path.read_text(encoding="utf-8")


def replace_once(source: str, old: str, new: str, label: str) -> str:
    count = source.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one anchor, found {count}")
    return source.replace(old, new, 1)


def regex_once(source: str, pattern: str, replacement: str, label: str) -> str:
    updated, count = re.subn(pattern, replacement, source, count=1, flags=re.MULTILINE | re.DOTALL)
    if count != 1:
        raise RuntimeError(f"{label}: expected one regex anchor, found {count}")
    return updated


YEARLY_ENDPOINT = r'''app.MapGet("/api/utilization/yearly-status", async (int? year, HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var selectedYear = year ?? DateTime.UtcNow.Year;
    var minimumYear = DateTime.UtcNow.Year - 3;
    var maximumYear = DateTime.UtcNow.Year + 6;

    if (selectedYear < minimumYear) selectedYear = minimumYear;
    if (selectedYear > maximumYear) selectedYear = maximumYear;

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var yearStart = new DateOnly(selectedYear, 1, 1);
    var nextYearStart = new DateOnly(selectedYear + 1, 1, 1);
    decimal standardQuarterHours = 482m;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    await using (var policyCommand = new NpgsqlCommand("""
        SELECT standard_period_hours
        FROM utilization_policies
        WHERE is_active = TRUE
        ORDER BY created_at DESC
        LIMIT 1;
        """, connection))
    {
        var policyHours = await policyCommand.ExecuteScalarAsync();
        if (policyHours is decimal configuredHours && configuredHours > 0)
        {
            standardQuarterHours = configuredHours;
        }
    }

    var billableByQuarter = new Dictionary<int, decimal>
    {
        [1] = 0m,
        [2] = 0m,
        [3] = 0m,
        [4] = 0m
    };

    await using (var usageCommand = new NpgsqlCommand("""
        WITH entry_rows AS (
            SELECT
                NULLIF(to_jsonb(te)->>'work_date', '')::date AS work_date,
                COALESCE(NULLIF(to_jsonb(te)->>'hours', '')::numeric, 0) AS hours,
                CASE
                    WHEN NULLIF(to_jsonb(te)->>'is_billable', '') IS NOT NULL
                        THEN NULLIF(to_jsonb(te)->>'is_billable', '')::boolean
                    WHEN NULLIF(to_jsonb(te)->>'billable', '') IS NOT NULL
                        THEN NULLIF(to_jsonb(te)->>'billable', '')::boolean
                    ELSE COALESCE(
                        NULLIF(to_jsonb(te)->>'project_id', ''),
                        NULLIF(to_jsonb(te)->>'project_task_id', ''),
                        NULLIF(to_jsonb(te)->>'task_id', ''),
                        NULLIF(to_jsonb(te)->>'service_request_id', '')
                    ) IS NOT NULL
                END AS is_billable,
                COALESCE(NULLIF(to_jsonb(ts)->>'status', ''), 'draft') AS timesheet_status
            FROM time_entries te
            JOIN timesheets ts
              ON ts.timesheet_id = te.timesheet_id
            WHERE ts.user_id = @user_id
        )
        SELECT
            EXTRACT(QUARTER FROM work_date)::int AS quarter_number,
            COALESCE(SUM(hours), 0)::numeric AS billable_hours
        FROM entry_rows
        WHERE work_date >= @year_start
          AND work_date < @next_year_start
          AND is_billable = TRUE
          AND timesheet_status NOT IN ('manager_declined', 'rejected', 'voided')
        GROUP BY EXTRACT(QUARTER FROM work_date)::int
        ORDER BY quarter_number;
        """, connection))
    {
        usageCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);
        usageCommand.Parameters.AddWithValue("year_start", yearStart);
        usageCommand.Parameters.AddWithValue("next_year_start", nextYearStart);

        await using var reader = await usageCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var quarterNumber = reader.GetInt32(0);
            if (quarterNumber is >= 1 and <= 4)
            {
                billableByQuarter[quarterNumber] = Math.Round(reader.GetDecimal(1), 2);
            }
        }
    }

    var targets = new[] { 70m, 75m, 80m, 85m, 90m, 95m, 100m, 105m }
        .Select(percent => new
        {
            targetPercent = percent,
            targetHours = Math.Round(standardQuarterHours * percent / 100m, 1)
        })
        .ToList();

    var quarters = new List<object>();

    foreach (var quarterNumber in new[] { 1, 2, 3, 4 })
    {
        var billableHours = billableByQuarter[quarterNumber];
        var utilizationPercent = standardQuarterHours == 0
            ? 0m
            : Math.Round((billableHours / standardQuarterHours) * 100m, 2);

        var nextTarget = targets.FirstOrDefault(target => target.targetHours > billableHours);
        var hoursToNextTarget = nextTarget is null
            ? 0m
            : Math.Max(0m, Math.Round(nextTarget.targetHours - billableHours, 2));

        quarters.Add(new
        {
            quarterNumber,
            quarterName = $"Q{quarterNumber}",
            standardQuarterHours,
            billableHours,
            utilizationPercent,
            nextTargetPercent = nextTarget?.targetPercent,
            nextTargetHours = nextTarget?.targetHours,
            hoursToNextTarget,
            thresholds = targets.Select(target => new
            {
                target.targetPercent,
                target.targetHours,
                hoursRemaining = Math.Max(0m, Math.Round(target.targetHours - billableHours, 2)),
                reached = billableHours >= target.targetHours
            })
        });
    }

    var annualBillableHours = billableByQuarter.Values.Sum();
    var annualCapacityHours = standardQuarterHours * 4m;
    var annualUtilizationPercent = annualCapacityHours == 0
        ? 0m
        : Math.Round((annualBillableHours / annualCapacityHours) * 100m, 2);

    return Results.Ok(new
    {
        year = selectedYear,
        standardQuarterHours,
        calculationStatus = "calculated",
        calculationNote = "Utilization is calculated from the signed-in effective user's authoritative billable time entries. Declined, rejected, and voided time is excluded.",
        annualSummary = new
        {
            billableHours = annualBillableHours,
            capacityHours = annualCapacityHours,
            utilizationPercent = annualUtilizationPercent
        },
        quarters
    });
});'''


def patch_program(source: str) -> str:
    if "UTILIZATION_ROLE_SCOPE_20260814" in source:
        raise RuntimeError("Program.cs already contains the utilization repair marker")

    yearly_pattern = (
        r'app\.MapGet\("/api/utilization/yearly-status",[\s\S]*?\n\}\);'
        r'\n\n\napp\.MapGet\("/api/project-allocation-info/source-projects"'
    )
    source = regex_once(
        source,
        yearly_pattern,
        YEARLY_ENDPOINT + '\n\n\napp.MapGet("/api/project-allocation-info/source-projects"',
        "yearly utilization endpoint",
    )

    start_marker = 'app.MapGet("/api/utilization/engineering-team-summary"'
    end_marker = 'app.MapGet("/api/utilization/manager-team-summary"'
    start = source.find(start_marker)
    end = source.find(end_marker, start + 1)
    if start < 0 or end < 0:
        raise RuntimeError("Could not isolate engineering-team-summary endpoint")

    block = source[start:end]

    block = replace_once(
        block,
        '''    var canViewAll =
        (roles.Contains("SUPER_ADMINISTRATOR") || roles.Contains("ADMINISTRATOR"))
        || roles.Contains("PROJECT_TEAM_COORDINATOR")
        || permissions.Contains("SYSTEM_ADMINISTRATION")
        || permissions.Contains("MANAGE_ALL");

    var isEngineeringTeamLead = (roles.Contains("ENGINEERING_LEAD") || roles.Contains("ENGINEERING_TEAM_LEAD"));
    var isManager = roles.Contains("MANAGER");
    var isEngineer = (roles.Contains("ENGINEERING") || roles.Contains("ENGINEER"));''',
        '''    /* UTILIZATION_ROLE_SCOPE_20260814 */
    var canViewAll =
        roles.Contains("SUPER_ADMINISTRATOR")
        || roles.Contains("SUPERADMINISTRATOR")
        || roles.Contains("GLOBAL_ADMINISTRATOR")
        || roles.Contains("GLOBALADMINISTRATOR")
        || roles.Contains("ADMINISTRATOR")
        || roles.Contains("PROJECT_TEAM_COORDINATOR")
        || roles.Contains("EXECUTIVE")
        || roles.Contains("EXECUTIVE_LEADERSHIP")
        || permissions.Contains("VIEW_ORGANIZATION_UTILIZATION")
        || permissions.Contains("VIEW_ALL_UTILIZATION")
        || permissions.Contains("SYSTEM_ADMINISTRATION")
        || permissions.Contains("MANAGE_ALL");

    var isEngineeringTeamLead =
        roles.Contains("TEAM_LEAD")
        || roles.Contains("ENGINEERING_LEAD")
        || roles.Contains("ENGINEERING_TEAM_LEAD");
    var isDirector = roles.Contains("DIRECTOR") || roles.Contains("ENGINEERING_DIRECTOR");
    var isManager = roles.Contains("MANAGER") || roles.Contains("ENGINEERING_MANAGER") || isDirector;
    var isEngineer = roles.Contains("ENGINEERING") || roles.Contains("ENGINEER");''',
        "utilization role definitions",
    )

    block = replace_once(
        block,
        '''    var canUseOwnScope = false;

    var canAccess =
        canViewAll
        || canUseTeamScope;''',
        '''    var canUseOwnScope = isEngineer || isEngineeringTeamLead;

    var canAccess =
        canViewAll
        || canUseTeamScope
        || canUseOwnScope;''',
        "own utilization access",
    )

    block = replace_once(
        block,
        'message = "Engineering team utilization is available to Engineering Team Leads, Managers, Project/Team Coordinators, and Administrators."',
        'message = "Utilization access is limited to an engineer\'s own record, assigned team scope, or authorized organization-wide reporting scope."',
        "access denied message",
    )

    role_filter = "AND r.role_code IN ('ENGINEERING', 'ENGINEER')"
    if block.count(role_filter) != 2:
        raise RuntimeError(f"engineering role query filter: expected two anchors, found {block.count(role_filter)}")
    block = block.replace(
        role_filter,
        "AND r.role_code IN ('ENGINEERING', 'ENGINEER', 'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD')",
        2,
    )

    block = replace_once(
        block,
        '''            eligible_engineers AS (
                SELECT user_id FROM membership_candidates
                UNION
                SELECT user_id FROM profile_candidates
            )''',
        '''            eligible_engineers AS (
                SELECT user_id FROM membership_candidates
                UNION
                SELECT user_id FROM profile_candidates
                UNION
                SELECT @user_id
            )''',
        "engineering lead self visibility",
    )

    block = replace_once(
        block,
        '        if (canViewAll || isEngineeringTeamLead)',
        '        if (canViewAll || canUseTeamScope)',
        "manager and director engineer selection",
    )

    block = replace_once(
        block,
        '''            isEngineeringTeamLead,
            isManager,
            isEngineer,''',
        '''            isEngineeringTeamLead,
            isManager,
            isDirector,
            isEngineer,''',
        "access metadata",
    )

    block = replace_once(
        block,
        'calculationNote = "Engineering Team Lead scope is enforced by backend role scope. Team leads can only view engineers on matching active team membership or profile team/department scope."',
        'calculationNote = "Backend authorization enforces self-only Engineer scope, assigned team scope for Engineering Leads, Managers, and Directors, and organization-wide scope for Executives and authorized administrators."',
        "calculation note",
    )

    return source[:start] + block + source[end:]


TEAM_HELPERS = r'''

const ENGINEERING_TEAM_SCOPE_ROLE_CODES = new Set([
  'TEAM_LEAD',
  'ENGINEERING_LEAD',
  'ENGINEERING_TEAM_LEAD',
  'MANAGER',
  'ENGINEERING_MANAGER',
  'DIRECTOR',
  'ENGINEERING_DIRECTOR'
]);

const ENGINEERING_ORGANIZATION_SCOPE_ROLE_CODES = new Set([
  'SUPER_ADMINISTRATOR',
  'SUPERADMINISTRATOR',
  'GLOBAL_ADMINISTRATOR',
  'GLOBALADMINISTRATOR',
  'ADMINISTRATOR',
  'PROJECT_TEAM_COORDINATOR',
  'EXECUTIVE',
  'EXECUTIVE_LEADERSHIP'
]);

function normalizeUtilizationRoleCode(value) {
  return String(value ?? '').trim().toUpperCase().replace(/[\s-]+/g, '_');
}

function canLoadEngineeringTeamSummary(securityContext) {
  const roleCodes = new Set((securityContext?.roles ?? [])
    .map((role) => normalizeUtilizationRoleCode(role?.roleCode ?? role?.roleName ?? role))
    .filter(Boolean));
  const permissions = new Set((securityContext?.permissions ?? [])
    .map((permission) => String(permission ?? '').trim().toUpperCase())
    .filter(Boolean));

  return [...roleCodes].some((roleCode) =>
    ENGINEERING_TEAM_SCOPE_ROLE_CODES.has(roleCode)
    || ENGINEERING_ORGANIZATION_SCOPE_ROLE_CODES.has(roleCode))
    || permissions.has('VIEW_TEAM_UTILIZATION')
    || permissions.has('VIEW_ORGANIZATION_UTILIZATION')
    || permissions.has('VIEW_ALL_UTILIZATION')
    || permissions.has('SYSTEM_ADMINISTRATION')
    || permissions.has('MANAGE_ALL');
}
'''


def patch_team_panel(source: str) -> str:
    if "ENGINEERING_TEAM_SCOPE_ROLE_CODES" in source:
        raise RuntimeError("EngineeringTeamLeadUtilizationPanel.jsx already contains the role gate")

    source = replace_once(
        source,
        '}\n\nasync function readApiErrorMessage',
        '}' + TEAM_HELPERS + '\nasync function readApiErrorMessage',
        "team panel helper insertion",
    )

    source = replace_once(
        source,
        '''  useEffect(() => {
    loadUtilization(selectedYear, selectedEngineerUserId);
  }, []);''',
        '''  useEffect(() => {
    let cancelled = false;

    async function loadAuthorizedTeamUtilization() {
      try {
        const securityContext = await fetchJson('/api/security/context');
        if (cancelled) return;

        if (!canLoadEngineeringTeamSummary(securityContext)) {
          setPayload({
            loading: false,
            data: { canViewEngineeringTeamUtilization: false },
            error: null
          });
          return;
        }

        await loadUtilization(selectedYear, selectedEngineerUserId);
      } catch (error) {
        if (cancelled) return;
        setPayload({
          loading: false,
          data: null,
          error: error instanceof Error ? error.message : 'Unable to verify engineering utilization access.'
        });
      }
    }

    void loadAuthorizedTeamUtilization();
    return () => {
      cancelled = true;
    };
  }, []);''',
        "team panel authorization preflight",
    )

    return source


VIEW_AS_AUTHORITY_HELPER = r'''

function securityContextAllowsViewAs(context) {
  const roleCodes = new Set((context?.roles ?? [])
    .map((role) => String(role?.roleCode ?? role?.roleName ?? role ?? '')
      .trim()
      .toUpperCase()
      .replace(/[\s-]+/g, '_'))
    .filter(Boolean));

  return [
    'SUPER_ADMINISTRATOR',
    'SUPERADMINISTRATOR',
    'GLOBAL_ADMINISTRATOR',
    'GLOBALADMINISTRATOR',
    'ADMINISTRATOR'
  ].some((roleCode) => roleCodes.has(roleCode));
}
'''

VIEW_AS_LOAD_USERS = r'''  const loadUsers = useCallback(async () => {
    const session = readAuthSession();
    const active = readActiveViewAs();
    setActiveViewAs(active);

    if (!session?.sessionToken) {
      setUsers([]);
      setLoadState(active ? 'error' : 'hidden');
      setLoadError(active ? 'Your administrator session is unavailable. Exit View-As and sign in again.' : '');
      return;
    }

    const sequence = ++requestSequence.current;
    setLoadState((current) => current === 'ready' ? 'refreshing' : 'loading');
    setLoadError('');

    try {
      const requestFetch = window.__projectPulseOriginalFetch || window.fetch.bind(window);

      if (!active) {
        const contextResponse = await requestFetch('/api/security/context', {
          method: 'GET',
          credentials: 'include',
          cache: 'no-store',
          headers: {
            'X-ProjectPulse-Session': session.sessionToken,
            'Cache-Control': 'no-cache, no-store',
            Pragma: 'no-cache'
          }
        });

        if (sequence !== requestSequence.current) return;

        if (!contextResponse.ok) {
          setUsers([]);
          setLoadState('hidden');
          setLoadError('');
          return;
        }

        const securityContext = await contextResponse.json().catch(() => ({}));
        if (!securityContextAllowsViewAs(securityContext)) {
          setUsers([]);
          setLoadState('hidden');
          setLoadError('');
          return;
        }
      }

      const response = await requestFetch(VIEW_AS_USERS_ENDPOINT, {
        method: 'GET',
        credentials: 'include',
        cache: 'no-store',
        headers: {
          'X-ProjectPulse-Session': session.sessionToken,
          'Cache-Control': 'no-cache, no-store',
          Pragma: 'no-cache'
        }
      });

      if (sequence !== requestSequence.current) return;

      if (!response.ok) {
        setUsers([]);
        setLoadState(active ? 'error' : 'hidden');
        setLoadError(active
          ? 'The eligible-user list could not be refreshed. You can still exit the active preview.'
          : '');
        return;
      }

      const body = await response.json().catch(() => ({}));
      const eligibleUsers = Array.isArray(body?.users)
        ? body.users.filter((user) => user?.userId)
        : [];

      setUsers(eligibleUsers);
      setLoadState(eligibleUsers.length || active ? 'ready' : 'hidden');
    } catch {
      if (sequence !== requestSequence.current) return;
      setUsers([]);
      setLoadState(active ? 'error' : 'hidden');
      setLoadError(active
        ? 'The eligible-user list could not be refreshed. You can still exit the active preview.'
        : '');
    }
  }, []);'''

VIEW_AS_EFFECT = r'''  useEffect(() => {
    const timer = window.setTimeout(loadUsers, 250);
    const synchronize = () => {
      setActiveViewAs(readActiveViewAs());
      void loadUsers();
    };
    const onStorage = (event) => {
      if (event.key === VIEW_AS_STORAGE_KEY || event.key === AUTH_SESSION_STORAGE_KEY) synchronize();
    };

    window.addEventListener('storage', onStorage);
    window.addEventListener('projectpulse:auth-session-ready', loadUsers);
    window.addEventListener(VIEW_AS_CHANGED_EVENT, synchronize);

    return () => {
      requestSequence.current += 1;
      window.clearTimeout(timer);
      window.removeEventListener('storage', onStorage);
      window.removeEventListener('projectpulse:auth-session-ready', loadUsers);
      window.removeEventListener(VIEW_AS_CHANGED_EVENT, synchronize);
    };
  }, [loadUsers]);'''


def patch_view_as_drawer(source: str) -> str:
    if "securityContextAllowsViewAs" in source:
        raise RuntimeError("GlobalViewAsDrawer.jsx already contains the capability preflight")

    source = replace_once(
        source,
        '}\n\nfunction roleLabel',
        '}' + VIEW_AS_AUTHORITY_HELPER + '\nfunction roleLabel',
        "View-As authority helper insertion",
    )
    source = regex_once(
        source,
        r'  const loadUsers = useCallback\(async \(\) => \{[\s\S]*?\n  \}, \[\]\);',
        VIEW_AS_LOAD_USERS,
        "View-As loadUsers replacement",
    )
    source = regex_once(
        source,
        r'  useEffect\(\(\) => \{\n    const timers = \[250, 1200, 3000\][\s\S]*?\n  \}, \[loadUsers\]\);',
        VIEW_AS_EFFECT,
        "View-As retry removal",
    )
    return source


def patch_app(source: str) -> str:
    return replace_once(
        source,
        'installProjectPulseGlobalViewAsPreview();',
        '/* Legacy DOM View-As preview disabled; GlobalViewAsDrawer is the single React-owned authority. */',
        "legacy View-As bootstrap disablement",
    )


TEST_CONTENT = r'''import assert from 'node:assert/strict';
import fs from 'node:fs';

const read = (path) => fs.readFileSync(path, 'utf8');
const program = read('src/backend/ProjectTime.Api/Program.cs');
const teamPanel = read('src/frontend/project-time-web/src/EngineeringTeamLeadUtilizationPanel.jsx');
const drawer = read('src/frontend/project-time-web/src/GlobalViewAsDrawer.jsx');
const app = read('src/frontend/project-time-web/src/App.jsx');

assert.match(program, /calculationStatus = "calculated"/);
assert.doesNotMatch(program, /calculationStatus = "placeholder"/);
assert.match(program, /WHERE ts\.user_id = @user_id/);
assert.match(program, /timesheet_status NOT IN \('manager_declined', 'rejected', 'voided'\)/);
assert.match(program, /roles\.Contains\("EXECUTIVE"\)/);
assert.match(program, /roles\.Contains\("ENGINEERING_DIRECTOR"\)/);
assert.match(program, /var canUseOwnScope = isEngineer \|\| isEngineeringTeamLead;/);
assert.match(program, /if \(canViewAll \|\| canUseTeamScope\)/);
assert.match(program, /SELECT @user_id/);
assert.match(program, /'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD'/);

assert.match(teamPanel, /canLoadEngineeringTeamSummary/);
assert.match(teamPanel, /fetchJson\('\/api\/security\/context'\)/);
assert.match(teamPanel, /ENGINEERING_TEAM_SCOPE_ROLE_CODES/);
assert.match(teamPanel, /ENGINEERING_ORGANIZATION_SCOPE_ROLE_CODES/);

assert.match(drawer, /securityContextAllowsViewAs/);
assert.match(drawer, /contextResponse = await requestFetch\('\/api\/security\/context'/);
assert.match(drawer, /const timer = window\.setTimeout\(loadUsers, 250\);/);
assert.doesNotMatch(drawer, /\[250, 1200, 3000\]/);
assert.doesNotMatch(drawer, /addEventListener\('hashchange', loadUsers\)/);

assert.match(app, /Legacy DOM View-As preview disabled/);
assert.doesNotMatch(app, /^installProjectPulseGlobalViewAsPreview\(\);$/m);

console.log('UTILIZATION_ROLE_SCOPING_VALIDATION=PASS engineer=self lead=self+team managerDirector=team executive=all viewAs=capability-gated');
'''

CI_CONTENT = r'''name: Utilization Role Scoping CI

on:
  pull_request:
    paths:
      - 'src/backend/ProjectTime.Api/Program.cs'
      - 'src/frontend/project-time-web/src/EngineeringTeamLeadUtilizationPanel.jsx'
      - 'src/frontend/project-time-web/src/GlobalViewAsDrawer.jsx'
      - 'src/frontend/project-time-web/src/App.jsx'
      - 'tests/validate-utilization-role-scoping.mjs'
      - '.github/workflows/utilization-role-scoping-ci.yml'
  workflow_dispatch:

permissions:
  contents: read

jobs:
  validate:
    runs-on: ubuntu-latest
    timeout-minutes: 45
    steps:
      - uses: actions/checkout@v5
      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: '10.0.x'
      - uses: actions/setup-node@v5
        with:
          node-version: '24'
          cache: npm
          cache-dependency-path: src/frontend/project-time-web/package-lock.json
      - name: Validate utilization contracts
        run: node tests/validate-utilization-role-scoping.mjs
      - name: Build API
        run: dotnet build src/backend/ProjectTime.Api/ProjectTime.Api.csproj --configuration Release
      - name: Validate and build web
        working-directory: src/frontend/project-time-web
        run: |
          npm ci --no-audit --no-fund
          npm run validate:modules003004-rolling-years
          npm run build
'''


def main() -> int:
    try:
        originals = {
            PROGRAM: read(PROGRAM),
            TEAM_PANEL: read(TEAM_PANEL),
            VIEW_AS_DRAWER: read(VIEW_AS_DRAWER),
            APP: read(APP),
        }

        updates = {
            PROGRAM: patch_program(originals[PROGRAM]),
            TEAM_PANEL: patch_team_panel(originals[TEAM_PANEL]),
            VIEW_AS_DRAWER: patch_view_as_drawer(originals[VIEW_AS_DRAWER]),
            APP: patch_app(originals[APP]),
            TEST: TEST_CONTENT,
            CI: CI_CONTENT,
        }

        # All transformations have succeeded before the first write.
        for path, content in updates.items():
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")

        print("UTILIZATION_ROLE_SCOPING_PATCH=PASS")
        for path in updates:
            print(path.relative_to(ROOT))
        return 0
    except Exception as exc:  # noqa: BLE001
        print(f"UTILIZATION_ROLE_SCOPING_PATCH=FAIL {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
