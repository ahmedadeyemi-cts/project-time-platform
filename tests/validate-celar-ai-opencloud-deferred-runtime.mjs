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

// OpenCloud remains the future destination and stays disabled. The temporary
// Oracle bridge is a separate, manual, protected-Test-only activation path.
requireMarker(architecture, "status: deferred-until-opencloud", architecturePath);
requireMarker(architecture, "topology: shared-private-runtime-vm", architecturePath);
requireMarker(architecture, "enabled: false", architecturePath);
requireMarker(architecture, "flowhive-sow-grounded-plan-passes", architecturePath);

requireMarker(workflow, "workflow_dispatch:", workflowPath);
requireMarker(workflow, "DEPLOY-CELAR-AI-ORACLE-RUNTIME-TO-TEST", workflowPath);
requireMarker(workflow, "environment: test", workflowPath);
requireMarker(workflow, "PRODUCTION_MUTATION=NONE", workflowPath);
requireMarker(workflow, "OPEN_CLOUD_RUNTIME_MUTATION=NONE", workflowPath);
requireMarker(workflow, "https://celarai.onenecklab.com/health", workflowPath);

if (/^\s{2}push:\s*$/m.test(workflow)) {
  throw new Error(`${workflowPath} must not automatically deploy the Oracle bridge from a main push.`);
}
if (workflow.includes("environment: production")) {
  throw new Error(`${workflowPath} must never bind the temporary Oracle bridge to Production.`);
}

console.log("Celar AI OpenCloud deferral and temporary Oracle Test boundary passed.");
