// Stage 3 — in-app SOW/GSD preview. Source-assertion checks (the module's
// frontend test convention) proving the preview UI + backend endpoints are wired,
// gated by a default-OFF kill-switch, and derived from the same exporter output.
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

// --- Kill-switch policy (default OFF) ---
const policy = backend('Ai', 'Module025PreviewPolicy.cs');
requireText(policy, 'PROJECTPULSE_MODULE025_PREVIEW_ENABLED', 'preview kill-switch uses the module env-var flag pattern');
requireText(policy, '? value\n            : false;', 'preview kill-switch defaults OFF');

// --- Backend endpoints, gating, and same-bytes derivation ---
const module = backend('Modules', 'Module025SowGsdModule.cs');
requireText(module, '/preview/sow', 'SOW preview endpoint registered');
requireText(module, '/preview/gsd', 'GSD preview endpoint registered');
requireText(module, 'if (!Module025PreviewPolicy.Enabled) return Results.NotFound();', 'preview endpoints 404 when the kill-switch is OFF');
requireText(module, 'await LoadReadableStateAsync(', 'preview reuses the download auth/scope gate');
requireText(module, 'Module025TemplatePackage.PreviewTrusted(', 'preview uses the shared structured parser on the exporter bytes');
requireText(module, 'context.Response.Headers.CacheControl = "no-store";', 'draft preview is no-store');
requireText(module, 'preview = Module025PreviewPolicy.Enabled', 'bootstrap advertises the preview capability from the kill-switch');

// The upload-oriented preview keeps its Validate gate; trusted preview skips it
// but reuses the identical renderer (no divergent second renderer).
const catalog = backend('Modules', 'Module025TemplateCatalog.cs');
requireText(catalog, 'internal static Module025TemplatePreview PreviewTrusted(', 'PreviewTrusted reuses the shared renderer');
requireText(catalog, 'private static Module025TemplatePreview Render(', 'shared Render extracted for both preview paths');

// --- Frontend UI, gated by the capability the kill-switch controls ---
const workspace = frontend('src', 'module025', 'SowGsdAuthoringWorkspace.jsx');
requireText(workspace, "import { downloadProtected, sessionHeaders } from './protected-download.js';", 'preview fetch uses the session-header pattern');
requireText(workspace, 'const previewEnabled = Boolean(bootstrap?.capabilities?.preview);', 'preview UI gated by the backend capability flag');
requireText(workspace, '/preview/${kind}?', 'preview fetch calls the preview endpoint');
requireText(workspace, 'headers: sessionHeaders()', 'preview fetch sends session headers');
requireText(workspace, 'function PreviewModal(', 'preview modal component present');
requireText(workspace, 'function SowPreviewBody(', 'SOW section/paragraph/table renderer present');
requireText(workspace, 'function GsdPreviewBody(', 'GSD sheet-tab / cell-grid renderer present');
requireText(workspace, "m025-preview-badge--${isDraft ? 'draft' : 'confirmed'}", 'DRAFT vs confirmed badge rendered');
requireText(workspace, "isDraft ? 'DRAFT — not approved' : 'Confirmed'", 'badge clearly labels draft vs confirmed');
requireText(workspace, "openPreview('sow', 'draft')", 'Preview draft SOW control present');
requireText(workspace, "openPreview('gsd', 'draft')", 'Preview draft GSD control present');
requireText(workspace, "openPreview('sow', 'confirmed')", 'Preview confirmed SOW control present');
requireText(workspace, "openPreview('gsd', 'confirmed')", 'Preview confirmed GSD control present');
// The controls must be inside a previewEnabled guard so OFF ⇒ no UI control.
requireText(workspace, 'previewEnabled ? <PreviewModal', 'modal only mounts when the feature is enabled');

const css = frontend('src', 'module025', 'sow-gsd-workspace.css');
requireText(css, '.m025-preview-overlay', 'preview modal styles present');
requireText(css, '.m025-preview-grid', 'GSD cell-grid styles present');

console.log('validate:module025-preview OK');
