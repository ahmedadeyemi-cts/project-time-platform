#!/usr/bin/env node
import fs from "node:fs";

const expectedSource = "1892c6d0187edc367a57b8cee2e868417dd9a01a";
const expectedBase = "https://phd-west-test.onenecklab.com";
const sourceSha = String(process.env.SOURCE_SHA || "").trim();
const base = String(process.env.PUBLIC_URL || "").trim().replace(/\/+$/, "");
const evidencePath = process.env.EVIDENCE_PATH || "/tmp/modules-081-082-001a-celar-ai-runtime-evidence.json";

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function sleep(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

assert(sourceSha === expectedSource, "Unexpected deployed source SHA.");
assert(base === expectedBase, "PUBLIC_URL is not the exact protected Test origin.");

async function request(route, { accept = "application/json", attempts = 18 } = {}) {
  let last;
  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    const separator = route.includes("?") ? "&" : "?";
    const response = await fetch(`${base}${route}${separator}release_check=${sourceSha.slice(0, 12)}-${attempt}`, {
      redirect: "manual",
      headers: {
        Accept: accept,
        "Cache-Control": "no-cache, no-store, max-age=0",
        Pragma: "no-cache",
        Origin: base,
        Referer: `${base}/`,
      },
    });
    const contentType = response.headers.get("content-type") || "";
    const text = await response.text();
    last = { status: response.status, contentType, text };
    if (![502, 503, 504].includes(response.status)) return last;
    await sleep(5_000);
  }
  return last;
}

function parseJson(result, label) {
  assert(result?.contentType.startsWith("application/json"), `${label} did not return JSON.`);
  try {
    return JSON.parse(result.text);
  } catch {
    throw new Error(`${label} returned invalid JSON.`);
  }
}

function assetUrl(value) {
  return new URL(value, `${base}/`).toString();
}

const release = await request("/release.json");
assert(release.status === 200, `Release stamp returned HTTP ${release.status}.`);
const releaseJson = parseJson(release, "Release stamp");
assert(releaseJson.sourceSha === sourceSha, "Release stamp source SHA is stale.");
assert(releaseJson.environment === "test", "Release stamp environment is not Test.");
assert(JSON.stringify(releaseJson.modules) === JSON.stringify(["081", "082", "001A"]), "Release stamp module list is incorrect.");
assert(JSON.stringify(releaseJson.migrations) === JSON.stringify(["076", "077", "078"]), "Release stamp migration list is incorrect.");
assert(releaseJson.celarAiPrivateRagUxIncluded === true, "Release stamp does not include the Celar AI private RAG UX release.");

const health = await request("/health");
assert(health.status === 200, `API health returned HTTP ${health.status}.`);
parseJson(health, "API health");

const labCapabilities = await request("/api/lab-equipment-tracker/capabilities");
assert(labCapabilities.status === 200, `Module 081 capabilities returned HTTP ${labCapabilities.status}.`);
const lab = parseJson(labCapabilities, "Module 081 capabilities");
assert(lab.module === "081" && lab.contractVersion === "081-enterprise-v1", "Module 081 capability contract is incorrect.");

const riskCapabilities = await request("/api/project-risk-register/capabilities");
assert(riskCapabilities.status === 200, `Module 082 capabilities returned HTTP ${riskCapabilities.status}.`);
const risk = parseJson(riskCapabilities, "Module 082 capabilities");
assert(risk.module === "082" && risk.contractVersion === "082-enterprise-v1", "Module 082 capability contract is incorrect.");

const engineerOverview = await request("/api/engineer-task-closeout/overview");
assert([401, 403].includes(engineerOverview.status), `Module 001A protected route returned unexpected HTTP ${engineerOverview.status}.`);
parseJson(engineerOverview, "Module 001A protected route");

const celarReadiness = await request("/api/celar-ai/v1/private-runtime/readiness");
assert([200, 401, 403].includes(celarReadiness.status), `Celar AI private-runtime route returned unexpected HTTP ${celarReadiness.status}.`);
parseJson(celarReadiness, "Celar AI private-runtime route");

const shell = await request("/", { accept: "text/html", attempts: 18 });
assert(shell.status === 200 && shell.contentType.startsWith("text/html"), "Web shell is unavailable.");
const scriptPaths = [...shell.text.matchAll(/<script[^>]+src=["']([^"']+\.js(?:\?[^"']*)?)["']/gi)].map((match) => match[1]);
assert(scriptPaths.length > 0, "Web shell does not reference a JavaScript bundle.");

let bundle = "";
for (const path of [...new Set(scriptPaths)]) {
  const response = await fetch(`${assetUrl(path)}${path.includes("?") ? "&" : "?"}release_check=${sourceSha.slice(0, 12)}`, {
    headers: { "Cache-Control": "no-cache, no-store", Pragma: "no-cache" },
  });
  assert(response.status === 200, `Served JavaScript asset returned HTTP ${response.status}.`);
  bundle += `\n${await response.text()}`;
}

for (const marker of [
  "Engineer Request Closeout",
  "Lab Equipment Tracker",
  "Enterprise Project Risk Register",
  "/api/engineer-task-closeout/overview",
  "/api/lab-equipment-tracker",
  "/api/project-risk-register",
  "Celar AI",
]) {
  assert(bundle.includes(marker), `Served application bundle is missing ${marker}.`);
}

const evidence = {
  environment: "test",
  sourceSha,
  releaseStamp: "verified",
  apiHealth: "healthy",
  migrations: ["076", "077", "078"],
  modules: {
    "081": lab.contractVersion,
    "082": risk.contractVersion,
    "001A": "protected-route-verified",
  },
  celarAiPrivateRuntimeRoute: `HTTP_${celarReadiness.status}`,
  servedBundle: "modules-and-celar-ai-markers-verified",
  authenticatedUatRequired: true,
  authenticatedUatStatus: "pending_user_session_validation",
  generatedAt: new Date().toISOString(),
};

fs.writeFileSync(evidencePath, `${JSON.stringify(evidence, null, 2)}\n`, { mode: 0o600 });
console.log("MODULES_081_082_001A_CELAR_AI_TEST_RUNTIME=PASSED");
