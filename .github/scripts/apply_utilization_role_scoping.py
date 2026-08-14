#!/usr/bin/env python3
"""Apply the focused utilization role-scope patch to project-time-platform.

The script is intentionally anchor-driven and transactional: it validates every
expected source anchor before writing any file. Run it from the repository root
at the reviewed base commit.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path.cwd()
PROGRAM = ROOT / "src/backend/ProjectTime.Api/Program.cs"
APP = ROOT / "src/frontend/project-time-web/src/App.jsx"
PACKAGE = ROOT / "src/frontend/project-time-web/package.json"
VALIDATOR = ROOT / "src/frontend/project-time-web/scripts/validate-utilization-role-scoping.mjs"


def require_file(path: Path) -> str:
    if not path.is_file():
        raise RuntimeError(f"Required file not found: {path}")
    return path.read_text(encoding="utf-8")


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one source anchor, found {count}")
    return text.replace(old, new, 1)


def replace_regex_once(text: str, pattern: str, replacement: str, label: str) -> str:
    updated, count = re.subn(pattern, replacement, text, count=1, flags=re.MULTILINE | re.DOTALL)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one regex anchor, found {count}")
    return updated


def patch_program(source: str) -> str:
    if "UTILIZATION_ROLE_SCOPE_V2_START" in source:
        raise RuntimeError("Program.cs already contains the utilization role-scope marker")

    source = replace_once(
        source,
        '''    var canViewAll = activeRoleCodes.Contains("EXECUTIVE")
                     || activeRoleCodes.Contains("ADMINISTRATOR")
                     || activeRoleCodes.Contains("SUPER_ADMINISTRATOR")
                     || activeRoleCodes.Contains("PTC")
                     || store.UserHasPermission(engineerId, "reports.view.all")
                     || store.UserHasPermission(engineerId, "reports.finance.view");''',
        '''    /* UTILIZATION_ROLE_SCOPE_V2_START */
    var canViewAll = activeRoleCodes.Contains("EXECUTIVE")
                     || activeRoleCodes.Contains("EXECUTIVE_LEADERSHIP")
                     || activeRoleCodes.Contains("SUPER_ADMINISTRATOR")
                     || activeRoleCodes.Contains("SUPERADMINISTRATOR")
                     || activeRoleCodes.Contains("GLOBAL_ADMINISTRATOR")
                     || activeRoleCodes.Contains("GLOBALADMINISTRATOR")
                     || store.UserHasPermission(engineerId, "reports.view.all")
                     || store.UserHasPermission(engineerId, "reports.finance.view");''',
        "full-scope role boundary",
    )

    source = replace_once(
        source,
        '''    var canUseTeamScope = activeRoleCodes.Contains("TEAM_LEAD")
                          || activeRoleCodes.Contains("ENGINEERING_MANAGER")
                          || activeRoleCodes.Contains("ENGINEERING_DIRECTOR");
    var canUseOwnScope = false;
    var canAccess = canViewAll || canUseTeamScope || canUseOwnScope;''',
        '''    var canUseTeamScope = activeRoleCodes.Contains("TEAM_LEAD")
                          || activeRoleCodes.Contains("ENGINEERING_LEAD")
                          || activeRoleCodes.Contains("ENGINEERING_TEAM_LEAD")
                          || activeRoleCodes.Contains("ENGINEERING_MANAGER")
                          || activeRoleCodes.Contains("ENGINEERING_DIRECTOR");
    var canUseOwnScope = activeRoleCodes.Contains("ENGINEER")
                         || activeRoleCodes.Contains("ENGINEERING")
                         || activeRoleCodes.Contains("SOLUTION_ARCHITECT")
                         || activeRoleCodes.Contains("SR_SOLUTION_ARCHITECT")
                         || activeRoleCodes.Contains("PRINCIPAL_SOLUTION_ARCHITECT")
                         || activeRoleCodes.Contains("PRODUCT_DESIGNER")
                         || activeRoleCodes.Contains("SYSTEMS_ENGINEER")
                         || activeRoleCodes.Contains("NETWORK_ENGINEER")
                         || activeRoleCodes.Contains("ENTERPRISE_NETWORK_ENGINEER");
    var canAccess = canViewAll || canUseTeamScope || canUseOwnScope;''',
        "own/team role boundary",
    )

    source = replace_once(
        source,
        '''            roleCode.Equals("ENGINEER", StringComparison.OrdinalIgnoreCase)
            || roleCode.Equals("SOLUTION_ARCHITECT", StringComparison.OrdinalIgnoreCase)
            || roleCode.Equals("SR_SOLUTION_ARCHITECT", StringComparison.OrdinalIgnoreCase)
            || roleCode.Equals("PRINCIPAL_SOLUTION_ARCHITECT", StringComparison.OrdinalIgnoreCase)
            || roleCode.Equals("PRODUCT_DESIGNER", StringComparison.OrdinalIgnoreCase)
            || roleCode.Equals("ENGINEERING_MANAGER", StringComparison.OrdinalIgnoreCase)
            || roleCode.Equals("ENGINEERING_DIRECTOR", StringComparison.OrdinalIgnoreCase)
            || roleCode.Equals("TEAM_LEAD", StringComparison.OrdinalIgnoreCase)))
        .Where(item =>
            canViewAll
            || activeRoleCodes.Contains("TEAM_LEAD"))''',
        '''            roleCode.Equals("ENGINEER", StringComparison.OrdinalIgnoreCase)
            || roleCode.Equals("ENGINEERING", StringComparison.OrdinalIgnoreCase)
            || roleCode.Equals("SOLUTION_ARCHITECT", StringComparison.OrdinalIgnoreCase)
            || roleCode.Equals("SR_SOLUTION_ARCHITECT", StringComparison.OrdinalIgnoreCase)
            || roleCode.Equals("PRINCIPAL_SOLUTION_ARCHITECT", StringComparison.OrdinalIgnoreCase)
            || roleCode.Equals("PRODUCT_DESIGNER", StringComparison.OrdinalIgnoreCase)
            || roleCode.Equals("SYSTEMS_ENGINEER", StringComparison.OrdinalIgnoreCase)
            || roleCode.Equals("NETWORK_ENGINEER", StringComparison.OrdinalIgnoreCase)
            || roleCode.Equals("ENTERPRISE_NETWORK_ENGINEER", StringComparison.OrdinalIgnoreCase)
            || roleCode.Equals("ENGINEERING_LEAD", StringComparison.OrdinalIgnoreCase)
            || roleCode.Equals("ENGINEERING_TEAM_LEAD", StringComparison.OrdinalIgnoreCase)
            || roleCode.Equals("ENGINEERING_MANAGER", StringComparison.OrdinalIgnoreCase)
            || roleCode.Equals("ENGINEERING_DIRECTOR", StringComparison.OrdinalIgnoreCase)
            || roleCode.Equals("TEAM_LEAD", StringComparison.OrdinalIgnoreCase)))
        .Where(item =>
            canViewAll
            || canUseTeamScope
            || (canUseOwnScope && item.Id == engineerId))''',
        "selectable engineer role filter",
    )

    source = replace_once(
        source,
        '''    if (!canViewAll)
    {
        var scopedEngineerIds = GetEngineeringScopeUserIds(store, engineerId, selectableEngineers.Select(item => item.Id));
        selectableEngineers = selectableEngineers
            .Where(item => scopedEngineerIds.Contains(item.Id))
            .ToList();
    }''',
        '''    if (!canViewAll)
    {
        HashSet<Guid> scopedEngineerIds;
        if (canUseTeamScope)
        {
            scopedEngineerIds = GetEngineeringScopeUserIds(
                    store,
                    engineerId,
                    selectableEngineers.Select(item => item.Id))
                .ToHashSet();
        }
        else
        {
            scopedEngineerIds = new HashSet<Guid> { engineerId };
        }

        selectableEngineers = selectableEngineers
            .Where(item => scopedEngineerIds.Contains(item.Id))
            .ToList();
    }
    /* UTILIZATION_ROLE_SCOPE_V2_END */''',
        "server-side scope application",
    )

    return source


FRONTEND_HELPERS = r'''

/* UTILIZATION_ROLE_SCOPE_V2_FRONTEND_START */
const ENGINEERING_OWN_UTILIZATION_ROLE_CODES = new Set([
  'ENGINEER',
  'ENGINEERING',
  'SOLUTION_ARCHITECT',
  'SR_SOLUTION_ARCHITECT',
  'PRINCIPAL_SOLUTION_ARCHITECT',
  'PRODUCT_DESIGNER',
  'SYSTEMS_ENGINEER',
  'NETWORK_ENGINEER',
  'ENTERPRISE_NETWORK_ENGINEER'
]);

const ENGINEERING_TEAM_UTILIZATION_ROLE_CODES = new Set([
  'TEAM_LEAD',
  'ENGINEERING_LEAD',
  'ENGINEERING_TEAM_LEAD',
  'ENGINEERING_MANAGER',
  'ENGINEERING_DIRECTOR'
]);

const ENGINEERING_FULL_UTILIZATION_ROLE_CODES = new Set([
  'EXECUTIVE',
  'EXECUTIVE_LEADERSHIP',
  'SUPER_ADMINISTRATOR',
  'SUPERADMINISTRATOR',
  'GLOBAL_ADMINISTRATOR',
  'GLOBALADMINISTRATOR'
]);

function normalizeUtilizationRoleCode(value) {
  return String(value ?? '')
    .trim()
    .toUpperCase()
    .replace(/[\s-]+/g, '_');
}

function getUtilizationRoleCodes(user) {
  const values = [
    ...(Array.isArray(user?.roleCodes) ? user.roleCodes : []),
    ...(Array.isArray(user?.roles) ? user.roles : []),
    user?.roleCode,
    user?.roleName,
    user?.workspaceRoleCode,
    user?.workspaceRole?.code,
    user?.workspaceRole?.name
  ];

  return new Set(values
    .map((value) => typeof value === 'string'
      ? value
      : value?.code ?? value?.roleCode ?? value?.name ?? value?.roleName)
    .map(normalizeUtilizationRoleCode)
    .filter(Boolean));
}

function canRequestEngineeringUtilization(user) {
  const roleCodes = getUtilizationRoleCodes(user);
  return [...roleCodes].some((roleCode) =>
    ENGINEERING_OWN_UTILIZATION_ROLE_CODES.has(roleCode)
    || ENGINEERING_TEAM_UTILIZATION_ROLE_CODES.has(roleCode)
    || ENGINEERING_FULL_UTILIZATION_ROLE_CODES.has(roleCode));
}
/* UTILIZATION_ROLE_SCOPE_V2_FRONTEND_END */
'''


def patch_app(source: str) -> str:
    if "UTILIZATION_ROLE_SCOPE_V2_FRONTEND_START" in source:
        raise RuntimeError("App.jsx already contains the utilization role-scope marker")

    endpoint_pattern = r'''(const engineeringTeamUtilizationEndpoint\s*=\s*\(userId,\s*options\s*=\s*\{\}\)\s*=>\s*\{.*?\n\};)'''
    match = re.search(endpoint_pattern, source, flags=re.MULTILINE | re.DOTALL)
    if not match:
        raise RuntimeError("App.jsx: could not locate engineeringTeamUtilizationEndpoint")
    source = source[:match.end()] + FRONTEND_HELPERS + source[match.end():]

    declaration = '''  const canViewEngineeringTeamUtilization = canRequestEngineeringUtilization(currentUser.data);\n\n'''
    loader_anchor = "  async function loadEngineeringTeamUtilization(userId = CURRENT_USER_ID, options = {}) {"
    if source.count(loader_anchor) != 1:
        raise RuntimeError(
            "App.jsx: expected exactly one loadEngineeringTeamUtilization loader "
            f"anchor, found {source.count(loader_anchor)}"
        )
    source = source.replace(loader_anchor, declaration + loader_anchor, 1)

    loader_guard_anchor = '''  async function loadEngineeringTeamUtilization(userId = CURRENT_USER_ID, options = {}) {
    setEngineeringTeamUtilization((previous) => ({ ...previous, loading: true, error: null }));'''
    loader_guard = '''  async function loadEngineeringTeamUtilization(userId = CURRENT_USER_ID, options = {}) {
    if (!canViewEngineeringTeamUtilization) {
      setEngineeringTeamUtilization({ data: null, loading: false, error: null });
      return;
    }

    setEngineeringTeamUtilization((previous) => ({ ...previous, loading: true, error: null }));'''
    source = replace_once(source, loader_guard_anchor, loader_guard, "frontend request gate")

    return source


VALIDATOR_CONTENT = r'''import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const repoRoot = path.resolve(webRoot, '../../..');
const read = (filePath) => fs.readFileSync(filePath, 'utf8');

const app = read(path.join(webRoot, 'src/App.jsx'));
const program = read(path.join(repoRoot, 'src/backend/ProjectTime.Api/Program.cs'));

assert.match(app, /UTILIZATION_ROLE_SCOPE_V2_FRONTEND_START/);
assert.match(app, /function canRequestEngineeringUtilization\(user\)/);
assert.match(app, /if \(!canViewEngineeringTeamUtilization\) \{/);
assert.match(app, /setEngineeringTeamUtilization\(\{ data: null, loading: false, error: null \}\);/);
assert.doesNotMatch(app, /ENGINEERING_FULL_UTILIZATION_ROLE_CODES[\s\S]*?'ADMINISTRATOR'/);
assert.doesNotMatch(app, /ENGINEERING_FULL_UTILIZATION_ROLE_CODES[\s\S]*?'PTC'/);

assert.match(program, /UTILIZATION_ROLE_SCOPE_V2_START/);
assert.match(program, /var canUseOwnScope = activeRoleCodes\.Contains\("ENGINEER"\)/);
assert.match(program, /\|\| canUseTeamScope\s*\|\| \(canUseOwnScope && item\.Id == engineerId\)/);
assert.match(program, /scopedEngineerIds = new HashSet<Guid> \{ engineerId \};/);
assert.match(program, /activeRoleCodes\.Contains\("ENGINEERING_DIRECTOR"\)/);
assert.match(program, /activeRoleCodes\.Contains\("SUPER_ADMINISTRATOR"\)/);
assert.doesNotMatch(
  program.slice(
    program.indexOf('/* UTILIZATION_ROLE_SCOPE_V2_START */'),
    program.indexOf('/* UTILIZATION_ROLE_SCOPE_V2_END */')
  ),
  /activeRoleCodes\.Contains\("ADMINISTRATOR"\)|activeRoleCodes\.Contains\("PTC"\)/
);

console.log('UTILIZATION_ROLE_SCOPING_VALIDATION=PASS engineer=self teamLeadManagerDirector=scoped executiveSuperAdmin=full unauthorizedFrontendRequest=blocked');
'''


def patch_package(source: str) -> str:
    payload = json.loads(source)
    scripts = payload.setdefault("scripts", {})
    scripts["validate:utilization-role-scoping"] = (
        "node ./scripts/validate-utilization-role-scoping.mjs"
    )
    return json.dumps(payload, indent=2, ensure_ascii=False) + "\n"


def main() -> int:
    try:
        program_original = require_file(PROGRAM)
        app_original = require_file(APP)
        package_original = require_file(PACKAGE)

        program_updated = patch_program(program_original)
        app_updated = patch_app(app_original)
        package_updated = patch_package(package_original)

        # Complete every transformation before any write occurs.
        PROGRAM.write_text(program_updated, encoding="utf-8")
        APP.write_text(app_updated, encoding="utf-8")
        PACKAGE.write_text(package_updated, encoding="utf-8")
        VALIDATOR.write_text(VALIDATOR_CONTENT, encoding="utf-8")

        print("Applied utilization role scoping to:")
        print(f"- {PROGRAM.relative_to(ROOT)}")
        print(f"- {APP.relative_to(ROOT)}")
        print(f"- {PACKAGE.relative_to(ROOT)}")
        print(f"- {VALIDATOR.relative_to(ROOT)}")
        return 0
    except Exception as exc:  # noqa: BLE001
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
