import fs from "node:fs";

const program = fs.readFileSync("src/backend/ProjectTime.Api/Program.cs", "utf8");
const app = fs.readFileSync("src/frontend/project-time-web/src/App.jsx", "utf8");
const pkg = fs.readFileSync("src/frontend/project-time-web/package.json", "utf8");
const dockerfile = fs.readFileSync("deployment/containers/web/Dockerfile", "utf8");

const modules = [
  ["075", "IntegrationEventGateway", "integration-event-gateway", "IntegrationEventGatewayModule.cs"],
  ["077", "ReleaseDeploymentControl", "release-deployment-control", "ReleaseDeploymentControlModule.cs"],
  ["078", "ObservabilitySloHealth", "observability-slo-health", "ObservabilitySloHealthModule.cs"],
  ["079", "DataGovernanceRetention", "data-governance-retention", "DataGovernanceRetentionModule.cs"],
  ["080", "CustomerDeliveryAcceptance", "customer-delivery-acceptance", "CustomerDeliveryAcceptanceModule.cs"]
];

let checks = 0;
let failures = 0;
const test = (name, ok) => {
  checks += 1;
  if (!ok) failures += 1;
  console.log(`MODULES_075_080_${name}=${ok ? "PASSED" : "FAILED"}`);
};
const occurrences = (source, token) => source.split(token).length - 1;

for (const [id, component, route, backendFile] of modules) {
  test(`MODULE_${id}_API_MAP_ONCE`, occurrences(program, `app.Map${component}Endpoints();`) === 1);
  test(`MODULE_${id}_IMPORT_ONCE`, occurrences(app, `import ${component}Center from './${component}Center.jsx';`) === 1);
  test(`MODULE_${id}_ROUTE_ONCE`, occurrences(app, `activeRoute === '${route}'`) === 1);
  test(`MODULE_${id}_MOUNT_ONCE`, occurrences(app, `<${component}Center authSession={authSession} />`) === 1);
  test(`MODULE_${id}_NAV_REGISTERED`, app.includes(`route: '${route}'`) && app.includes(`navLabel: 'MODULE ${id}'`));
  test(`MODULE_${id}_VALIDATOR_CHAINED`, pkg.includes(`validate:module${id}`));
  test(`MODULE_${id}_CONTAINER_CONTEXT`, dockerfile.includes(`Modules/${backendFile}`));
}

test("CROSS_VALIDATOR_CHAINED", pkg.includes("validate:modules075080-runtime"));
test("RUNTIME_MARKERS", app.includes("MODULES_075_080_RUNTIME_ROUTES_START") && program.includes("MODULES_075_080_RUNTIME_ENDPOINT_MAP_START"));
test("NO_EXTERNAL_ACTIVATION", !program.includes("HttpClient") || modules.every(([, component]) => {
  const modulePath = `src/backend/ProjectTime.Api/Modules/${component}Module.cs`;
  const source = fs.readFileSync(modulePath, "utf8");
  return !source.includes("HttpClient") && source.includes("423Locked") && source.includes("requestBodyRead = false");
}));

console.log(`MODULES_075_080_RUNTIME_VALIDATION_CHECKS=${checks}`);
console.log(`MODULES_075_080_RUNTIME_CONTRACT=${failures ? "FAILED" : "PASSED"}`);
process.exitCode = failures ? 1 : 0;
