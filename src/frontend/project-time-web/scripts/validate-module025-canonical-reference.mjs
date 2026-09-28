// Stage 4 — Module 025 canonical-reference (content/citation) admin UI.
// Source-assertion checks (the module's frontend test convention, mirroring
// validate-module025-preview.mjs) proving the admin library UI + author selection
// are wired to the ALREADY-EXISTING backend endpoints only, gated by the same
// default-OFF dark-launch kill-switch, admin-gated for writes via server flags,
// and that the client boundary sends an identifier only (never reference text).
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const repositoryRoot = path.resolve(webRoot, '..', '..', '..');
const read = (...parts) => fs.readFileSync(path.join(repositoryRoot, ...parts), 'utf8');
const backend = (...parts) => read('src', 'backend', 'ProjectTime.Api', ...parts);
const frontend = (...parts) => read('src', 'frontend', 'project-time-web', ...parts);
const requireText = (source, value, message) => assert.ok(source.includes(value), message || `missing: ${value}`);
const forbidText = (source, value, message) => assert.ok(!source.includes(value), message || `unexpected: ${value}`);

// --- Kill-switch policy (default OFF), consumed not changed ---
const policy = backend('Ai', 'Module025ReferenceSourcePolicy.cs');
requireText(policy, 'PROJECTPULSE_MODULE025_REFERENCE_SOURCES_ENABLED', 'reference kill-switch uses the module env-var flag pattern');
requireText(policy, '? value\n            : false;', 'reference kill-switch defaults OFF');

// --- Backend endpoints + gating are pre-existing and unchanged (consumed) ---
const module = backend('Modules', 'Module025CanonicalReferenceModule.cs');
requireText(module, 'private const string RoutePrefix = "/api/module025/canonical-references";', 'canonical route prefix present');
requireText(module, 'app.MapGet(RoutePrefix,', 'list endpoint registered');
requireText(module, 'app.MapGet(RoutePrefix + "/{id:guid}"', 'detail endpoint registered');
requireText(module, 'app.MapPost(RoutePrefix,', 'create endpoint registered');
requireText(module, 'app.MapPut(RoutePrefix + "/{id:guid}"', 'edit endpoint registered');
requireText(module, 'app.MapPost(RoutePrefix + "/{id:guid}/replace"', 'replace endpoint registered');
requireText(module, 'app.MapDelete(RoutePrefix + "/{id:guid}"', 'delete endpoint registered');
requireText(module, 'if (!Module025ReferenceSourcePolicy.Enabled) return Disabled();', 'every endpoint 404s when the kill-switch is OFF');
requireText(module, 'private static IResult Disabled() => Results.NotFound(', 'disabled path returns 404 Not Found');
requireText(module, 'private static bool IsAdmin(Module025AccessContext access) => access.IsAdministrator && !access.IsViewAs;', 'writes are admin-only and never in View-As (server-driven)');

// The generate contract carries an identifier only (never reference text).
const workspaceModule = backend('Modules', 'Module025SowGsdModule.cs');
requireText(workspaceModule, 'ReadCanonicalReferenceSelectionAsync', 'generate reads the optional identifier-only selection');
requireText(workspaceModule, '"canonicalReferenceId"', 'generate body key is canonicalReferenceId');
// Server-driven enablement: the bootstrap reports the kill-switch as a capability
// (mirroring the Stage 3 preview flag) so the client never probes to learn it.
requireText(workspaceModule, 'referenceSources = Module025ReferenceSourcePolicy.Enabled', 'bootstrap exposes the reference-sources kill-switch as a capability');

// --- Frontend: the new library component, DISTINCT from TemplateCatalog ---
const library = frontend('src', 'module025', 'CanonicalReferenceLibrary.jsx');
requireText(library, "import { sessionHeaders } from './protected-download.js';", 'library reuses the protected-download session pattern');
requireText(library, "const BASE = '/api/module025/canonical-references';", 'library targets the existing canonical endpoints only');
requireText(library, 'const isAdmin = Boolean(access?.isAdministrator) && !access?.isViewAs;', 'write gating is server-driven (isAdministrator && !isViewAs)');
requireText(library, 'await requestJson(BASE)', 'list wired');
requireText(library, 'await requestJson(`${BASE}/${id}`)', 'detail wired');
requireText(library, "method: 'POST', body: form", 'create/replace upload multipart .docx');
requireText(library, "method: 'PUT'", 'edit wired');
requireText(library, '/replace`, { method: \'POST\', body: form }', 'replace endpoint wired');
requireText(library, "method: 'DELETE'", 'delete wired');
requireText(library, 'setRefresh((value) => value + 1)', 'mutations refresh library state');
forbidText(library, "import TemplateCatalog", 'library must be distinct from the immutable TemplateCatalog');
requireText(library, 'distinct from the immutable', 'library documents its distinctness from the candidates catalog');
// Non-admins / View-As get an explicit read-only surface.
requireText(library, 'read-only', 'non-admin and View-As sessions are read-only');

// --- Frontend: workspace surfacing, server-driven enablement, author selection ---
const workspace = frontend('src', 'module025', 'SowGsdAuthoringWorkspace.jsx');
requireText(workspace, "import CanonicalReferenceLibrary from './CanonicalReferenceLibrary.jsx';", 'workspace surfaces the new library');
requireText(workspace, 'Boolean(bootstrap?.capabilities?.referenceSources)', 'enablement is read from the server-driven bootstrap capability, not a client probe');
forbidText(workspace, 'setReferenceSourcesEnabled', 'enablement must not be inferred from a failing endpoint probe');
requireText(workspace, "requestJson('/api/module025/canonical-references?active=true')", 'active references load for author selection when enabled (identifier-only source)');
requireText(workspace, 'if (!referenceSourcesEnabled) {', 'no canonical request is issued when the kill-switch is OFF');
requireText(workspace, "referenceSourcesEnabled && templatesTab === 'canonical'", 'library surfaced from the Templates nav, distinct from candidates');
requireText(workspace, 'access={bootstrap.access}', 'library receives server-provided access flags (never client-computed roles)');
// Author selection: active refs only, identifier only.
requireText(workspace, 'activeCanonicalReferences.filter((item) => item.active)', 'author selection only offers active references');
requireText(workspace, 'JSON.stringify({ canonicalReferenceId: selectedCanonicalReferenceId })', 'generate sends the identifier only');
requireText(workspace, 'referenceSourcesEnabled && selectedCanonicalReferenceId', 'selection attached only when enabled and chosen (empty body otherwise = today)');
forbidText(workspace, 'sourceText: selectedCanonicalReference', 'the author boundary must never send reference text');

// --- CSS uses the module theme tokens so the Module surfaces gate passes ---
const css = frontend('src', 'module025', 'canonical-reference-library.css');
requireText(css, 'var(--m025-text)', 'library CSS uses the module text token');
requireText(css, 'var(--m025-surface)', 'library CSS uses the module surface token');
requireText(css, 'var(--m025-border)', 'library CSS uses the module border token');

console.log('validate:module025-canonical-reference OK');
