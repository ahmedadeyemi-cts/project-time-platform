import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..');
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const exists = (relative) => fs.existsSync(path.join(root, relative));
const checks = [];

function check(name, condition, evidence) {
  checks.push(Boolean(condition));
  console.log(`MODULE011_MANAGED_ARCHITECTURE_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

const files = {
  backend: 'src/backend/ProjectTime.Api/Modules/Module064074NativeAdministration.cs',
  overview: 'src/frontend/project-time-web/src/CelarAiArchitectureOverview.jsx',
  catalog: 'src/frontend/project-time-web/src/CelarAiArchitectureCatalog.jsx',
  catalogCss: 'src/frontend/project-time-web/src/celar-ai-architecture-catalog.css',
  nativePanel: 'src/frontend/project-time-web/src/NativeModuleAdministrationPanel.jsx',
  migration032: 'database/migrations/032_projectpulse_native_administration_documents.sql',
  migration081: 'database/migrations/081_celar_ai_private_runtime_activation.sql',
  deployController: '.github/workflows/projectpulse-deploy-test.yml',
  deferredWorkflow: '.github/workflows/projectpulse-deploy-celar-ai-private-runtime-test.yml',
  architectureReadme: 'docs/modules/module-011-pulse-ai/architecture/v2.0/README.md',
  architectureGenerator: 'docs/modules/module-011-pulse-ai/architecture/v2.0/source/generate_celar_architecture_v2_0.py',
  architecturePdf: 'docs/modules/module-011-pulse-ai/architecture/v2.0/US_Signal_Celar_AI_Private_Intelligence_Architecture_v2.0.pdf',
  architectureDocx: 'docs/modules/module-011-pulse-ai/architecture/v2.0/US_Signal_Celar_AI_Private_Intelligence_Architecture_v2.0.docx',
  openCloudDiagram: 'docs/modules/module-011-pulse-ai/architecture/v2.0/US_Signal_Celar_AI_OpenCloud_Private_Runtime_Architecture_v2.0.svg'
};

for (const [name, relative] of Object.entries(files)) check(`FILE_${name.toUpperCase()}`, exists(relative), relative);
if (checks.some((value) => !value)) process.exit(1);

const backend = read(files.backend);
const overview = read(files.overview);
const catalog = read(files.catalog);
const catalogCss = read(files.catalogCss);
const nativePanel = read(files.nativePanel);
const deployController = read(files.deployController);
const deferredWorkflow = read(files.deferredWorkflow);
const architectureReadme = read(files.architectureReadme);
const architectureGenerator = read(files.architectureGenerator);
const openCloudDiagram = read(files.openCloudDiagram);

check(
  'MODULE011_NATIVE_CATALOG',
  backend.includes('["011"] = new(')
    && backend.includes('"Celar AI Architecture Component Catalog"')
    && backend.includes('CelarAiArchitectureDocument'),
  'Module 011 uses the existing versioned native-administration document store'
);
check(
  'NO_NEW_SCHEMA_REQUIRED',
  backend.includes('private const string MigrationFile = "032_projectpulse_native_administration_documents.sql"')
    && backend.includes('projectpulse_native_admin_documents')
    && backend.includes('projectpulse_native_admin_document_revisions'),
  'catalog persistence reuses migration 032 and audited revisions'
);
check(
  'MANAGED_FIELDS',
  ['componentId', 'name', 'layer', 'architectureState', 'placement', 'technology', 'versionOrModel', 'configurationName', 'readinessSource', 'purpose', 'dependsOn', 'ownerUserId', 'includeInDiagram', 'notes']
    .every((field) => backend.includes(`Field("${field}"`)),
  'editable non-secret component metadata'
);
check(
  'DEFERRED_RUNTIME_COMPONENTS',
  ['opencloud-runtime-vm', 'ollama', 'tesseract-5', 'clamav', 'private-document-worker']
    .every((component) => backend.includes(`"${component}"`) && catalog.includes(`'${component}'`))
    && backend.includes('"planned_opencloud"')
    && backend.includes('"deferred_opencloud"'),
  'OpenCloud VM, Ollama, Tesseract 5, ClamAV, and worker are explicit planned/deferred records'
);
check(
  'CURRENT_PR556_COMPONENTS',
  ['module-011-workspace', 'internal-data-intelligence', 'module-064-router', 'pulse-postgresql']
    .every((component) => backend.includes(`"${component}"`))
    && backend.includes('"Migration 080"'),
  'deployed Module 011, migration 080 intelligence, routing, and data plane are represented'
);
check(
  'CATALOG_READS_MODULE011',
  catalog.includes("const CATALOG_URL = '/api/native-administration/011/document';")
    && catalog.includes('saved Module 011 catalog')
    && catalog.includes('source-controlled fallback'),
  'live viewer reads the saved catalog and fails visibly to a safe baseline'
);
check(
  'CATALOG_EDITING_GOVERNED',
  catalog.includes('state.canManage')
    && catalog.includes('moduleNumber="011"')
    && catalog.includes('onDocumentChanged={acceptDocument}')
    && nativePanel.includes('onDocumentChanged?.(body.document || document)')
    && nativePanel.includes('expectedRevision'),
  'actual-session administrators edit through optimistic, versioned persistence'
);
check(
  'CATALOG_DOES_NOT_ACTIVATE',
  catalog.includes('does not deploy or configure it')
    && catalog.includes('No private-runtime activation performed')
    && catalog.includes('Migration 081 remains absent')
    && catalog.includes('Saving this catalog never activates infrastructure or changes live routing.'),
  'architecture metadata is not represented as runtime activation'
);
check(
  'TOPOLOGY_ONE_VM_THREE_CONTAINERS',
  catalog.includes('one planned OpenCloud Linux virtual machine containing separate Ollama, Tesseract 5, and ClamAV containers')
    && catalog.includes("resolve('ollama')")
    && catalog.includes("resolve('tesseract-5')")
    && catalog.includes("resolve('clamav')")
    && catalog.includes('One VM for Test/UAT · three isolated containers'),
  'the live Module 011 topology matches the approved OpenCloud design'
);
check(
  'OVERVIEW_MOUNTS_CATALOG',
  overview.includes("import CelarAiArchitectureCatalog from './CelarAiArchitectureCatalog.jsx';")
    && overview.includes('<CelarAiArchitectureCatalog />')
    && overview.includes('managed component register below separates what is deployed now'),
  'existing accessible logical diagram is paired with the managed deployment view'
);
check(
  'RESPONSIVE_ENTERPRISE_STYLES',
  catalogCss.includes('.celar-ai-runtime-flow__vm')
    && catalogCss.includes('.celar-ai-component-grid')
    && catalogCss.includes('@media (max-width: 720px)')
    && catalogCss.includes('[data-theme="dark"]'),
  'topology, cards, mobile, and dark-theme treatments are scoped'
);
check(
  'PRIVATE_RUNTIME_STILL_DEFERRED',
  !deployController.includes('081_celar_ai_private_runtime_activation')
    && deferredWorkflow.includes('OPEN_CLOUD_PRIVATE_RUNTIME_DEPLOYMENT=DISABLED')
    && deferredWorkflow.includes('AZURE_PRIVATE_RUNTIME_MUTATION=NONE'),
  'normal Test deployment excludes migration 081 and the runtime workflow remains non-mutating'
);
check(
  'BRANDED_ARCHITECTURE_PACKAGE_ALIGNED',
  architectureReadme.includes('Ollama')
    && architectureReadme.includes('Tesseract')
    && architectureReadme.includes('ClamAV')
    && architectureGenerator.includes('Current interim state: the additional Azure private-runtime deployment is deferred')
    && openCloudDiagram.includes('ONE PRIVATE LINUX VM')
    && openCloudDiagram.includes('Ollama')
    && openCloudDiagram.includes('Tesseract Adapter')
    && openCloudDiagram.includes('ClamAV'),
  'Module 011 catalog and canonical v2.0 package tell the same deployment story'
);

console.log(`MODULE011_MANAGED_ARCHITECTURE_CHECKS=${checks.length}`);
if (checks.some((value) => !value)) {
  console.error('MODULE011_MANAGED_ARCHITECTURE_CONTRACT=FAILED');
  process.exit(1);
}
console.log('MODULE011_MANAGED_ARCHITECTURE_CONTRACT=PASSED');
