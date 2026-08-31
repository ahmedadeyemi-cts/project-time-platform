import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const requireText = (source, token, label) => {
  if (!source.includes(token)) throw new Error(`${label} is missing required contract token: ${token}`);
};

const modulePath = 'src/backend/ProjectTime.Api/Modules/WorkRegisterDocumentContinuityModule.cs';
const authorizationPath = 'src/backend/ProjectTime.Api/Modules/WorkRegisterAuthorization.cs';
const integrityPath = 'src/frontend/project-time-web/src/work-register-document-integrity.js';
const workRegisterPath = 'src/frontend/project-time-web/src/WorkRegisterCenter.jsx';
const resolverPath = 'src/backend/ProjectTime.Api/Modules/ProjectPlanningDocumentResolver.cs';
const workflowPath = '.github/workflows/shared-project-document-planning-ci.yml';
const manifestPath = '.github/shared-project-document-planning-governed-release-files.txt';

const moduleSource = read(modulePath);
const authorizationSource = read(authorizationPath);
const integritySource = read(integrityPath);
const workRegisterSource = read(workRegisterPath);
const resolverSource = read(resolverPath);
const workflowSource = read(workflowPath);
const manifestSource = read(manifestPath);

for (const token of [
  'WORK_REGISTER_DOCUMENT_CONTINUITY_V2',
  'TryHandleAsync(HttpContext context)',
  "SET status = 'deleted'",
  "'document_deleted'",
  'DeactivateSharedDocumentBridgeAsync',
  'AND project_id = @project_id',
  'ProjectPulseUploadStorage.ResolveExistingStoredFile',
  'workRegister055C = "removed"',
  'projectWorkspace019 = "removed"',
  'flowHiveProjectForgeAuthority = "removed"',
  'A delete reason is required for immutable audit history.'
]) requireText(moduleSource, token, 'Work Register document continuity module');

for (const token of [
  'isDocumentContinuityPath',
  'WorkRegisterDocumentContinuityModule.TryHandleAsync(context)',
  'ProjectManagementWorkRegisterScope.TryHandleReadAsync',
  'route:projectId',
  'IsAssignedProjectManagerAsync'
]) requireText(authorizationSource, token, 'Work Register authorization');

for (const token of [
  'mergeCanonicalDocuments',
  '/documents`',
  '.work-register-document-card',
  '.work-register-document-actions',
  'canonicalDocumentForCard',
  'canonicalDocument.canDelete === false',
  "normalizedType === 'SOW' || normalizedType === 'GSD'",
  '`Delete ${normalizedType}`',
  "method: 'DELETE'",
  'required audit reason',
  'active FlowHive/Project Forge evidence',
  "['deleted', 'removed', 'purged']",
  'MutationObserver',
  'remember055cRequestContext',
  '__projectPulse055cRequestHeaders',
  '__projectPulse055cCredentials',
  "deleteHeaders.set('Content-Type', 'application/json')",
  'data-projectpulse-055c-shared-delete'
]) requireText(integritySource, token, 'Module 055C frontend continuity');

for (const token of [
  'work-register-document-card',
  'work-register-document-actions',
  'archiveWorkRegisterDocument(document)',
  'document.canArchive'
]) requireText(workRegisterSource, token, 'Manage Existing Project document cards');

for (const token of [
  'document.is_active=TRUE',
  'ActiveWorkRegisterSource',
  'SelectCurrent'
]) requireText(resolverSource, token, 'FlowHive/Forge document authority');

for (const token of [
  "'src/backend/ProjectTime.Api/Modules/WorkRegisterDocumentContinuityModule.cs'",
  "'src/backend/ProjectTime.Api/Modules/WorkRegisterAuthorization.cs'",
  "'src/frontend/project-time-web/src/work-register-document-integrity.js'",
  "'tests/validate-work-register-document-continuity.mjs'",
  'node tests/validate-work-register-document-continuity.mjs'
]) requireText(workflowSource, token, 'Shared project-document planning workflow');

for (const entry of [modulePath, authorizationPath, integrityPath, 'tests/validate-work-register-document-continuity.mjs']) {
  requireText(manifestSource, entry, 'Governed release manifest');
}

console.log('WORK_REGISTER_DOCUMENT_CONTINUITY=VERIFIED');
console.log('MODULE_055C_CANONICAL_DOCUMENT_READ=VERIFIED');
console.log('MODULE_055C_NATIVE_MANAGE_EXISTING_DELETE=VERIFIED');
console.log('MODULE_055C_019_SHARED_DELETE=VERIFIED');
console.log('MODULE_055C_DELETE_AUTH_CONTEXT=VERIFIED');
console.log('FLOWHIVE_FORGE_DELETE_AUTHORITY=VERIFIED');
console.log('PRODUCTION_MUTATION=NONE');
