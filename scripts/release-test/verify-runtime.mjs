#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import { createHash, randomUUID } from "node:crypto";

const expectedSource = "acae6fda08e6d58dfb1e63b5eef4828877fd5523";
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
    const text = (await response.text()).slice(0, 1_000_000);
    let json = null;
    try {
      json = text ? JSON.parse(text) : null;
    } catch {}
    return { status: response.status, json, text };
  } finally {
    clearTimeout(timeout);
  }
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
  flowHiveIncluded: false,
  migration074Included: false,
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

  const shell = await request("/", { accept: "text/html" });
  assert(shell.status === 200, "Web application shell returned HTTP " + shell.status + ".");
  const scriptPath = shell.text.match(/<script[^>]+src=["']([^"']+\.js)["']/i)?.[1] || "";
  assert(scriptPath.length > 0, "Web application shell did not reference its production bundle.");
  const bundle = await request(scriptPath, { accept: "text/javascript" });
  assert(bundle.status === 200, "Web production bundle returned HTTP " + bundle.status + ".");
  assert(bundle.text.includes("brand-logo-image"), "Web production bundle does not mount the main-page US Signal logo.");
  const logoPath = bundle.text.match(/\/assets\/(?:USSNavyStacked|ussignal)-[A-Za-z0-9_-]+\.png/)?.[0] || "";
  assert(logoPath.length > 0, "Web production bundle does not reference the approved stacked US Signal logo asset.");
  const logoBytes = await requestBytes(logoPath);
  assert(createHash("sha256").update(logoBytes).digest("hex") === officialLogoSha256, "Live US Signal logo bytes do not match the approved governed asset.");
  evidence.publicChecks.officialUsSignalLogo = "passed";

  const health = await request("/api/health");
  assert(health.status === 200, "API health returned HTTP " + health.status + ".");
  evidence.publicChecks.apiHealth = "passed";

  const version = await request("/api/version");
  assert(version.status === 200, "Version route returned HTTP " + version.status + ".");
  evidence.publicChecks.version = "passed";

  const protectedRoutes = [
    ["GET", "/api/pulse-ai/v1/system/apis?search=project-forge&module=033&limit=10"],
    ["POST", "/api/celar-ai/v2/chat"],
    ["GET", "/api/ai-configuration/routes"],
    ["GET", "/api/celar-ai/v2/attachments/readiness"],
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
  for (const feature of expectedFeatures) {
    const route = routes.json.routes.find((item) => item?.feature === feature);
    assert(route, "Module 064 route is missing: " + feature + ".");
    assert(route.persisted === true, "Module 064 route is not persisted: " + feature + ".");
    assert(Number.isInteger(route.revision) && route.revision > 0, "Module 064 route has no persisted revision: " + feature + ".");
    assert(route.deploymentManaged === false && route.readOnly === false, "Module 064 database-managed route is unexpectedly deployment locked: " + feature + ".");
    assert(route.configurationAuthority === "database_managed_active", "Module 064 route has the wrong configuration authority: " + feature + ".");
    assert(JSON.stringify(route.targets) === JSON.stringify(expectedTargets), "Module 064 target order is wrong for " + feature + ".");
  }
  evidence.authenticatedChecks.module064Routes = "passed";

  const providerConfiguration = await request("/api/ai-configuration", {
    moduleNumber: "064",
    authenticated: true,
  });
  assert(providerConfiguration.status === 200, "Module 064 provider configuration returned HTTP " + providerConfiguration.status + ".");
  assert(providerConfiguration.json?.module === "064", "Module 064 provider configuration has the wrong module.");
  const providerHealth = await request("/api/ai-configuration/health", {
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
  evidence.authenticatedChecks.externalProviderReadiness = {
    status: String(providerHealth.json?.status || "unknown"),
    providers: remoteProviderHealth,
  };

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

  const createConversation = await request("/api/pulse-ai/v1/system/conversations", {
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
  assert((forge.json?.projects || []).some((item) => item?.projectId === forgeProjectId), "Project Forge fixture project was not returned.");
  assert(forge.json?.summary?.selectedProjectId === forgeProjectId, "Project Forge selected project binding is incorrect.");

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
