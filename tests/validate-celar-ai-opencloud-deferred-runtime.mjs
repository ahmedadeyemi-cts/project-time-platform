import fs from "node:fs";

const workflowPath = ".github/workflows/projectpulse-deploy-celar-ai-private-runtime-test.yml";
const architecturePath = "deployment/environments/opencloud-template.yml";
const workflow = fs.readFileSync(workflowPath, "utf8");
const architecture = fs.readFileSync(architecturePath, "utf8");

function requireMarker(text, marker, context) {
  if (!text.includes(marker)) {
    throw new Error(`${context} is missing required marker: ${marker}`);
  }
}

requireMarker(workflow, "workflow_dispatch:", workflowPath);
requireMarker(workflow, "DEPLOY-CELAR-AI-OPENCLOUD-RUNTIME-TO-TEST", workflowPath);

if (/^\s{2}push:\s*$/m.test(workflow)) {
  throw new Error(`${workflowPath} must not automatically deploy the deferred private runtime from a main push.`);
}

for (const prohibited of [
  "environment: test",
  "id-token: write",
  "azure/login@",
  "az containerapp",
  "az acr",
]) {
  if (workflow.includes(prohibited)) {
    throw new Error(`${workflowPath} is deferred and must not contain active Azure mutation capability: ${prohibited}`);
  }
}

requireMarker(workflow, "OPEN_CLOUD_PRIVATE_RUNTIME_DEPLOYMENT=DISABLED", workflowPath);
requireMarker(workflow, "AZURE_PRIVATE_RUNTIME_MUTATION=NONE", workflowPath);

requireMarker(architecture, "status: deferred-until-opencloud", architecturePath);
requireMarker(architecture, "topology: shared-private-runtime-vm", architecturePath);
requireMarker(architecture, "enabled: false", architecturePath);
requireMarker(architecture, "flowhive-sow-grounded-plan-passes", architecturePath);

console.log("Celar AI OpenCloud deferred-runtime contract passed.");
