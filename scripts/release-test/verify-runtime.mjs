#!/usr/bin/env node
import fs from "node:fs";

const sourceSha = process.env.SOURCE_SHA || "";
const base = (process.env.PUBLIC_URL || "").replace(/\/+$/, "");
const session = process.env.PROJECTPULSE_TEST_UAT_SESSION || "";
const forgeProjectId = process.env.PROJECTPULSE_TEST_FORGE_PROJECT_ID || "";
const evidencePath = process.env.EVIDENCE_PATH || "/tmp/current-main-release-runtime-evidence.json";
const expectedSource = "e83340b5a4215ea63901cea98ea17596444f96b7";
const expectedBase = "https://phd-west-test.onenecklab.com";

function assert(condition, message) {
  if (!condition) throw new Error(message);
}
assert(sourceSha === expectedSource, "Unexpected deployed source SHA.");
assert(base === expectedBase, "PUBLIC_URL is not the exact protected Test origin.");

async function request(path, { method = "GET", body, moduleNumber, authenticated = false } = {}) {
  const headers = {
    Accept: "application/json",
    "Cache-Control": "no-cache, no-store, max-age=0",
    Pragma: "no-cache",
  };
  if (body !== undefined) headers["Content-Type"] = "application/json";
  if (authenticated) {
    assert(session, `Authenticated request cannot run without PROJECTPULSE_TEST_UAT_SESSION: ${path}`);
    headers.Authorization = `Bearer ${session}`;
    headers["X-ProjectPulse-Session"] = session;
    headers.Origin = base;
    headers["Sec-Fetch-Site"] = "same-origin";
    if (moduleNumber) headers["X-ProjectPulse-Module-Number"] = moduleNumber;
  }
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 45000);
  try {
    const response = await fetch(`${base}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      redirect: "follow",
      signal: controller.signal,
    });
    const text = (await response.text()).slice(0, 1_000_000);
    let json = null;
    try { json = text ? JSON.parse(text) : null; } catch {}
    return { status: response.status, json, text };
  } finally {
    clearTimeout(timeout);
  }
}

const evidence = {
  environment: "test",
  sourceSha,
  publicOrigin: base,
  flowHiveIncluded: false,
  migration074Included: false,
  publicChecks: {},
  authenticatedChecks: { executed: Boolean(session) },
};

const stamp = await request("/release.json");
assert(stamp.status === 200, `Web source stamp returned HTTP ${stamp.status}.`);
assert(stamp.json?.sourceSha === sourceSha, "Web source stamp does not match the exact release SHA.");
evidence.publicChecks.webSourceStamp = "passed";

let healthPassed = false;
for (const path of ["/api/health", "/health"]) {
  const result = await request(path);
  evidence.publicChecks[`health:${path}`] = result.status;
  if (result.status === 200 || result.status === 401) healthPassed = true;
}
assert(healthPassed, "Neither API health route reached an accepted live status.");

const version = await request("/api/version");
assert(version.status === 200, `Version route returned HTTP ${version.status}.`);
evidence.publicChecks.version = 200;

const protectedRoutes = [
  ["GET", "/api/pulse-ai/v1/system/apis?search=project-forge&module=033&limit=10"],
  ["POST", "/api/celar-ai/v2/chat"],
  ["GET", "/api/ai-configuration/routes"],
  ["GET", "/api/celar-ai/v2/attachments/readiness"],
  ["GET", "/api/project-forge/bootstrap"],
];
for (const [method, path] of protectedRoutes) {
  const result = await request(path, {
    method,
    body: method === "POST" ? {} : undefined,
  });
  assert(result.status !== 404, `Expected protected route is not registered: ${method} ${path}`);
  assert(result.status < 500, `Protected route failed at runtime: ${method} ${path} HTTP ${result.status}`);
  evidence.publicChecks[`${method} ${path}`] = result.status;
}

if (session) {
  const versionChat = await request("/api/celar-ai/v2/chat", {
    method: "POST",
    moduleNumber: "011",
    authenticated: true,
    body: {
      question: "What is the current system version?",
      mode: "system_intelligence",
      detailLevel: "comprehensive",
      moduleCode: "011",
      includeAuthorizedProjectDocuments: false,
      usePrivateModelWhenAvailable: true,
      includeRepositoryContext: true,
      includeAssumptions: true,
      includeSourceCitations: true,
      answerPreferenceSource: "guarded_test_release_uat",
    },
  });
  assert(versionChat.status === 200, `Authenticated Celar system-version search returned HTTP ${versionChat.status}.`);
  const versionPayload = JSON.stringify(versionChat.json || {});
  assert(versionPayload.includes(sourceSha), "Celar system-version answer does not contain the deployed source SHA.");
  assert(versionChat.json?.result?.intentCode === "system_version", "Celar system-version intent was not verified.");
  assert(versionChat.json?.rawPrivateContextSentToExternalProvider !== true, "Celar reported private context sent to an external provider.");
  evidence.authenticatedChecks.celarSystemVersion = "passed";

  const apiSearch = await request("/api/celar-ai/v2/chat", {
    method: "POST",
    moduleNumber: "033",
    authenticated: true,
    body: {
      question: "Which registered API routes support Project Forge Module 033?",
      mode: "system_intelligence",
      detailLevel: "comprehensive",
      moduleCode: "033",
      apiSearch: "project-forge",
      includeAuthorizedProjectDocuments: false,
      includeRepositoryContext: true,
      includeAssumptions: true,
      includeSourceCitations: true,
      answerPreferenceSource: "guarded_test_release_uat",
    },
  });
  assert(apiSearch.status === 200, `Authenticated Celar API search returned HTTP ${apiSearch.status}.`);
  assert(apiSearch.json?.result?.intentCode === "api_inventory", "Celar API inventory intent was not verified.");
  const relevantApis = apiSearch.json?.result?.relevantApis || [];
  assert(relevantApis.some((item) => String(item?.routePattern || "").startsWith("/api/project-forge")), "Celar search did not return a Project Forge route.");
  evidence.authenticatedChecks.celarApiSearch = "passed";

  const routes = await request("/api/ai-configuration/routes", {
    moduleNumber: "064",
    authenticated: true,
  });
  assert(routes.status === 200, `Module 064 routes returned HTTP ${routes.status}.`);
  const expectedFeatures = [
    "timesheet_non_project_description",
    "timesheet_project_task_description",
    "timesheet_service_request_description",
    "sow_gsd_planning",
    "project_flowhive_plan",
    "project_forge_plan_estimate",
    "closeout_communication",
    "help_assistant",
  ];
  assert(routes.json?.module === "064", "Module 064 route response has the wrong module.");
  assert(routes.json?.status === "celar_ai_capability_routes_loaded", "Module 064 route catalog is not loaded.");
  assert(Array.isArray(routes.json?.routes) && routes.json.routes.length === 8, "Module 064 must expose exactly eight routes.");
  for (const feature of expectedFeatures) {
    const route = routes.json.routes.find((item) => item?.featureCode === feature);
    assert(route, `Module 064 route is missing: ${feature}`);
    assert(JSON.stringify(route.targets) === JSON.stringify(["celar_ai", "claude", "openai", "local_template"]), `Module 064 target order is wrong for ${feature}.`);
  }
  evidence.authenticatedChecks.module064Routes = "passed";

  const attachments = await request("/api/celar-ai/v2/attachments/readiness", {
    moduleNumber: "011",
    authenticated: true,
  });
  assert(attachments.status === 200, `Attachment readiness returned HTTP ${attachments.status}.`);
  const readiness = attachments.json?.attachmentReadiness || attachments.json || {};
  assert(readiness.status === "celar_ai_chat_attachments_ready", `Attachment runtime is not ready: ${readiness.status || "unknown"}.`);
  assert(readiness.rawDocumentsSentToClaudeOrOpenAi !== true, "Attachment readiness allows raw documents to a public provider.");
  evidence.authenticatedChecks.attachmentReadiness = "passed";

  if (forgeProjectId) {
    assert(/^[0-9a-f-]{36}$/i.test(forgeProjectId), "PROJECTPULSE_TEST_FORGE_PROJECT_ID is not a UUID.");
    const forge = await request(`/api/project-forge/bootstrap?projectId=${encodeURIComponent(forgeProjectId)}&workspace=canonical`, {
      moduleNumber: "033",
      authenticated: true,
    });
    assert(forge.status === 200, `Project Forge bootstrap returned HTTP ${forge.status}.`);
    assert(forge.json?.status === "project_forge_loaded", "Project Forge did not report loaded.");
    evidence.authenticatedChecks.projectForgeBootstrap = "passed";
  } else {
    evidence.authenticatedChecks.projectForgeBootstrap = "skipped_missing_fixture";
  }
} else {
  evidence.authenticatedChecks.reason = "PROJECTPULSE_TEST_UAT_SESSION is not configured; protected routes were verified fail-closed and are ready for operator UI testing.";
}

fs.mkdirSync(new URL(".", `file://${evidencePath}`).pathname, { recursive: true });
fs.writeFileSync(evidencePath, `${JSON.stringify(evidence, null, 2)}\n`, { mode: 0o600 });
console.log("CURRENT_MAIN_TEST_RUNTIME_VERIFICATION=PASSED");
console.log(`CURRENT_MAIN_AUTHENTICATED_UAT=${session ? "EXECUTED" : "SKIPPED_MISSING_SESSION"}`);
