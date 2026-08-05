#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import { randomUUID } from "node:crypto";

const expectedSource = "e83340b5a4215ea63901cea98ea17596444f96b7";
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
const base = (process.env.PUBLIC_URL || "").replace(/\/+$/, "");
const session = process.env.PROJECTPULSE_TEST_UAT_SESSION || "";
const forgeProjectId = process.env.PROJECTPULSE_TEST_FORGE_PROJECT_ID || "";
const evidencePath = process.env.EVIDENCE_PATH || "/tmp/current-main-release-runtime-evidence.json";
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

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
assert(session.length >= 20, "PROJECTPULSE_TEST_UAT_SESSION is required for authenticated acceptance.");
assert(uuidPattern.test(forgeProjectId), "PROJECTPULSE_TEST_FORGE_PROJECT_ID must be a UUID.");

async function request(route, options = {}) {
  const method = options.method || "GET";
  const authenticated = options.authenticated === true;
  const headers = {
    Accept: "application/json",
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

const evidence = {
  environment: "test",
  sourceSha,
  publicOrigin: base,
  flowHiveIncluded: false,
  migration074Included: false,
  status: "running",
  publicChecks: {},
  authenticatedChecks: {},
  cleanup: {},
};

let conversationId = "";
let attachmentId = "";
let attachmentRevoked = false;
let forgeTaskId = "";
let forgeTaskRevision = 0;
let forgeTaskArchived = false;

function writeEvidence() {
  fs.mkdirSync(path.dirname(evidencePath), { recursive: true });
  fs.writeFileSync(evidencePath, JSON.stringify(evidence, null, 2) + "\n", { mode: 0o600 });
}

async function revokeAttachment() {
  if (!conversationId || !attachmentId || attachmentRevoked) return;
  const result = await request(
    "/api/celar-ai/v2/conversations/" + encodeURIComponent(conversationId) +
      "/attachments/" + encodeURIComponent(attachmentId),
    { method: "DELETE", authenticated: true, moduleNumber: "011" },
  );
  assert(result.status === 200, "Attachment cleanup returned HTTP " + result.status + ".");
  assert(result.json?.status === "celar_ai_chat_attachment_revoked", "Attachment cleanup did not confirm revocation.");
  assert(result.json?.retrievalEligible === false, "Revoked attachment remains retrieval eligible.");
  attachmentRevoked = true;
  evidence.cleanup.attachment = "revoked";
}

async function archiveForgeTask() {
  if (!forgeTaskId || forgeTaskArchived) return;
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
    ["GET", "/api/project-forge/bootstrap?projectId=" + encodeURIComponent(forgeProjectId) + "&workspace=canonical"],
  ];
  for (const [method, route] of protectedRoutes) {
    const result = await request(route, { method, body: method === "POST" ? {} : undefined });
    assert(result.status === 401, "Protected route did not fail closed with HTTP 401: " + method + " " + route + " returned " + result.status + ".");
    evidence.publicChecks[method + " " + route] = "session_required";
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
  assert(relevantApis.some((item) => String(item?.routePattern || "").startsWith("/api/project-forge")), "Celar search did not return a Project Forge route.");
  assert((apiSearch.json?.result?.answer?.citationIds || []).length > 0 || (apiSearch.json?.result?.sources || []).length > 0, "Celar API search returned no source evidence.");
  assert(apiSearch.json?.rawPrivateContextSentToExternalProvider === false, "Celar API-search privacy flag was not strictly false.");
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
  assert(routes.json?.controls?.releasePhase === "active", "Module 064 did not report active release phase.");
  assert(routes.json?.controls?.deploymentManaged === true, "Module 064 routing is not deployment managed.");
  assert(routes.json?.controls?.readOnly === true, "Module 064 routing is not release read-only.");
  assert(routes.json?.controls?.configurationSourceCommit === sourceSha, "Module 064 source binding is incorrect.");
  assert(routes.json?.controls?.catalogCapabilityCount === 8, "Module 064 catalog count is incorrect.");
  for (const feature of expectedFeatures) {
    const route = routes.json.routes.find((item) => item?.feature === feature);
    assert(route, "Module 064 route is missing: " + feature + ".");
    assert(JSON.stringify(route.targets) === JSON.stringify(expectedTargets), "Module 064 target order is wrong for " + feature + ".");
  }
  evidence.authenticatedChecks.module064Routes = "passed";

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
  const fileName = "guarded-test-" + sourceSha.slice(0, 12) + ".txt";
  const form = new FormData();
  form.append("files", new Blob(["Guarded Test private attachment nonce: " + nonce + "\n"], { type: "text/plain" }), fileName);
  const upload = await request("/api/celar-ai/v2/conversations/" + encodeURIComponent(conversationId) + "/attachments", {
    method: "POST",
    moduleNumber: "011",
    authenticated: true,
    formData: form,
    timeoutMs: 120000,
  });
  assert(upload.status === 202, "Attachment upload returned HTTP " + upload.status + ".");
  assert(upload.json?.status === "celar_ai_chat_attachments_accepted", "Attachment upload was not accepted.");
  assert(upload.json?.privacy?.rawDocumentSentToClaudeOrOpenAi === false, "Upload privacy flag was not strictly false.");
  attachmentId = String(upload.json?.attachments?.[0]?.attachmentId || "");
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
  assert((attachmentChat.json?.result?.privateCitations || []).some((item) => item?.originalFileName === fileName), "Attachment-scoped answer did not cite the uploaded file.");
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

  const forgeName = "Guarded Test persistence probe " + randomUUID().slice(0, 8);
  const createTask = await request("/api/project-forge/projects/" + encodeURIComponent(forgeProjectId) + "/tasks", {
    method: "POST",
    moduleNumber: "033",
    authenticated: true,
    body: {
      clientMutationId: randomUUID(),
      taskCode: null,
      taskName: forgeName,
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
  assert(createTask.status === 201, "Project Forge task creation returned HTTP " + createTask.status + ".");
  assert(createTask.json?.status === "canonical_task_created", "Project Forge did not create the canonical task.");
  forgeTaskId = String(createTask.json?.task?.taskId || "");
  forgeTaskRevision = Number(createTask.json?.revision || createTask.json?.task?.revision || 0);
  assert(uuidPattern.test(forgeTaskId), "Project Forge create response did not include a task UUID.");
  assert(Number.isInteger(forgeTaskRevision) && forgeTaskRevision > 0, "Project Forge create response did not include a valid revision.");

  const forgeReload = await request("/api/project-forge/bootstrap?projectId=" + encodeURIComponent(forgeProjectId) + "&workspace=canonical", {
    moduleNumber: "033",
    authenticated: true,
  });
  assert(forgeReload.status === 200, "Project Forge reload returned HTTP " + forgeReload.status + ".");
  assert((forgeReload.json?.tasks || []).some((item) => item?.taskId === forgeTaskId && item?.taskName === forgeName), "Project Forge task did not persist across reload.");
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
}

try {
  await run();
  console.log("CURRENT_MAIN_TEST_RUNTIME_VERIFICATION=PASSED");
  console.log("CURRENT_MAIN_AUTHENTICATED_UAT=EXECUTED");
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
