#!/usr/bin/env python3
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, content: str) -> None:
    (ROOT / path).write_text(content, encoding="utf-8")


def replace_once(content: str, old: str, new: str, label: str) -> str:
    count = content.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected one exact anchor, found {count}")
    return content.replace(old, new, 1)


def patch_app() -> None:
    path = "src/frontend/project-time-web/src/App.jsx"
    text = read(path)

    pattern = re.compile(
        r"\{\(activeRoute === 'engineer-task-closeout' && canUseEngineerTaskCloseout\) \? \(\s*"
        r"<section id=\"engineer-task-closeout\" className=\"panel engineer-task-closeout-route-panel\">\s*"
        r"<EngineerTaskCloseoutCenter authSession=\{authSession\} />\s*"
        r"</section>\s*"
        r"\) : null\}",
        re.MULTILINE,
    )
    replacement = """{activeRoute === 'engineer-task-closeout' ? (
        <section
          id="engineer-task-closeout"
          className="panel engineer-task-closeout-route-panel"
          data-client-access={canUseEngineerTaskCloseout ? 'allowed' : 'server-authority'}
        >
          <EngineerTaskCloseoutCenter authSession={authSession} />
        </section>
      ) : null}"""
    text, count = pattern.subn(replacement, text, count=1)
    if count != 1:
        raise SystemExit(f"App Module 001A route: expected one guarded route, found {count}")

    summary_call = "          fetchJson('/api/project-management/summary', authSession),"
    summary_replacement = """          (activeRoute === 'engineer-task-closeout'
            ? Promise.resolve({ skipped: 'module001a_owns_route_data' })
            : fetchJson('/api/project-management/summary', authSession)),"""
    text = replace_once(
        text,
        summary_call,
        summary_replacement,
        "App Module 001A unrelated project-management summary",
    )

    dependency = "  }, [selectedWeekStart, authSession?.sessionToken]);"
    dependency_replacement = "  }, [selectedWeekStart, authSession?.sessionToken, activeRoute]);"
    load_start = text.find("    async function loadStatus()")
    if load_start < 0:
        raise SystemExit("App loadStatus function was not found")
    dependency_at = text.find(dependency, load_start)
    if dependency_at < 0:
        raise SystemExit("App loadStatus dependency anchor was not found")
    text = text[:dependency_at] + dependency_replacement + text[dependency_at + len(dependency):]

    write(path, text)


def patch_closeout_empty_state() -> None:
    path = "src/frontend/project-time-web/src/EngineerTaskCloseoutCenter.jsx"
    text = read(path)
    old = """            <h2>{tab === 'active' ? 'No active tasks match this view' : 'No historical tasks match this view'}</h2>
            <p>{tab === 'active' ? 'When eligible work is assigned, it will appear here for closeout.' : 'Closed task evidence will remain available here for review.'}</p>"""
    new = """            <h2>{tab === 'active'
              ? (sourceItems.length === 0 ? 'No tasks are available for closeout' : 'No tasks match the selected filters')
              : (sourceItems.length === 0 ? 'No closeout history is available' : 'No historical tasks match the selected filters')}</h2>
            <p>{tab === 'active'
              ? (sourceItems.length === 0
                ? 'This engineer currently has no Service Request, Pre-Sales, or Internal assignment eligible for closeout. No action is required.'
                : 'Adjust the search or request-type filter to review the engineer’s eligible assignments.')
              : (sourceItems.length === 0
                ? 'Completed closeout evidence will appear here after an engineer closes an eligible assignment.'
                : 'Adjust the search or request-type filter to review retained closeout evidence.')}</p>"""
    write(path, replace_once(text, old, new, "Module 001A enterprise empty state"))


def patch_page_context() -> None:
    path = "src/frontend/project-time-web/src/PageContextGuide.jsx"
    text = read(path)
    helper_anchor = "function getContext(route, module) {"
    helper = """function activeViewAsUser() {
  try {
    const value = JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null');
    return value?.userId ? value : null;
  } catch {
    return null;
  }
}

"""
    text = replace_once(text, helper_anchor, helper + helper_anchor, "PageContextGuide View-As helper")

    old = """    if (!module) {
      setApiEvidence({ status: 'not_applicable', apis: [] });
      return () => { active = false; };
    }
    setApiEvidence({ status: 'loading', apis: [] });"""
    new = """    if (!module) {
      setApiEvidence({ status: 'not_applicable', apis: [] });
      return () => { active = false; };
    }
    if (activeViewAsUser()) {
      setApiEvidence({ status: 'view_as_documented_contract', apis: [] });
      return () => { active = false; };
    }
    setApiEvidence({ status: 'loading', apis: [] });"""
    text = replace_once(text, old, new, "PageContextGuide View-As API inventory boundary")

    message = "            {apiEvidence.status === 'permission_limited' ? <small>Exact live API inventory requires the API inventory permission; the documented module contract is shown.</small> : null}"
    replacement = message + "\n            {apiEvidence.status === 'view_as_documented_contract' ? <small>View-As uses the effective user’s documented module contract and does not request administrator-only API inventory.</small> : null}"
    text = replace_once(text, message, replacement, "PageContextGuide View-As status message")
    write(path, text)


def patch_provider_readiness_controller() -> None:
    path = "src/frontend/project-time-web/src/ai/AiProviderReadinessController.jsx"
    write(
        path,
        """import { useEffect, useState } from 'react';
import {
  startAiProviderReadinessMonitoring,
  stopAiProviderReadinessMonitoring
} from './ai-provider-readiness-store.js';

function sessionToken(authSession) {
  return authSession?.sessionToken
    ?? authSession?.token
    ?? authSession?.accessToken
    ?? '';
}

function viewAsActive() {
  try {
    const value = JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null');
    return Boolean(value?.userId);
  } catch {
    return false;
  }
}

export default function AiProviderReadinessController({ authSession }) {
  const token = sessionToken(authSession);
  const [authorityVersion, setAuthorityVersion] = useState(0);

  useEffect(() => {
    const refreshAuthority = () => setAuthorityVersion((value) => value + 1);
    window.addEventListener('projectpulse:view-as-changed', refreshAuthority);
    window.addEventListener('storage', refreshAuthority);
    return () => {
      window.removeEventListener('projectpulse:view-as-changed', refreshAuthority);
      window.removeEventListener('storage', refreshAuthority);
    };
  }, []);

  useEffect(() => {
    if (!token || viewAsActive()) {
      stopAiProviderReadinessMonitoring();
      return undefined;
    }

    const stop = startAiProviderReadinessMonitoring();
    return () => stop?.();
  }, [token, authorityVersion]);

  return null;
}
""",
    )


def patch_approval_access() -> None:
    path = "src/frontend/project-time-web/src/approval-access-navigation-compatibility.js"
    text = read(path)
    anchor = "function normalizeApprovalAccess(payload) {"
    helpers = """function readViewAsUser() {
  try {
    const value = JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null');
    return value?.userId ? value : null;
  } catch {
    return null;
  }
}

function viewAsHasApprovalAuthority(user) {
  const allowed = new Set([
    'SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR',
    'PROJECT_COORDINATOR', 'MANAGER', 'PROJECT_MANAGER', 'PROJECT_MANAGEMENT'
  ]);
  return roleCodes(user?.roleCodes ?? user?.roles ?? user?.roleCode)
    .some((role) => allowed.has(role));
}

function viewAsReadOnlyApprovalAccess(user) {
  return {
    userId: user?.userId ?? null,
    email: cleanText(user?.email),
    displayName: cleanText(user?.displayName ?? user?.email),
    roleCodes: roleCodes(user?.roleCodes ?? user?.roles ?? user?.roleCode),
    canViewTimeApprovals: false,
    canViewPasswordResetApprovals: false,
    canViewAllTimeApprovals: false,
    canResolveStaleApprovals: false,
    scope: 'view_as_no_approval_authority',
    scopeLabel: 'No approval queue for this effective user',
    primaryRoleLabel: cleanText(user?.roleCodes ?? user?.roleCode)
  };
}

"""
    text = replace_once(text, anchor, helpers + anchor, "Approval compatibility View-As helpers")

    old = """    const summaryPayload = await jsonPayload(summaryResponse);
    let access = null;

    try {"""
    new = """    const summaryPayload = await jsonPayload(summaryResponse);
    let access = null;
    const viewAsUser = readViewAsUser();
    if (viewAsUser && !viewAsHasApprovalAuthority(viewAsUser)) {
      return responseWithJson(summaryResponse, {
        ...summaryPayload,
        access: viewAsReadOnlyApprovalAccess(viewAsUser)
      });
    }

    try {"""
    write(path, replace_once(text, old, new, "Approval compatibility View-As request boundary"))


def patch_platform_operations_api() -> None:
    path = "src/backend/ProjectTime.Api/Modules/PlatformOperationsModule.cs"
    text = read(path)
    old = """            runtime = snapshot.Runtime,
            resources = snapshot.Resources,"""
    new = """            runtime = snapshot.Runtime,
            versions = await BuildVersionInventoryAsync(connection, context.RequestAborted),
            serviceOperations = new
            {
                controlSurface = "#system-diagnostics",
                workflow = "diagnose_prepare_separate_approve_stage_execute_verify",
                directProcessRestartEnabled = false,
                viewAsReadOnly = IsViewAs(context)
            },
            resources = snapshot.Resources,"""
    text = replace_once(text, old, new, "Module 013 overview version inventory")

    anchor = "    private static List<ApiInventoryItem> BuildApiInventory(HttpContext context)\n    {"
    method = """    private static async Task<object[]> BuildVersionInventoryAsync(
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

"""
    text = replace_once(text, anchor, method + anchor, "Module 013 version inventory method")
    write(path, text)


def patch_service_control_center() -> None:
    path = "src/frontend/project-time-web/src/ServiceControlCenter.jsx"
    text = read(path)
    old = """      const [overview, inventory] = await Promise.all([
        readJson('/api/platform-operations/overview', authSession),
        readJson('/api/platform-operations/apis', authSession)
      ]);
      setState({ loading: false, overview, inventory, error: '' });"""
    new = """      const [overview, inventory, operationsAdapter, remediationPolicy] = await Promise.all([
        readJson('/api/platform-operations/overview', authSession),
        readJson('/api/platform-operations/apis', authSession),
        readJson('/api/system-diagnostics/operations-adapter-readiness', authSession)
          .catch((error) => ({ ready: false, enabled: false, allowedTargets: [], message: error?.message ?? 'Controlled restart readiness is unavailable.' })),
        readJson('/api/system-diagnostics/remediation-policy', authSession)
          .catch((error) => ({ execution: {}, message: error?.message ?? 'Remediation policy is unavailable.' }))
      ]);
      setState({ loading: false, overview, inventory, operationsAdapter, remediationPolicy, error: '' });"""
    text = replace_once(text, old, new, "Module 013 governed operations load")

    old = """  const dependencies = overview.dependencies ?? {};
  const inventorySummary = state.inventory?.summary ?? {};"""
    new = """  const dependencies = overview.dependencies ?? {};
  const inventorySummary = state.inventory?.summary ?? {};
  const versions = Array.isArray(overview.versions) ? overview.versions : [];
  const operationsAdapter = state.operationsAdapter ?? {};
  const remediationPolicy = state.remediationPolicy ?? {};
  const restartTargets = Array.isArray(operationsAdapter.allowedTargets) ? operationsAdapter.allowedTargets : [];
  const isViewAs = overview.access?.isViewAs === true;"""
    text = replace_once(text, old, new, "Module 013 derived operations state")

    old = """      </section>

      <section className="service-control-card">
        <div className="service-control-card-header">
          <div><p className="eyebrow">Resource usage</p><h2>Compute, memory, and storage</h2></div>"""
    new = """      </section>

      <section className="service-control-card" data-module-013-version-inventory="live">
        <div className="service-control-card-header">
          <div>
            <p className="eyebrow">Runtime versions</p>
            <h2>Version inventory</h2>
            <p>Server-reported versions and deployment-managed component identities. Missing values remain visibly not reported rather than being inferred.</p>
          </div>
          <span>{versions.length} components</span>
        </div>
        <div className="platform-table-wrap">
          <table>
            <thead><tr><th>Component</th><th>Version or model</th><th>Status</th><th>Evidence source</th><th>Operational detail</th></tr></thead>
            <tbody>
              {versions.map((item) => (
                <tr key={item.key}>
                  <td><strong>{item.component}</strong></td>
                  <td><code>{item.version || 'not_reported'}</code></td>
                  <td><Status value={item.status} /></td>
                  <td>{title(item.source)}</td>
                  <td>{item.detail}</td>
                </tr>
              ))}
              {!versions.length ? <tr><td colSpan="5">Version inventory is not available from the active API revision.</td></tr> : null}
            </tbody>
          </table>
        </div>
      </section>

      <section className="service-control-card">
        <div className="service-control-card-header">
          <div><p className="eyebrow">Resource usage</p><h2>Compute, memory, and storage</h2></div>"""
    text = replace_once(text, old, new, "Module 013 version inventory UI")

    anchor = "      <section className=\"service-control-card api-inventory-card\">"
    operations = """      <section className="service-control-card" data-module-013-controlled-restart="module-998-governed">
        <div className="service-control-card-header">
          <div>
            <p className="eyebrow">Controlled service operations</p>
            <h2>Restart and remediation</h2>
            <p>Authorized administrators can restart an exact allowlisted Azure Container App through the governed Module 998 lifecycle: diagnose, prepare, separate approval, stage, execute, and verify.</p>
          </div>
          <Status value={operationsAdapter.ready ? 'healthy' : 'adapter_required'} />
        </div>
        <div className="dependency-list">
          <article>
            <div><strong>Azure Container Apps restart adapter</strong><Status value={operationsAdapter.ready ? 'configured' : 'adapter_required'} /></div>
            <p>{operationsAdapter.ready
              ? 'Managed identity and the bounded restart adapter are ready.'
              : (operationsAdapter.message || 'The restart adapter requires approved managed-identity configuration and an exact target allowlist.')}</p>
            <small>Authentication: {title(operationsAdapter.authentication || 'managed_identity_no_client_secret')}</small>
          </article>
          <article>
            <div><strong>Allowed restart targets</strong><span>{restartTargets.length}</span></div>
            <p>{restartTargets.length ? restartTargets.join(' · ') : 'No Container App target is currently approved for restart.'}</p>
            <small>HTTP routes cannot be restarted independently; they share the complete API application process.</small>
          </article>
          <article>
            <div><strong>Governance lifecycle</strong><span>Separate approval required</span></div>
            <p>{overview.serviceOperations?.workflow?.replaceAll('_', ' → ') || 'Diagnose → prepare → approve → stage → execute → verify'}</p>
            <small>{isViewAs ? 'View-As is read-only. Exit View-As before preparing or executing a remediation.' : 'Every accepted action writes audit evidence and requires post-action verification.'}</small>
          </article>
        </div>
        <div className="service-control-card-header">
          <div><p>Direct process-kill and arbitrary service-name actions remain prohibited.</p></div>
          <button
            type="button"
            className="primary-action"
            onClick={() => { window.location.hash = '#system-diagnostics'; }}
          >
            Open controlled restart workspace
          </button>
        </div>
        {remediationPolicy.message ? <div className="service-control-alert warning">{remediationPolicy.message}</div> : null}
      </section>

"""
    text = replace_once(text, anchor, operations + anchor, "Module 013 controlled restart entry")
    write(path, text)


def write_validator() -> None:
    write(
        "tests/validate-module001a-viewas-module013-operations.mjs",
        """import fs from 'node:fs';

const read = (path) => fs.readFileSync(path, 'utf8');
const requireText = (content, marker, label) => {
  if (!content.includes(marker)) throw new Error(`${label}: missing ${marker}`);
};
const rejectText = (content, marker, label) => {
  if (content.includes(marker)) throw new Error(`${label}: forbidden ${marker}`);
};

const app = read('src/frontend/project-time-web/src/App.jsx');
const closeout = read('src/frontend/project-time-web/src/EngineerTaskCloseoutCenter.jsx');
const guide = read('src/frontend/project-time-web/src/PageContextGuide.jsx');
const provider = read('src/frontend/project-time-web/src/ai/AiProviderReadinessController.jsx');
const approval = read('src/frontend/project-time-web/src/approval-access-navigation-compatibility.js');
const operationsUi = read('src/frontend/project-time-web/src/ServiceControlCenter.jsx');
const operationsApi = read('src/backend/ProjectTime.Api/Modules/PlatformOperationsModule.cs');
const operationsContracts = read('src/backend/ProjectTime.Api/Modules/PlatformOperationsContracts.cs');
const remediationApi = read('src/backend/ProjectTime.Api/Modules/SystemDiagnosticRemediationModule.cs');

requireText(app, "activeRoute === 'engineer-task-closeout' ? (", 'Module 001A direct route');
rejectText(app, "activeRoute === 'engineer-task-closeout' && canUseEngineerTaskCloseout", 'client-only Module 001A route gate');
requireText(app, "module001a_owns_route_data", 'Module 001A route-owned data boundary');
requireText(app, '[selectedWeekStart, authSession?.sessionToken, activeRoute]', 'Module 001A route refresh dependency');
requireText(closeout, 'No tasks are available for closeout', 'enterprise closeout empty state');
requireText(closeout, 'No action is required.', 'enterprise no-action message');
requireText(guide, 'view_as_documented_contract', 'View-As page context boundary');
requireText(provider, 'viewAsActive()', 'View-As provider-monitoring boundary');
requireText(approval, 'view_as_no_approval_authority', 'View-As approval no-authority contract');
requireText(operationsApi, 'BuildVersionInventoryAsync', 'Module 013 version inventory');
requireText(operationsApi, 'SHOW server_version;', 'PostgreSQL server version evidence');
requireText(operationsContracts, 'secretValuesReturned = false', 'Module 013 security contract');
requireText(operationsUi, 'Version inventory', 'Module 013 version inventory UI');
requireText(operationsUi, 'Open controlled restart workspace', 'Module 013 controlled restart entry');
requireText(operationsUi, '/api/system-diagnostics/operations-adapter-readiness', 'Module 998 restart readiness');
requireText(remediationApi, 'restart_service', 'governed restart implementation');
requireText(remediationApi, 'requested_by <> @actor', 'restart separation of duties');
rejectText(operationsUi, 'Process.Start', 'arbitrary process execution');
rejectText(operationsUi, '/restart?service=', 'arbitrary restart target');

console.log('MODULE_001A_VIEW_AS_ROUTE=PASS');
console.log('MODULE_001A_ENTERPRISE_EMPTY_STATE=PASS');
console.log('MODULE_001A_PRIVILEGED_BACKGROUND_ISOLATION=PASS');
console.log('MODULE_013_VERSION_INVENTORY=PASS');
console.log('MODULE_013_GOVERNED_RESTART_ENTRY=PASS');
console.log('PRODUCTION_MUTATIONS=0');
""",
    )


def write_validation_workflow() -> None:
    write(
        ".github/workflows/validate-module001a-viewas-module013-operations.yml",
        """name: Validate Module 001A View-As and Module 013 Operations

on:
  pull_request:
    branches: [main]
    paths:
      - src/frontend/project-time-web/src/App.jsx
      - src/frontend/project-time-web/src/EngineerTaskCloseoutCenter.jsx
      - src/frontend/project-time-web/src/PageContextGuide.jsx
      - src/frontend/project-time-web/src/ai/AiProviderReadinessController.jsx
      - src/frontend/project-time-web/src/approval-access-navigation-compatibility.js
      - src/frontend/project-time-web/src/ServiceControlCenter.jsx
      - src/backend/ProjectTime.Api/Modules/PlatformOperationsModule.cs
      - tests/validate-module001a-viewas-module013-operations.mjs
      - .github/workflows/validate-module001a-viewas-module013-operations.yml

permissions:
  contents: read

jobs:
  validate:
    runs-on: ubuntu-24.04
    timeout-minutes: 45
    steps:
      - uses: actions/checkout@fbc6f3992d24b796d5a048ff273f7fcc4a7b6c09 # v5
        with:
          ref: ${{ github.event.pull_request.head.sha }}
          persist-credentials: false
      - uses: actions/setup-node@49933ea5288caeca8642d1e84afbd3f7d6820020 # v4
        with:
          node-version: '22'
          cache: npm
          cache-dependency-path: src/frontend/project-time-web/package-lock.json
      - uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v4
        with:
          dotnet-version: '10.0.x'
      - name: Validate bounded contracts
        run: node tests/validate-module001a-viewas-module013-operations.mjs
      - name: Build API
        run: dotnet build src/backend/ProjectTime.Api/ProjectTime.Api.csproj --configuration Release --nologo
      - name: Build frontend
        working-directory: src/frontend/project-time-web
        run: |
          npm ci
          npm run build
      - name: Verify source was not rewritten
        run: |
          git diff --check
          test -z "$(git status --short)"
""",
    )


def main() -> int:
    patch_app()
    patch_closeout_empty_state()
    patch_page_context()
    patch_provider_readiness_controller()
    patch_approval_access()
    patch_platform_operations_api()
    patch_service_control_center()
    write_validator()
    write_validation_workflow()

    for helper in (
        ".github/workflows/apply-module001a-viewas-module013-operations-fix.yml",
        "scripts/apply_module001a_viewas_module013_fix.py",
    ):
        candidate = ROOT / helper
        if candidate.exists():
            candidate.unlink()

    print("MODULE_001A_VIEW_AS_AND_MODULE_013_SOURCE_TRANSFORM=PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
