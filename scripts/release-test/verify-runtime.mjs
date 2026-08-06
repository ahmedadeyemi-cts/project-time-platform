#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import { createHash, randomUUID } from "node:crypto";

const expectedSource = "e2ab37c93b565a4e1d5a94ef5e54efaf3d22e6a3";
const expectedBase = "https://phd-west-test.onenecklab.com";
const expectedTargets = ["celar_ai", "claude", "openai", "local_template"];
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

const sourceSha = process.env.SOURCE_SHA || "";
const base = (process.env.PUBLIC_URL || "").trim().replace(/\/+$/, "");
const session = (process.env.PROJECTPULSE_TEST_UAT_SESSION || "").trim();
const forgeProjectId = (process.env.PROJECTPULSE_TEST_FORGE_PROJECT_ID || "").trim().toLowerCase();
const evidencePath = process.env.EVIDENCE_PATH || "/tmp/current-main-release-runtime-evidence.json";
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const authenticatedUatEnabled = session.length >= 20 && uuidPattern.test(forgeProjectId);
const officialLogoSha256 = "f28a48b72d16d5a2d0377d559ba0a549f4486309cc6e09a285a32840e0df806b";

function assert(condition, message) {
  if (!condition) throw new Error(message);
}
function sleep(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}
function safeError(error) {
  const message = error instanceof Error ? error.message : String(error);
  return message.replace(/[A-Za-z0-9+/_=-]{40,}/g, "[redacted]").slice(0, 500);
}

function answerContainsApiDetail(answer) {
  return /\b(?:api|apis|endpoint|endpoints|route|routes|http|https|swagger)\b|\/(?:api)(?:\/|\b)/i
    .test(JSON.stringify(answer || {}));
}

function sentenceCount(value) {
  return String(value || "")
    .split(/[.!?](?:\s|$)/)
    .map((item) => item.trim())
    .filter(Boolean)
    .length;
}

function assertPopulatedList(value, label, minimum = 1) {
  assert(Array.isArray(value), label + " is not an array.");
  assert(value.length >= minimum, label + " was not automatically populated.");
  assert(value.every((item) => String(item || "").trim().length > 0), label + " contains an empty value.");
}

function assertDetailedPlanningTask(task, label, requirePriority = true) {
  assert(String(task?.wbs || "").trim().length > 0, label + " has no WBS value.");
  assert(String(task?.phase || "").trim().length > 0, label + " has no phase.");
  assert(String(task?.name || "").trim().length > 0, label + " has no name.");
  assert(String(task?.description || "").trim().length >= 80, label + " description is not customer-ready or sufficiently detailed.");
  assertPopulatedList(task?.detailedSteps, label + " detailed steps", 3);
  assert(task.detailedSteps.every((step) => String(step).trim().length >= 60), label + " contains a vague detailed step.");
  assertPopulatedList(task?.inputs, label + " inputs");
  assertPopulatedList(task?.outputs, label + " outputs");
  assertPopulatedList(task?.acceptanceCriteria, label + " acceptance criteria");
  assertPopulatedList(task?.validationSteps, label + " validation steps");
  assertPopulatedList(task?.customerResponsibilities, label + " customer responsibilities");
  assertPopulatedList(task?.usSignalResponsibilities, label + " US Signal responsibilities");
  assertPopulatedList(task?.prerequisites, label + " prerequisites");
  assertPopulatedList(task?.risks, label + " risks");
  assertPopulatedList(task?.openQuestions, label + " open questions");
  assertPopulatedList(task?.requiredRoles, label + " required roles");
  assert(Array.isArray(task?.predecessors), label + " predecessors are missing.");
  assertPopulatedList(task?.citationIds, label + " citations");
  assert(Number(task?.estimatedDurationDays) > 0, label + " has no positive duration estimate.");
  assert(Number(task?.estimatedHours) > 0, label + " has no positive engineering-hours estimate.");
  if (requirePriority) {
    assert(["low", "normal", "high", "critical"].includes(String(task?.priority || "").toLowerCase()), label + " has an invalid priority.");
  }
}

function assertDetailedPlan(plan, label) {
  assert(String(plan?.objective || "").trim().length >= 80, label + " objective is not sufficiently detailed.");
  assert(Array.isArray(plan?.tasks) && plan.tasks.length > 0, label + " returned no structured tasks.");
  plan.tasks.forEach((task, index) => assertDetailedPlanningTask(task, label + " task " + (index + 1)));
  assert(Array.isArray(plan?.milestones) && plan.milestones.length > 0, label + " returned no milestones.");
  for (const [index, milestone] of plan.milestones.entries()) {
    assert(String(milestone?.name || "").trim().length > 0, label + " milestone " + (index + 1) + " has no name.");
    assert(String(milestone?.description || "").trim().length >= 60, label + " milestone " + (index + 1) + " is not detailed.");
    assertPopulatedList(milestone?.acceptanceEvidence, label + " milestone " + (index + 1) + " acceptance evidence");
    assertPopulatedList(milestone?.citationIds, label + " milestone " + (index + 1) + " citations");
  }
  assertPopulatedList(plan?.dependencies, label + " dependencies");
  assertPopulatedList(plan?.requiredRoles, label + " required roles");
  assertPopulatedList(plan?.assumptions, label + " assumptions");
  assertPopulatedList(plan?.risks, label + " risks");
  assertPopulatedList(plan?.outOfScopeItems, label + " out-of-scope items");
  assertPopulatedList(plan?.openQuestions, label + " open questions");
  assert(Array.isArray(plan?.conflicts), label + " conflicts section is missing.");
  assertPopulatedList(plan?.citationIds, label + " citations");
}

assert(sourceSha === expectedSource, "Unexpected deployed source SHA.");
assert(base === expectedBase, "PUBLIC_URL is not the exact protected Test origin.");
assert(
  (session.length === 0 && forgeProjectId.length === 0) || authenticatedUatEnabled,
  "PROJECTPULSE_TEST_UAT_SESSION and PROJECTPULSE_TEST_FORGE_PROJECT_ID must be configured together for automated authenticated UAT.",
);

async function request(route, options = {}) {
  const method = options.method || "GET";
  const authenticated = options.authenticated === true;
  const headers = {
    Accept: options.accept || "application/json",
    "Cache-Control": "no-cache, no-store, max-age=0",
    Pragma: "no-cache",
  };
  if (authenticated) {
    headers.Authorization = "Bearer " + session;
    headers["X-ProjectPulse-Session"] = session;
    headers.Origin = base;
    headers["Sec-Fetch-Site"] = "same-origin";
    if (options.moduleNumber) headers["X-ProjectPulse-Module-Number"] = options.moduleNumber;
  }
  let body;
  if (options.formData) {
    body = options.formData;
  } else if (options.body !== undefined) {
    headers["Content-Type"] = "application/json";
    body = JSON.stringify(options.body);
  }
  const target = new URL(route, base);
  assert(target.origin === new URL(base).origin, "Verifier refused a cross-origin request.");
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), options.timeoutMs || 60000);
  try {
    const response = await fetch(target, {
      method,
      headers,
      body,
      redirect: "manual",
      signal: controller.signal,
    });
    assert(response.status < 300 || response.status >= 400, "Verifier refused an HTTP redirect for " + method + " " + route + ".");
    const maxTextLength = Number.isSafeInteger(options.maxTextLength)
      ? options.maxTextLength
      : 1_000_000;
    const text = (await response.text()).slice(0, maxTextLength);
    let json = null;
    try {
      json = text ? JSON.parse(text) : null;
    } catch {}
    return { status: response.status, json, text };
  } finally {
    clearTimeout(timeout);
  }
}

async function requestWithTransientReadinessRetry(route, options = {}) {
  const maximumAttempts = 12;
  let result = null;
  for (let attempt = 1; attempt <= maximumAttempts; attempt += 1) {
    const separator = route.includes("?") ? "&" : "?";
    result = await request(
      route + separator + "release_readiness=" + sourceSha.slice(0, 12) + "-" + attempt,
      options,
    );
    if (![502, 503, 504].includes(result.status)) {
      return { ...result, readinessAttempts: attempt };
    }
    if (attempt < maximumAttempts) await sleep(5000);
  }
  return { ...result, readinessAttempts: maximumAttempts };
}

async function requestBytes(route) {
  const target = new URL(route, base);
  assert(target.origin === new URL(base).origin, "Verifier refused a cross-origin asset request.");
  const response = await fetch(target, {
    headers: { Accept: "image/png", "Cache-Control": "no-cache, no-store, max-age=0" },
    redirect: "manual",
  });
  assert(response.status === 200, "Official US Signal logo asset returned HTTP " + response.status + ".");
  return Buffer.from(await response.arrayBuffer());
}

const evidence = {
  environment: "test",
  sourceSha,
  publicOrigin: base,
  flowHiveIncluded: true,
  migration074Included: true,
  aiReleasePhase: "disabled",
  routingAuthority: "database_managed_active",
  authenticatedUatStatus: authenticatedUatEnabled ? "executing" : "pending_user_session_validation",
  status: "running",
  publicChecks: {},
  authenticatedChecks: {},
  cleanup: {},
};

let conversationId = "";
let attachmentId = "";
let attachmentFileName = "";
let attachmentRevoked = false;
let forgeTaskId = "";
let forgeTaskName = "";
let forgeTaskRevision = 0;
let forgeTaskArchived = false;

function writeEvidence() {
  fs.mkdirSync(path.dirname(evidencePath), { recursive: true });
  fs.writeFileSync(evidencePath, JSON.stringify(evidence, null, 2) + "\n", { mode: 0o600 });
}

async function revokeAttachment() {
  if (!conversationId || attachmentRevoked) return;
  if (!attachmentId && attachmentFileName) {
    const lookup = await request(
      "/api/celar-ai/v2/conversations/" + encodeURIComponent(conversationId) + "/attachments",
      { authenticated: true, moduleNumber: "011" },
    );
    assert(lookup.status === 200, "Attachment cleanup lookup returned HTTP " + lookup.status + ".");
    const matches = (lookup.json?.attachments || []).filter((item) => item?.fileName === attachmentFileName);
    assert(matches.length <= 1, "Attachment cleanup found duplicate release fixtures.");
    attachmentId = String(matches[0]?.attachmentId || "");
    if (!attachmentId) {
      attachmentRevoked = true;
      evidence.cleanup.attachment = "not_persisted";
      return;
    }
  }
  if (!attachmentId) return;
  const result = await request(
    "/api/celar-ai/v2/conversations/" + encodeURIComponent(conversationId) +
      "/attachments/" + encodeURIComponent(attachmentId),
    { method: "DELETE", authenticated: true, moduleNumber: "011" },
  );
  if (result.status === 404) {
    const lookup = await request(
      "/api/celar-ai/v2/conversations/" + encodeURIComponent(conversationId) + "/attachments",
      { authenticated: true, moduleNumber: "011" },
    );
    assert(lookup.status === 200, "Attachment cleanup verification returned HTTP " + lookup.status + ".");
    assert(!(lookup.json?.attachments || []).some((item) => item?.attachmentId === attachmentId), "Attachment remains listed after a 404 cleanup response.");
    attachmentRevoked = true;
    evidence.cleanup.attachment = "already_absent";
    return;
  }
  assert(result.status === 200, "Attachment cleanup returned HTTP " + result.status + ".");
  assert(result.json?.status === "celar_ai_chat_attachment_revoked", "Attachment cleanup did not confirm revocation.");
  assert(result.json?.retrievalEligible === false, "Revoked attachment remains retrieval eligible.");
  attachmentRevoked = true;
  evidence.cleanup.attachment = "revoked";
}

async function archiveForgeTask() {
  if (forgeTaskArchived) return;
  if (!forgeTaskId && forgeTaskName) {
    const lookup = await request(
      "/api/project-forge/bootstrap?projectId=" + encodeURIComponent(forgeProjectId) + "&workspace=canonical",
      { authenticated: true, moduleNumber: "033" },
    );
    assert(lookup.status === 200, "Project Forge cleanup lookup returned HTTP " + lookup.status + ".");
    const matches = (lookup.json?.tasks || []).filter((item) => item?.taskName === forgeTaskName);
    assert(matches.length <= 1, "Project Forge cleanup found duplicate release fixtures.");
    forgeTaskId = String(matches[0]?.taskId || "");
    forgeTaskRevision = Number(matches[0]?.revision || matches[0]?.taskRevision || 0);
    if (!forgeTaskId) {
      forgeTaskArchived = true;
      evidence.cleanup.projectForgeTask = "not_persisted";
      return;
    }
  }
  if (!forgeTaskId) return;
  assert(Number.isInteger(forgeTaskRevision) && forgeTaskRevision > 0, "Project Forge cleanup has no valid revision.");
  const result = await request("/api/project-forge/tasks/" + encodeURIComponent(forgeTaskId), {
    method: "DELETE",
    authenticated: true,
    moduleNumber: "033",
    body: {
      recordSource: "canonical",
      planId: null,
      expectedRevision: forgeTaskRevision,
      clientMutationId: randomUUID(),
      reason: "Automated guarded Test UAT cleanup",
    },
  });
  if (result.status === 404) {
    const lookup = await request(
      "/api/project-forge/bootstrap?projectId=" + encodeURIComponent(forgeProjectId) + "&workspace=canonical",
      { authenticated: true, moduleNumber: "033" },
    );
    assert(lookup.status === 200, "Project Forge cleanup verification returned HTTP " + lookup.status + ".");
    assert(!(lookup.json?.tasks || []).some((item) => item?.taskId === forgeTaskId), "Project Forge task remains after a 404 cleanup response.");
    forgeTaskArchived = true;
    evidence.cleanup.projectForgeTask = "already_absent";
    return;
  }
  assert(result.status === 200, "Project Forge cleanup returned HTTP " + result.status + ".");
  assert(result.json?.status === "canonical_task_archived", "Project Forge cleanup did not archive the task.");
  forgeTaskRevision = Number(result.json?.revision || result.json?.task?.revision || forgeTaskRevision);
  forgeTaskArchived = true;
  evidence.cleanup.projectForgeTask = "archived";
}

async function run() {
  const stamp = await request("/release.json");
  assert(stamp.status === 200, "Web source stamp returned HTTP " + stamp.status + ".");
  assert(stamp.json?.sourceSha === sourceSha, "Web source stamp does not match the exact release SHA.");
  evidence.publicChecks.webSourceStamp = "passed";

  const shell = await requestWithTransientReadinessRetry("/", { accept: "text/html" });
  assert(shell.status === 200, "Web application shell returned HTTP " + shell.status + ".");
  evidence.publicChecks.webShellReadinessAttempts = shell.readinessAttempts;
  const scriptPath = shell.text.match(/<script[^>]+src=["']([^"']+\.js)["']/i)?.[1] || "";
  assert(scriptPath.length > 0, "Web application shell did not reference its production bundle.");
  const bundle = await request(scriptPath, {
    accept: "text/javascript",
    maxTextLength: 4_000_000,
  });
  assert(bundle.status === 200, "Web production bundle returned HTTP " + bundle.status + ".");
  assert(bundle.text.includes("brand-logo-image"), "Web production bundle does not mount the main-page US Signal logo.");
  for (const marker of [
    "authorized-typeahead",
    "celar-ai-chat-drag-handle",
    "Type a project name or code",
    "Temporal context graph",
    "Self-monitoring adapters",
    "Project planning command center",
    "Generate and auto-fill detailed plan",
    "/api/project-flowhive",
  ]) {
    assert(bundle.text.includes(marker), "Web production bundle is missing the Celar smart-interaction or context-fabric marker: " + marker + ".");
  }
  evidence.publicChecks.celarSmartInteractionBundle = "passed";
  const logoPath = bundle.text.match(/\/assets\/(?:USSNavyStacked|ussignal)-[A-Za-z0-9_-]+\.png/)?.[0] || "";
  assert(logoPath.length > 0, "Web production bundle does not reference the approved stacked US Signal logo asset.");
  const logoBytes = await requestBytes(logoPath);
  assert(createHash("sha256").update(logoBytes).digest("hex") === officialLogoSha256, "Live US Signal logo bytes do not match the approved governed asset.");
  evidence.publicChecks.officialUsSignalLogo = "passed";

  const health = await request("/health");
  assert(health.status === 200, "API health returned HTTP " + health.status + ".");
  evidence.publicChecks.apiHealth = "passed";

  const version = await request("/api/version");
  assert(version.status === 200, "Version route returned HTTP " + version.status + ".");
  evidence.publicChecks.version = "passed";

  const protectedRoutes = [
    ["GET", "/api/celar-ai/v1/system/apis?search=project-forge&module=033&limit=10"],
    ["POST", "/api/celar-ai/v2/chat"],
    ["GET", "/api/ai-configuration/routes"],
    ["GET", "/api/ai-configuration/knowledge-fabric"],
    ["GET", "/api/celar-ai/v1/production/readiness"],
    ["GET", "/api/celar-ai/v2/attachments/readiness"],
    ["GET", "/api/project-flowhive/readiness"],
    ["GET", authenticatedUatEnabled
      ? "/api/project-forge/bootstrap?projectId=" + encodeURIComponent(forgeProjectId) + "&workspace=canonical"
      : "/api/project-forge/bootstrap?workspace=canonical"],
  ];
  for (const [method, route] of protectedRoutes) {
    const result = await request(route, { method, body: method === "POST" ? {} : undefined });
    assert(result.status === 401, "Protected route did not fail closed with HTTP 401: " + method + " " + route + " returned " + result.status + ".");
    evidence.publicChecks[method + " " + route] = "session_required";
  }

  if (!authenticatedUatEnabled) {
    evidence.authenticatedUatStatus = "pending_user_session_validation";
    evidence.status = "passed";
    return;
  }

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
  assert(versionChat.status === 200, "Authenticated Celar system-version search returned HTTP " + versionChat.status + ".");
  assert(versionChat.json?.result?.intentCode === "system_version", "Celar system-version intent was not verified.");
  assert(String(versionChat.json?.result?.answer?.directConclusion || "").includes(sourceSha), "Celar direct conclusion does not contain the deployed source SHA.");
  assert(versionChat.json?.rawPrivateContextSentToExternalProvider === false, "Celar privacy flag was not strictly false.");
  evidence.authenticatedChecks.celarSystemVersion = "passed";

  const systemNameChat = await request("/api/celar-ai/v2/chat", {
    method: "POST",
    moduleNumber: "011",
    authenticated: true,
    body: {
      question: "What is the system name?",
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
  assert(systemNameChat.status === 200, "Authenticated Celar system-name question returned HTTP " + systemNameChat.status + ".");
  assert(systemNameChat.json?.result?.intentCode === "platform_identity", "Celar system-name intent was not verified.");
  assert(/\bPulse\b/i.test(String(systemNameChat.json?.result?.answer?.directConclusion || "")), "Celar system-name answer did not identify Pulse.");
  assert(Array.isArray(systemNameChat.json?.result?.relevantApis) && systemNameChat.json.result.relevantApis.length === 0, "Celar system-name answer exposed API inventory.");
  assert(Array.isArray(systemNameChat.json?.result?.toolResults) && systemNameChat.json.result.toolResults.length === 0, "Celar system-name answer ran unrelated tools.");
  assert(!answerContainsApiDetail(systemNameChat.json?.result?.answer), "Celar system-name answer included unrequested API, route, endpoint, or HTTP detail.");
  assert(systemNameChat.json?.rawPrivateContextSentToExternalProvider === false, "Celar system-name privacy flag was not strictly false.");
  evidence.authenticatedChecks.celarSystemNameAnswerFirst = "passed";

  const apiSearch = await request("/api/celar-ai/v2/chat", {
    method: "POST",
    moduleNumber: "011",
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
  assert(apiSearch.status === 200, "Authenticated Celar API search returned HTTP " + apiSearch.status + ".");
  assert(apiSearch.json?.result?.intentCode === "api_inventory", "Celar API inventory intent was not verified.");
  assert(String(apiSearch.json?.result?.answer?.directConclusion || "").trim().length > 0, "Celar API search returned no direct conclusion.");
  const relevantApis = apiSearch.json?.result?.relevantApis || [];
  assert(Array.isArray(relevantApis) && relevantApis.length > 0, "Celar search returned no Project Forge routes.");
  assert(
    relevantApis.every((item) =>
      item?.moduleCode === "033" &&
      String(item?.routePattern || "").startsWith("/api/project-forge")),
    "Celar apiSearch/moduleCode filtering returned an out-of-scope route.",
  );
  assert((apiSearch.json?.result?.answer?.citationIds || []).length > 0 || (apiSearch.json?.result?.sources || []).length > 0, "Celar API search returned no source evidence.");
  assert(apiSearch.json?.rawPrivateContextSentToExternalProvider === false, "Celar API-search privacy flag was not strictly false.");

  const noMatchSearch = "project-forge-no-such-route-" + sourceSha.slice(0, 12);
  const noMatch = await request("/api/celar-ai/v2/chat", {
    method: "POST",
    moduleNumber: "011",
    authenticated: true,
    body: {
      question: "Find the exact registered API route named " + noMatchSearch + ".",
      mode: "system_intelligence",
      detailLevel: "comprehensive",
      moduleCode: "033",
      apiSearch: noMatchSearch,
      includeAuthorizedProjectDocuments: false,
      includeRepositoryContext: true,
      includeAssumptions: false,
      includeSourceCitations: true,
      answerPreferenceSource: "guarded_test_release_uat",
    },
  });
  assert(noMatch.status === 200, "Celar no-match API search returned HTTP " + noMatch.status + ".");
  assert(noMatch.json?.result?.intentCode === "api_inventory", "Celar no-match search did not use API inventory intent.");
  assert(Array.isArray(noMatch.json?.result?.relevantApis) && noMatch.json.result.relevantApis.length === 0, "Celar apiSearch ignored an exact no-match filter.");
  assert(noMatch.json?.rawPrivateContextSentToExternalProvider === false, "Celar no-match search privacy flag was not strictly false.");
  evidence.authenticatedChecks.celarApiSearch = "passed";

  const module011Readiness = await request("/api/celar-ai/v1/production/readiness", {
    moduleNumber: "011",
    authenticated: true,
  });
  assert(module011Readiness.status === 200, "Module 011 production readiness returned HTTP " + module011Readiness.status + ".");
  assert(module011Readiness.json?.module === "011", "Module 011 readiness response has the wrong module.");
  assert(module011Readiness.json?.access?.canAsk === true, "The active Test user cannot use Ask Celar AI.");
  assert(module011Readiness.json?.access?.canManage === true, "The actual-session Test Super Administrator cannot manage Module 011.");
  assert(module011Readiness.json?.access?.isViewAs === false, "Module 011 management authority was evaluated through View-As.");
  assert(module011Readiness.json?.access?.mutationAuthorityTransferredByViewAs === false, "Module 011 reported transferred mutation authority.");
  assert(module011Readiness.json?.lifecycle?.databaseConfigured === true, "Module 011 did not resolve the existing Test database configuration.");
  assert(module011Readiness.json?.lifecycle?.schemaReady === true, "Module 011 production schema is not ready.");
  assert(module011Readiness.json?.lifecycle?.status === "celar_ai_production_schema_ready", "Module 011 lifecycle is not database ready.");
  assert(module011Readiness.json?.privateRag?.status === "private_rag_ready", "Module 011 private RAG is not ready.");
  assert(module011Readiness.json?.privateRag?.schemaReady === true, "Module 011 private RAG schema is not ready.");
  assert(module011Readiness.json?.privateRag?.enabled === true, "Module 011 private RAG is not enabled.");
  assert(module011Readiness.json?.chatAttachments?.status === "celar_ai_chat_attachments_ready", "Ask Celar AI private attachment pipeline is not ready.");
  assert(module011Readiness.json?.chatAttachments?.privateRuntimeSchemaReady === true, "Ask Celar AI private runtime schema is not ready.");
  assert(module011Readiness.json?.chatAttachments?.privateWorkerEnabled === true, "Ask Celar AI private worker is not enabled.");
  assert(Array.isArray(module011Readiness.json?.chatAttachments?.blockers) && module011Readiness.json.chatAttachments.blockers.length === 0, "Ask Celar AI private attachment pipeline reported blockers.");
  evidence.authenticatedChecks.module011CoreAskAccess = "passed";
  evidence.authenticatedChecks.module011SuperAdministratorAuthority = "passed";
  evidence.authenticatedChecks.module011DatabaseConfiguration = "passed";
  evidence.authenticatedChecks.module011PrivatePipeline = "passed";

  const flowHiveApis = await request("/api/celar-ai/v1/system/apis?search=project-flowhive&module=066&limit=25", {
    moduleNumber: "011",
    authenticated: true,
  });
  assert(flowHiveApis.status === 200, "Project FlowHive canonical API inventory returned HTTP " + flowHiveApis.status + ".");
  assert(flowHiveApis.json?.status === "live_registered_api_inventory_loaded", "Project FlowHive live API inventory is unavailable.");
  assert(flowHiveApis.json?.filters?.moduleCode === "066", "Project FlowHive API inventory did not retain the Module 066 filter.");
  assert(Array.isArray(flowHiveApis.json?.apis) && flowHiveApis.json.apis.length >= 10, "Project FlowHive API inventory is incomplete.");
  assert(flowHiveApis.json.apis.every((item) => item?.moduleCode === "066" && String(item?.routePattern || "").startsWith("/api/project-flowhive")), "Project FlowHive API inventory contains a mismatched module or route.");
  assert(flowHiveApis.json.apis.every((item) => item?.moduleName === "Project FlowHive"), "Project FlowHive API inventory contains an inconsistent module name.");
  evidence.authenticatedChecks.flowHiveCanonicalApiInventory = "passed";

  const flowHiveReadiness = await request("/api/project-flowhive/readiness", {
    moduleNumber: "066",
    authenticated: true,
  });
  assert(flowHiveReadiness.status === 200, "Project FlowHive readiness returned HTTP " + flowHiveReadiness.status + ".");
  assert(flowHiveReadiness.json?.module === "066" && flowHiveReadiness.json?.moduleName === "Project FlowHive", "Project FlowHive readiness exposes an inconsistent module identity.");
  assert(flowHiveReadiness.json?.route === "project-flowhive" && flowHiveReadiness.json?.apiBase === "/api/project-flowhive", "Project FlowHive readiness exposes an inconsistent route or API base.");
  assert(flowHiveReadiness.json?.ready === true, "Project FlowHive is not production ready after Migration 074.");
  assert(flowHiveReadiness.json?.persistence?.ready === true && flowHiveReadiness.json?.persistence?.status === "project_flowhive_production_ready", "Project FlowHive persistence is not ready after Migration 074.");

  const flowHiveCapabilities = await request("/api/project-flowhive/capabilities", {
    moduleNumber: "066",
    authenticated: true,
  });
  assert(flowHiveCapabilities.status === 200, "Project FlowHive capabilities returned HTTP " + flowHiveCapabilities.status + ".");
  assert(flowHiveCapabilities.json?.module === "066" && flowHiveCapabilities.json?.moduleName === "Project FlowHive", "Project FlowHive capabilities expose an inconsistent module identity.");
  assert(flowHiveCapabilities.json?.route === "project-flowhive" && flowHiveCapabilities.json?.status === "production_ready", "Project FlowHive capabilities do not report production readiness.");
  assert(flowHiveCapabilities.json?.databaseMutationEnabled === true && flowHiveCapabilities.json?.aiExecutionEnabled === true, "Project FlowHive persistence or Celar AI execution is not enabled.");
  assert(flowHiveCapabilities.json?.integration?.aiProvider === "module_064_celar_ai_capability_router", "Project FlowHive is not connected to the Module 064 Celar AI router.");
  assert(flowHiveCapabilities.json?.integration?.sharedRegistration === "production_registered", "Project FlowHive shared registration is not production ready.");

  const flowHivePlans = await request("/api/project-flowhive/plans?projectId=" + encodeURIComponent(forgeProjectId), {
    moduleNumber: "066",
    authenticated: true,
  });
  assert(flowHivePlans.status === 200, "Project FlowHive plan persistence read returned HTTP " + flowHivePlans.status + ".");
  assert(flowHivePlans.json?.module === "066" && flowHivePlans.json?.moduleName === "Project FlowHive", "Project FlowHive plan persistence read exposes an inconsistent module identity.");
  assert(Array.isArray(flowHivePlans.json?.plans), "Project FlowHive plan persistence read did not return a plan collection.");
  evidence.authenticatedChecks.flowHiveProductionReadiness = "passed";
  evidence.authenticatedChecks.flowHivePersistenceRead = "passed";

  const routes = await request("/api/ai-configuration/routes", {
    moduleNumber: "064",
    authenticated: true,
  });
  assert(routes.status === 200, "Module 064 routes returned HTTP " + routes.status + ".");
  assert(routes.json?.module === "064", "Module 064 route response has the wrong module.");
  assert(routes.json?.status === "celar_ai_capability_routes_loaded", "Module 064 route catalog is not loaded.");
  assert(JSON.stringify(routes.json?.defaultOrder) === JSON.stringify(expectedTargets), "Module 064 default order is incorrect.");
  assert(Array.isArray(routes.json?.routes) && routes.json.routes.length === 8, "Module 064 must expose exactly eight routes.");
  assert(routes.json?.controls?.releasePhase === "disabled", "Module 064 did not report the explicit source-only disabled release phase.");
  assert(routes.json?.controls?.deploymentManaged === false, "Module 064 unexpectedly reports deployment-managed routing.");
  assert(routes.json?.controls?.readOnly === false, "Module 064 unexpectedly reports release-scoped read-only routing.");
  assert(routes.json?.controls?.configurationAuthority === "database_managed_active", "Module 064 routing authority is not the explicit database-managed Test mode.");
  assert(routes.json?.controls?.configurationSourceCommit === sourceSha, "Module 064 source binding is incorrect.");
  assert(routes.json?.controls?.catalogCapabilityCount === 8, "Module 064 catalog count is incorrect.");
  const configuredRoutes = new Map();
  for (const feature of expectedFeatures) {
    const route = routes.json.routes.find((item) => item?.feature === feature);
    assert(route, "Module 064 route is missing: " + feature + ".");
    assert(route.persisted === true, "Module 064 route is not persisted: " + feature + ".");
    assert(Number.isInteger(route.revision) && route.revision > 0, "Module 064 route has no persisted revision: " + feature + ".");
    assert(route.deploymentManaged === false && route.readOnly === false, "Module 064 database-managed route is unexpectedly deployment locked: " + feature + ".");
    assert(route.configurationAuthority === "database_managed_active", "Module 064 route has the wrong configuration authority: " + feature + ".");
    assert(Array.isArray(route.targets) && route.targets.length === expectedTargets.length, "Module 064 target count is wrong for " + feature + ".");
    assert(new Set(route.targets).size === expectedTargets.length && expectedTargets.every((target) => route.targets.includes(target)), "Module 064 route is not an exact eligible-target permutation for " + feature + ".");
    assert(route.targets.at(-1) === "local_template", "Module 064 governed local fallback is not last for " + feature + ".");
    configuredRoutes.set(feature, route.targets);
  }
  evidence.authenticatedChecks.module064Routes = Object.fromEntries(configuredRoutes);
  evidence.authenticatedChecks.module064SelectedOrderPreserved = "passed";
  evidence.authenticatedChecks.module064SuperAdministratorAuthority = "passed";

  const providerConfiguration = await request("/api/ai-configuration", {
    moduleNumber: "064",
    authenticated: true,
  });
  assert(providerConfiguration.status === 200, "Module 064 provider configuration returned HTTP " + providerConfiguration.status + ".");
  assert(providerConfiguration.json?.module === "064", "Module 064 provider configuration has the wrong module.");
  assert(providerConfiguration.json?.governance?.sanitizedExternalExecutionEnabled === true, "Module 064 sanitized external execution is not enabled.");
  assert(providerConfiguration.json?.governance?.enterpriseSanitizedExternalFallbackEnabled === true, "Module 064 enterprise sanitized external fallback is not enabled.");
  const providerHealth = await request("/api/ai-configuration/health/refresh", {
    method: "POST",
    moduleNumber: "064",
    authenticated: true,
  });
  assert(providerHealth.status === 200, "Module 064 provider health returned HTTP " + providerHealth.status + ".");
  const remoteProviderHealth = (providerHealth.json?.providers || [])
    .filter((item) => ["claude", "openai"].includes(String(item?.provider || "").toLowerCase()))
    .map((item) => ({
      provider: String(item.provider).toLowerCase(),
      enabled: item.enabled === true,
      configured: item.configured === true,
      probeStatus: String(item.probeStatus || "not_checked"),
    }));
  assert(remoteProviderHealth.length === 2, "Module 064 did not return sanitized Claude and OpenAI health evidence.");
  assert(remoteProviderHealth.every((item) => item.enabled && item.configured && item.probeStatus === "available"), "Claude and OpenAI must both be configured, enabled, and available for governed failover.");
  evidence.authenticatedChecks.externalProviderReadiness = {
    status: String(providerHealth.json?.status || "unknown"),
    providers: remoteProviderHealth,
  };

  const externalProductionProbe = await request("/api/ai-configuration/sanitized-external-fallback/production-test", {
    method: "POST",
    moduleNumber: "064",
    authenticated: true,
    timeoutMs: 120000,
  });
  assert(externalProductionProbe.status === 200, "Module 064 sanitized external production probe returned HTTP " + externalProductionProbe.status + ".");
  assert(externalProductionProbe.json?.ready === true, "Module 064 sanitized external production probe is not ready.");
  assert(externalProductionProbe.json?.policy?.sanitizedExternalExecutionEnabled === true, "Sanitized external execution policy is not active.");
  assert(externalProductionProbe.json?.policy?.enterpriseSanitizedExternalFallbackEnabled === true, "Enterprise sanitized fallback policy is not active.");
  assert(Array.isArray(externalProductionProbe.json?.targets) && externalProductionProbe.json.targets.length === 2, "The sanitized external production probe did not exercise Claude and OpenAI.");
  evidence.authenticatedChecks.externalProviderProductionProbe = "passed";

  const knowledgeFabric = await request("/api/ai-configuration/knowledge-fabric", {
    moduleNumber: "064",
    authenticated: true,
    timeoutMs: 120000,
  });
  assert(knowledgeFabric.status === 200, "Module 064 knowledge fabric returned HTTP " + knowledgeFabric.status + ".");
  assert(knowledgeFabric.json?.module === "064", "Knowledge-fabric response has the wrong module.");
  assert(knowledgeFabric.json?.status === "celar_ai_knowledge_fabric_ready", "Module 064 knowledge fabric is not ready.");
  const fabric = knowledgeFabric.json?.knowledgeFabric || {};
  assert(fabric.ready === true, "The comprehensive knowledge fabric is not ready.");
  assert(fabric.routeGraphReady === true, "The Module 064 capability route graph is not ready.");
  assert(fabric.contentGraphReady === true, "The current private document/content graph is not ready.");
  assert(fabric.contextGraphReady === true, "The Celar temporal and policy context graph is not ready.");
  assert(fabric.temporalGraphReady === true, "The Celar temporal graph is not ready.");
  assert(fabric.policyGraphReady === true, "The Celar policy graph is not ready.");
  assert(fabric.decisionTraceReady === true, "The Celar privacy-safe decision trace is not ready.");
  assert(fabric.privateEndpointsReady === true, "One or more required private endpoints are not ready.");
  assert(fabric.sourceCommit === sourceSha, "Knowledge-fabric source binding is incorrect.");
  assert(fabric.productKnowledgeVersion === "celar-ai-product-knowledge-v3-20260806", "The latest product knowledge catalog is not active.");
  assert(fabric.systemKnowledgeVersion === "celar-ai-system-knowledge-v3-20260806", "The latest system knowledge catalog is not active.");
  assert(Number(fabric.readyDocumentCount) > 0, "The private content graph has no ready documents.");
  assert(Number(fabric.readySowDocumentCount) > 0, "The private content graph has no current ready SOW/GSD document.");
  assert(Number(fabric.activeVersionCount) > 0, "The private content graph has no authoritative active versions.");
  assert(Number(fabric.activeChunkCount) > 0, "The private content graph has no searchable active chunks.");
  const privateEmbeddingAdapter = (fabric.endpoints || []).find((item) => item?.component === "private_embedding");
  const approvedLexicalOnlyIndex = privateEmbeddingAdapter?.required === false
    && privateEmbeddingAdapter?.runtimeVerified === true
    && privateEmbeddingAdapter?.diagnosticCode === "approved_lexical_only";
  assert(Number(fabric.pendingIndexCount) === 0, "The latest private knowledge content still has pending indexing work.");
  assert(Number(fabric.unembeddedChunkCount) === 0 || approvedLexicalOnlyIndex, "Unembedded chunks are present without an approved lexical-only retrieval contract.");
  assert(fabric.freshnessStatus === "authoritative_index_current", "The private knowledge fabric is not current.");
  assert(String(fabric.knowledgeAsOf || "").length > 0, "The private knowledge fabric has no evidence-as-of time.");
  assert(Array.isArray(fabric.blockers) && fabric.blockers.length === 0, "The knowledge fabric reported readiness blockers.");
  assert(Array.isArray(fabric.contentGraphRelationships) && fabric.contentGraphRelationships.some((item) => String(item).includes("authoritative version")), "The authoritative private content-graph relationship is missing.");
  assert(Array.isArray(fabric.contextGraphRelationships) && fabric.contextGraphRelationships.some((item) => String(item).includes("question time")), "The temporal policy context relationship is missing.");
  assert(fabric.contextGraphRelationships.some((item) => String(item).includes("evidence as-of")), "The evidence freshness and confidence relationship is missing.");
  const endpointComponents = new Set((fabric.endpoints || []).map((item) => item?.component));
  for (const component of ["private_inference", "private_database", "private_malware_scanning", "private_ocr", "private_embedding", "private_training", "persistent_private_content_storage"]) {
    assert(endpointComponents.has(component), "Module 064 is missing private-adapter readiness evidence for " + component + ".");
  }
  const requiredPrivateEndpoints = (fabric.endpoints || []).filter((item) => item?.required === true);
  assert(requiredPrivateEndpoints.length >= 4, "Module 064 returned incomplete required private-endpoint evidence.");
  assert(requiredPrivateEndpoints.every((item) => item?.configured === true && item?.privateBoundaryVerified === true && item?.runtimeVerified === true), "A required Celar private endpoint is not configured, private, and runtime-verified.");
  const forgeFabricCapability = (fabric.capabilities || []).find((item) => item?.feature === "project_forge_plan_estimate");
  assert(forgeFabricCapability?.module === "011/033", "The knowledge fabric does not map Project Forge to Modules 011/033.");
  assert(forgeFabricCapability?.centralRouterConnected === true, "Project Forge is not connected to the Module 064 central router.");
  assert(forgeFabricCapability?.privateContextCompliant === true && forgeFabricCapability?.directProviderFree === true, "Project Forge does not satisfy the private, router-only provider boundary.");
  assert(forgeFabricCapability?.privateKnowledgeReady === true, "Project Forge cannot use the ready private knowledge fabric.");
  assert(JSON.stringify(forgeFabricCapability?.route) === JSON.stringify(configuredRoutes.get("project_forge_plan_estimate")), "Project Forge does not reflect its selected Module 064 target order.");
  assert(Array.isArray(fabric.decisionTraces) && fabric.decisionTraces.length === expectedFeatures.length, "Module 064 returned incomplete capability decision traces.");
  for (const trace of fabric.decisionTraces) {
    assert(JSON.stringify(trace?.configuredRoute) === JSON.stringify(configuredRoutes.get(trace?.feature)), "A live decision trace does not reflect the selected Module 064 order for " + trace?.feature + ".");
    assert(trace?.hiddenReasoningReturned === false, "A decision trace reported hidden reasoning exposure.");
  }
  assert(knowledgeFabric.json?.privacyBoundary?.endpointValuesReturned === false, "Knowledge-fabric response exposed private endpoint values.");
  assert(knowledgeFabric.json?.privacyBoundary?.secretValuesReturned === false, "Knowledge-fabric response exposed secret values.");
  assert(knowledgeFabric.json?.privacyBoundary?.rawDocumentsReturned === false, "Knowledge-fabric response exposed raw documents.");
  assert(knowledgeFabric.json?.privacyBoundary?.embeddingVectorsReturned === false, "Knowledge-fabric response exposed embedding vectors.");
  evidence.authenticatedChecks.module064KnowledgeFabric = "passed";
  evidence.authenticatedChecks.module064ContextFabric = "passed";
  evidence.authenticatedChecks.module064PrivateAdapterMatrix = "passed";

  const generalKnowledgeChat = await request("/api/celar-ai/v2/chat", {
    method: "POST",
    moduleNumber: "011",
    authenticated: true,
    timeoutMs: 120000,
    body: {
      question: "What is the capital of France?",
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
  assert(generalKnowledgeChat.status === 200, "Celar general-knowledge question returned HTTP " + generalKnowledgeChat.status + ".");
  assert(generalKnowledgeChat.json?.result?.intentCode === "general_knowledge", "Celar did not classify the outside question as general knowledge.");
  assert(/\bParis\b/i.test(String(generalKnowledgeChat.json?.result?.answer?.directConclusion || "")), "Celar general-knowledge answer did not return Paris.");
  assert(["celar_ai", "claude", "openai"].includes(String(generalKnowledgeChat.json?.result?.modelProvider || "").toLowerCase()), "Celar general knowledge fell through to the governed local template.");
  assert(Array.isArray(generalKnowledgeChat.json?.result?.relevantApis) && generalKnowledgeChat.json.result.relevantApis.length === 0, "Celar general-knowledge answer exposed API inventory.");
  assert(Array.isArray(generalKnowledgeChat.json?.result?.toolResults) && generalKnowledgeChat.json.result.toolResults.length === 0, "Celar general-knowledge answer ran Pulse tools.");
  assert(!answerContainsApiDetail(generalKnowledgeChat.json?.result?.answer), "Celar general-knowledge answer included unrequested API, route, endpoint, or HTTP detail.");
  assert(generalKnowledgeChat.json?.rawPrivateContextSentToExternalProvider === false, "Celar general-knowledge privacy flag was not strictly false.");
  evidence.authenticatedChecks.celarGeneralKnowledge = "passed";

  const categories = await request("/api/non-project-time-categories", {
    moduleNumber: "001",
    authenticated: true,
  });
  assert(categories.status === 200, "Timesheet non-project categories returned HTTP " + categories.status + ".");
  const category = (categories.json?.categories || []).find((item) => uuidPattern.test(String(item?.id || "")));
  assert(category, "No active non-project Timesheet category is available for AI suggestion UAT.");
  const timesheetSuggestion = await request("/api/timesheets/ai-description-suggestions", {
    method: "POST",
    moduleNumber: "001",
    authenticated: true,
    timeoutMs: 120000,
    body: {
      workDate: new Date().toISOString().slice(0, 10),
      timeEntryId: null,
      assignmentId: null,
      projectId: null,
      taskId: null,
      nonProjectTimeCategoryId: category.id,
      timeType: "regular",
      rowType: "nonProject",
      rowLabel: category.name,
      customerName: null,
      projectName: null,
      projectCode: null,
      taskName: null,
      taskCode: null,
      categoryCode: category.code,
      hours: 0.5,
      currentDescription: "Reviewed and tested the deployment configuration, validated expected system behavior, and documented the results.",
    },
  });
  assert(timesheetSuggestion.status === 200, "Timesheet AI suggestion returned HTTP " + timesheetSuggestion.status + ".");
  assert(timesheetSuggestion.json?.status === "ai_suggestion_generated", "Timesheet AI suggestion was not generated.");
  assert(["celar_ai", "claude", "openai"].includes(String(timesheetSuggestion.json?.provider || "").toLowerCase()), "Timesheet AI suggestion fell through to the governed local template.");
  assert(String(timesheetSuggestion.json?.suggestion || "").trim().length >= 160, "Timesheet AI suggestion was not comprehensive enough for review.");
  assert(sentenceCount(timesheetSuggestion.json?.suggestion) >= 2, "Timesheet AI suggestion did not contain a comprehensive multi-sentence response.");
  evidence.authenticatedChecks.timesheetComprehensiveAiSuggestion = "passed";

  const closeoutDraft = await request("/api/project-closeout/ai/communication", {
    method: "POST",
    moduleNumber: "040",
    authenticated: true,
    timeoutMs: 120000,
    body: {
      projectCode: null,
      projectName: null,
      audience: "internal",
      completionSummary: "The planned validation was completed and the recorded checks passed.",
      acceptanceEvidence: "The review evidence is available for PM and Engineering confirmation.",
      outstandingItems: "Final human approval and recipient confirmation remain open.",
      handoffSummary: "The operational notes and validation evidence are ready for review.",
      requestedTone: "professional and factual",
      allowSanitizedExternalFallback: false,
    },
  });
  assert(closeoutDraft.status === 200, "Closeout AI communication returned HTTP " + closeoutDraft.status + ".");
  assert(closeoutDraft.json?.status !== "closeout_draft_refused", "Closeout AI communication was refused.");
  assert(["celar_ai", "claude", "openai"].includes(String(closeoutDraft.json?.selectedTarget || "").toLowerCase()), "Closeout AI communication fell through to the governed local template.");
  assert(String(closeoutDraft.json?.draft || "").trim().length >= 300, "Closeout AI communication was not comprehensive enough for review.");
  assert(closeoutDraft.json?.reviewRequired === true && closeoutDraft.json?.emailSent === false && closeoutDraft.json?.stateChanged === false, "Closeout AI communication violated the review-only boundary.");
  evidence.authenticatedChecks.closeoutComprehensiveAiDraft = "passed";

  const profilePhoto = await request("/api/profile/preferences/production-validation", {
    authenticated: true,
  });
  assert(profilePhoto.status === 200, "Profile-picture persistence validation returned HTTP " + profilePhoto.status + ".");
  assert(profilePhoto.json?.storage?.mode === "database", "Profile picture is not stored in the database.");
  assert(profilePhoto.json?.storage?.redeploySafe === true, "Profile-picture storage is not marked redeploy-safe.");
  assert(profilePhoto.json?.currentUser?.profilePhotoPayloadReturned === false, "Profile-picture validation returned image payload data.");
  evidence.authenticatedChecks.profilePicturePersistence = {
    status: String(profilePhoto.json?.status || "unknown"),
    hasPersistedProfilePhoto: profilePhoto.json?.currentUser?.hasPersistedProfilePhoto === true,
    redeploySafe: true,
  };

  const attachments = await request("/api/celar-ai/v2/attachments/readiness", {
    moduleNumber: "011",
    authenticated: true,
  });
  assert(attachments.status === 200, "Attachment readiness returned HTTP " + attachments.status + ".");
  const readiness = attachments.json?.attachmentReadiness || {};
  assert(readiness.status === "celar_ai_chat_attachments_ready", "Attachment runtime is not ready.");
  assert(readiness.schemaReady === true, "Attachment schema is not ready.");
  assert(readiness.privateRuntimeSchemaReady === true, "Private document schema is not ready.");
  assert(readiness.privateWorkerEnabled === true, "Private document worker is not enabled.");
  assert(readiness.persistentStorageReady === true, "Persistent attachment storage is not ready.");
  assert(readiness.malwareScanningConfigured === true, "Malware scanning is not configured.");
  assert(readiness.embeddingConfigured === true || readiness.lexicalOnlyCompletionApproved === true, "Neither embeddings nor approved lexical-only completion are available.");
  assert(Array.isArray(readiness.blockers) && readiness.blockers.length === 0, "Attachment readiness has blockers.");
  assert(readiness.rawDocumentsSentToClaudeOrOpenAi === false, "Attachment readiness permits raw documents to a public provider.");
  evidence.authenticatedChecks.attachmentReadiness = "passed";

  const createConversation = await request("/api/celar-ai/v1/system/conversations", {
    method: "POST",
    moduleNumber: "011",
    authenticated: true,
    body: {
      title: "Guarded Test attachment UAT " + sourceSha.slice(0, 12),
      mode: "help",
      scope: { purpose: "attachment_uat", releaseSha: sourceSha },
    },
  });
  assert(createConversation.status === 200, "Conversation creation returned HTTP " + createConversation.status + ".");
  assert(createConversation.json?.status === "pulse_ai_conversation_created", "Conversation was not created.");
  conversationId = String(createConversation.json?.conversation?.conversationId || "");
  assert(uuidPattern.test(conversationId), "Conversation response did not include a UUID.");
  const nonce = "PP-UAT-" + randomUUID();
  attachmentFileName = "guarded-test-" + sourceSha.slice(0, 12) + ".txt";
  const form = new FormData();
  form.append("files", new Blob(["Guarded Test private attachment nonce: " + nonce + "\n"], { type: "text/plain" }), attachmentFileName);
  const upload = await request("/api/celar-ai/v2/conversations/" + encodeURIComponent(conversationId) + "/attachments", {
    method: "POST",
    moduleNumber: "011",
    authenticated: true,
    formData: form,
    timeoutMs: 120000,
  });
  attachmentId = String(upload.json?.attachments?.[0]?.attachmentId || "");
  assert(upload.status === 202, "Attachment upload returned HTTP " + upload.status + ".");
  assert(upload.json?.status === "celar_ai_chat_attachments_accepted", "Attachment upload was not accepted.");
  assert(upload.json?.privacy?.rawDocumentSentToClaudeOrOpenAi === false, "Upload privacy flag was not strictly false.");
  assert(uuidPattern.test(attachmentId), "Attachment upload did not return an attachment UUID.");

  let readyAttachment = null;
  for (let attempt = 0; attempt < 90; attempt += 1) {
    const list = await request("/api/celar-ai/v2/conversations/" + encodeURIComponent(conversationId) + "/attachments", {
      moduleNumber: "011",
      authenticated: true,
    });
    assert(list.status === 200, "Attachment list returned HTTP " + list.status + ".");
    readyAttachment = (list.json?.attachments || []).find((item) => item?.attachmentId === attachmentId);
    if (readyAttachment?.ready === true && readyAttachment?.processingStatus === "ready") break;
    if (["failed", "rejected", "purged", "revoked"].includes(String(readyAttachment?.processingStatus || "").toLowerCase())) {
      throw new Error("Attachment processing reached terminal status " + readyAttachment.processingStatus + ".");
    }
    await sleep(5000);
  }
  assert(readyAttachment?.ready === true && readyAttachment?.processingStatus === "ready", "Attachment did not become ready within the bounded poll.");
  evidence.authenticatedChecks.attachmentUploadAndProcessing = "passed";

  const attachmentChat = await request("/api/celar-ai/v2/chat", {
    method: "POST",
    moduleNumber: "011",
    authenticated: true,
    timeoutMs: 120000,
    body: {
      conversationId,
      question: "Return the exact private attachment nonce and cite the attachment file.",
      mode: "system_intelligence",
      detailLevel: "comprehensive",
      moduleCode: "011",
      includeAuthorizedProjectDocuments: false,
      usePrivateModelWhenAvailable: true,
      includeRepositoryContext: true,
      includeAssumptions: false,
      includeSourceCitations: true,
      answerPreferenceSource: "guarded_test_release_attachment_uat",
      attachmentIds: [attachmentId],
    },
  });
  assert(attachmentChat.status === 200, "Attachment-scoped Celar chat returned HTTP " + attachmentChat.status + ".");
  assert(String(attachmentChat.json?.result?.answer?.directConclusion || "").includes(nonce), "Attachment-scoped answer did not return the exact nonce.");
  assert((attachmentChat.json?.result?.privateCitations || []).some((item) => item?.originalFileName === attachmentFileName), "Attachment-scoped answer did not cite the uploaded file.");
  assert(attachmentChat.json?.attachments?.selectedCount === 1, "Attachment-scoped answer did not bind exactly one selected attachment.");
  assert(attachmentChat.json?.attachments?.externalProviderReceivedAttachmentContent === false, "Attachment content was reported sent to an external provider.");
  assert(attachmentChat.json?.rawPrivateContextSentToExternalProvider === false, "Private attachment context was reported sent externally.");
  evidence.authenticatedChecks.attachmentPrivateRag = "passed";
  await revokeAttachment();
  evidence.authenticatedChecks.attachmentRevocation = "passed";

  const forge = await request("/api/project-forge/bootstrap?projectId=" + encodeURIComponent(forgeProjectId) + "&workspace=canonical", {
    moduleNumber: "033",
    authenticated: true,
  });
  assert(forge.status === 200, "Project Forge bootstrap returned HTTP " + forge.status + ".");
  assert(forge.json?.status === "project_forge_loaded", "Project Forge did not report loaded.");
  assert(forge.json?.access?.canManage === true, "Project Forge fixture is not manageable by the Test session.");
  const forgeProject = (forge.json?.projects || []).find((item) => item?.projectId === forgeProjectId);
  assert(forgeProject, "Project Forge fixture project was not returned.");
  assert(forge.json?.summary?.selectedProjectId === forgeProjectId, "Project Forge selected project binding is incorrect.");
  const forgeConnection = forge.json?.ai?.module064Connection || {};
  assert(forge.json?.ai?.enabled === true, "Project Forge does not expose AI as enabled for the authorized Test session.");
  assert(forge.json?.ai?.capability === "project_forge_plan_estimate", "Project Forge exposes the wrong Celar AI capability.");
  assert(forgeConnection.connected === true, "Project Forge is not visibly connected to Module 064.");
  assert(forgeConnection.status === "connected_private_knowledge_ready", "Project Forge does not report a ready private Module 064 connection.");
  assert(forgeConnection.permissionAuthorized === true, "Project Forge AI permission is not authorized.");
  assert(forgeConnection.privateKnowledgeReady === true, "Project Forge private knowledge is not ready.");
  assert(JSON.stringify(forgeConnection.route) === JSON.stringify(configuredRoutes.get("project_forge_plan_estimate")), "Project Forge does not expose its selected Module 064 route order.");
  assert(forgeConnection.sourceCommit === sourceSha, "Project Forge Module 064 evidence is not bound to this release.");
  assert(forgeConnection.productKnowledgeVersion === "celar-ai-product-knowledge-v3-20260806", "Project Forge is not using the latest product knowledge catalog.");
  assert(forgeConnection.systemKnowledgeVersion === "celar-ai-system-knowledge-v3-20260806", "Project Forge is not using the latest system knowledge catalog.");
  assert(Number(forgeConnection.readyDocumentCount) > 0 && Number(forgeConnection.readySowDocumentCount) > 0, "Project Forge has no ready private project/SOW knowledge.");
  assert(Number(forgeConnection.activeVersionCount) > 0 && Number(forgeConnection.activeChunkCount) > 0, "Project Forge has no active authoritative content graph.");
  assert(Array.isArray(forgeConnection.blockers) && forgeConnection.blockers.length === 0, "Project Forge reports Module 064 connection blockers.");
  assert(forgeConnection.endpointValuesReturned === false && forgeConnection.secretValuesReturned === false, "Project Forge exposed private endpoint or secret values.");
  evidence.authenticatedChecks.projectForgeModule064Connection = "passed";

  const projectCode = String(forgeProject.projectCode || "").trim();
  const projectName = String(forgeProject.projectName || "").trim();
  assert(projectCode.length > 0 && projectName.length > 0, "Project Forge fixture is missing project identity required for grounded planning UAT.");
  const composePlanning = async (mode, capabilityCode, requestedOutcome) => request("/api/celar-ai/v1/compose", {
    method: "POST",
    moduleNumber: "011",
    authenticated: true,
    timeoutMs: 180000,
    body: {
      mode,
      projectId: forgeProjectId,
      projectCode,
      projectName,
      startDate: new Date().toISOString().slice(0, 10),
      requestedOutcome,
      detailLevel: "comprehensive",
      diagramType: "gantt",
      allowSanitizedExternalFallback: false,
      capabilityCode,
    },
  });

  const sowComposition = await composePlanning(
    "sow_draft",
    "sow_gsd_planning",
    "Automatically populate every customer-facing SOW section and every work package with ordered actor/action/input/output/evidence/completion steps, responsibilities, prerequisites, estimates, acceptance criteria, validation, risks, open questions, dependencies, and citations.",
  );
  assert(sowComposition.status === 200, "Comprehensive SOW composition returned HTTP " + sowComposition.status + ".");
  assert(sowComposition.json?.result?.status === "celar_ai_solution_draft_completed", "Comprehensive SOW composition did not complete.");
  assert(["celar_ai", "claude", "openai"].includes(String(sowComposition.json?.result?.selectedTarget || "").toLowerCase()), "SOW composition fell through to a local template.");
  const sowDraft = sowComposition.json?.result?.sowDraft || {};
  assert(String(sowDraft.title || "").trim().length > 0 && String(sowDraft.executiveSummary || "").trim().length >= 80, "SOW title or executive summary is incomplete.");
  for (const [field, label] of [
    ["objectives", "objectives"], ["inScope", "in-scope services"], ["outOfScope", "out-of-scope services"],
    ["deliverables", "deliverables"], ["customerResponsibilities", "customer responsibilities"],
    ["usSignalResponsibilities", "US Signal responsibilities"], ["assumptions", "assumptions"],
    ["dependencies", "dependencies"], ["acceptanceCriteria", "acceptance criteria"],
    ["timelineAndMilestones", "timeline and milestones"], ["risks", "risks"], ["openQuestions", "open questions"],
  ]) assertPopulatedList(sowDraft[field], "SOW " + label);
  assert(Array.isArray(sowDraft.workPackages) && sowDraft.workPackages.length > 0, "SOW returned no populated work packages.");
  sowDraft.workPackages.forEach((item, index) => assertDetailedPlanningTask(item, "SOW work package " + (index + 1), false));
  assert(sowDraft.reviewRequired === true && sowDraft.contractuallyBinding === false, "SOW violated the required non-binding human-review boundary.");
  assert(sowComposition.json?.result?.controls?.sowPublished === false && sowComposition.json?.result?.controls?.stateChanged === false, "SOW composition changed or published state.");
  evidence.authenticatedChecks.sowComprehensiveStructuredDraft = "passed";

  const flowHiveComposition = await composePlanning(
    "project_plan",
    "project_flowhive_plan",
    "Create an extremely detailed customer-facing FlowHive plan and automatically populate every structured task and planning section with executable steps, dependencies, roles, estimates, responsibilities, evidence, and completion criteria.",
  );
  assert(flowHiveComposition.status === 200, "Comprehensive FlowHive composition returned HTTP " + flowHiveComposition.status + ".");
  assert(flowHiveComposition.json?.result?.status === "celar_ai_solution_draft_completed", "Comprehensive FlowHive composition did not complete.");
  assertDetailedPlan(flowHiveComposition.json?.result?.flowHivePlan, "FlowHive plan");
  assert(flowHiveComposition.json?.result?.controls?.projectPlanBaselined === false && flowHiveComposition.json?.result?.controls?.stateChanged === false, "FlowHive composition changed or baselined project state.");
  evidence.authenticatedChecks.flowHiveComprehensiveStructuredPlan = "passed";

  const forgeComposition = await composePlanning(
    "project_plan",
    "project_forge_plan_estimate",
    "Create an extremely detailed customer-facing Project Forge task and estimate draft; automatically populate each supported section so a delivery professional can execute and validate the work without guessing.",
  );
  assert(forgeComposition.status === 200, "Comprehensive Project Forge composition returned HTTP " + forgeComposition.status + ".");
  assert(forgeComposition.json?.result?.status === "celar_ai_solution_draft_completed", "Comprehensive Project Forge composition did not complete.");
  assertDetailedPlan(forgeComposition.json?.result?.flowHivePlan, "Project Forge plan");
  assert(forgeComposition.json?.result?.controls?.projectPlanBaselined === false && forgeComposition.json?.result?.controls?.engineersAssigned === false && forgeComposition.json?.result?.controls?.stateChanged === false, "Project Forge composition changed project state.");
  evidence.authenticatedChecks.projectForgeComprehensiveStructuredPlan = "passed";

  forgeTaskName = "Guarded Test persistence probe " + randomUUID().slice(0, 8);
  const createTask = await request("/api/project-forge/projects/" + encodeURIComponent(forgeProjectId) + "/tasks", {
    method: "POST",
    moduleNumber: "033",
    authenticated: true,
    body: {
      clientMutationId: randomUUID(),
      taskCode: null,
      taskName: forgeTaskName,
      description: "Automated Test validation; archive after reload.",
      taskType: "variable",
      phase: "UAT",
      priority: "normal",
      status: null,
      kanbanCategory: "backlog",
      startDate: null,
      dueDate: null,
      durationWorkingDays: 0,
      estimatedHours: 0,
      percentComplete: 0,
      blockedReason: null,
      billable: false,
      assigneeUserId: null,
      parentTaskId: null,
      hourlyRate: 0,
      materialUnits: 0,
      materialUnitCost: 0,
      fixedCost: 0,
      travelCost: 0,
      equipmentCost: 0,
      miscCost: 0,
      recurrenceRule: null,
      decisionAction: "none",
      important: false,
      urgent: false,
    },
  });
  forgeTaskId = String(createTask.json?.task?.taskId || "");
  forgeTaskRevision = Number(createTask.json?.revision || createTask.json?.task?.revision || 0);
  assert(createTask.status === 201, "Project Forge task creation returned HTTP " + createTask.status + ".");
  assert(createTask.json?.status === "canonical_task_created", "Project Forge did not create the canonical task.");
  assert(uuidPattern.test(forgeTaskId), "Project Forge create response did not include a task UUID.");
  assert(Number.isInteger(forgeTaskRevision) && forgeTaskRevision > 0, "Project Forge create response did not include a valid revision.");

  const forgeReload = await request("/api/project-forge/bootstrap?projectId=" + encodeURIComponent(forgeProjectId) + "&workspace=canonical", {
    moduleNumber: "033",
    authenticated: true,
  });
  assert(forgeReload.status === 200, "Project Forge reload returned HTTP " + forgeReload.status + ".");
  assert((forgeReload.json?.tasks || []).some((item) => item?.taskId === forgeTaskId && item?.taskName === forgeTaskName), "Project Forge task did not persist across reload.");
  evidence.authenticatedChecks.projectForgeCreateReload = "passed";
  await archiveForgeTask();

  const forgeAfterArchive = await request("/api/project-forge/bootstrap?projectId=" + encodeURIComponent(forgeProjectId) + "&workspace=canonical", {
    moduleNumber: "033",
    authenticated: true,
  });
  assert(forgeAfterArchive.status === 200, "Project Forge post-archive reload returned HTTP " + forgeAfterArchive.status + ".");
  assert(!(forgeAfterArchive.json?.tasks || []).some((item) => item?.taskId === forgeTaskId), "Archived Project Forge task remains active.");
  evidence.authenticatedChecks.projectForgeArchive = "passed";

  evidence.status = "passed";
  evidence.authenticatedUatStatus = "executed";
}

try {
  await run();
  console.log("CURRENT_MAIN_TEST_RUNTIME_VERIFICATION=PASSED");
  console.log(authenticatedUatEnabled
    ? "CURRENT_MAIN_AUTHENTICATED_UAT=EXECUTED"
    : "CURRENT_MAIN_AUTHENTICATED_UAT=PENDING_USER_SESSION_VALIDATION");
} catch (error) {
  evidence.status = "failed";
  evidence.failure = safeError(error);
  throw error;
} finally {
  try {
    await revokeAttachment();
  } catch (error) {
    evidence.cleanup.attachment = "failed:" + safeError(error);
    if (evidence.status === "passed") {
      evidence.status = "failed";
      evidence.failure = "Attachment cleanup failed.";
    }
  }
  try {
    await archiveForgeTask();
  } catch (error) {
    evidence.cleanup.projectForgeTask = "failed:" + safeError(error);
    if (evidence.status === "passed") {
      evidence.status = "failed";
      evidence.failure = "Project Forge cleanup failed.";
    }
  }
  writeEvidence();
  if (evidence.status !== "passed") process.exitCode = 1;
}
