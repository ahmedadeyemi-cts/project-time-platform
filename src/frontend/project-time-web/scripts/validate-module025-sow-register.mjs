import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { assertImportedTargetMarkers, readEffectiveBuildTargets } from './read-effective-build-targets.mjs';

const webRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const repositoryRoot = path.resolve(webRoot, '..', '..', '..');
const read = (...parts) => fs.readFileSync(path.join(repositoryRoot, ...parts), 'utf8');
const backend = (...parts) => read('src', 'backend', 'ProjectTime.Api', ...parts);
const frontend = (...parts) => read('src', 'frontend', 'project-time-web', ...parts);
const requireText = (source, value, message) => assert.ok(source.includes(value), message || value);

const directoryTargets = backend('Directory.Build.targets');
const platformTargets = backend('build', 'PlatformRuntime.targets');
const sowTargets = backend('build', 'Module025SowSell.targets');
const effective = readEffectiveBuildTargets(repositoryRoot);
const effectiveTargets = effective.text;
const generator = backend('build', 'generate-module025-sow-sell.py');
const migration = read('database', 'migrations', '106_module025_sow_sell_register.sql');
const migrationTest = read('tests', 'test-module025-sow-sell-register-migration-106.sh');
const module = backend('Modules', 'Module025SowSellModule.cs');
const worker = backend('Modules', 'Module025SowSellWorker.cs');
const policy = backend('Modules', 'Module025SowSellPolicy.cs');
const protectedUat = read('scripts', 'release-test', 'run-module025-sow-gsd-protected-test-uat.sh');
const browserLifecycle = read('tests', 'module025-sow-register-browser.py');
const editor = frontend('src', 'module025', 'SowGsdAuthoringWorkspace.jsx');
const shell = frontend('src', 'module025', 'SowGsdWorkspace.jsx');
const register = frontend('src', 'module025', 'SowRegister.jsx');

requireText(directoryTargets, '<Import Project="$(MSBuildProjectDirectory)/build/PlatformRuntime.targets" />', 'Directory.Build.targets must import the effective platform targets');
requireText(directoryTargets, '<Import Project="$(MSBuildProjectDirectory)/build/Module025SowSell.targets" />', 'Directory.Build.targets must import the Module 025 generated-source bridge');
assertImportedTargetMarkers(effective.sources, {
  'src/backend/ProjectTime.Api/build/PlatformRuntime.targets': [
    'GenerateWithPrivateTargetAsync',
    'ExternalFactCodes: externalFactCodes',
    'DestinationFiles="$(CelarAiTimesheetGenerated)"'
  ],
  'src/backend/ProjectTime.Api/build/Module025SowSell.targets': [
    'GenerateModule025SowSellSources',
    'Compile Include="$(Module025SowSellGenerated)"'
  ]
});
requireText(effectiveTargets, 'GenerateWithPrivateTargetAsync', 'effective targets must preserve the reviewed private-target route');
requireText(effectiveTargets, 'ExternalFactCodes: externalFactCodes', 'effective targets must preserve the closed external fact-code capsule');
requireText(effectiveTargets, 'SourceFiles="$(MSBuildProjectDirectory)/ProjectPulseAiTimeEntrySuggestionService.cs"', 'effective targets must copy the canonical Timesheet source');
requireText(effectiveTargets, 'DestinationFiles="$(CelarAiTimesheetGenerated)"', 'effective targets must register the generated Timesheet source');
assert.doesNotMatch(effectiveTargets, /ProjectPulseAiProviders\.Local\/s\/\/CelarAiCapabilityTargets\.CelarAi/);
assert.doesNotMatch(effectiveTargets, /sed[^\n]*ProjectPulseAiProviders\.Local[^\n]*CelarAiCapabilityTargets\.CelarAi/);
requireText(sowTargets, 'GenerateModule025SowSellSources', 'Module 025 generated-source target must run before compile');
requireText(sowTargets, 'Compile Remove="Modules/Module025SowGsdModule.cs"', 'canonical Module 025 editor must not compile beside its generated partial');
requireText(sowTargets, 'Compile Include="$(Module025SowSellGenerated)"', 'generated Module 025 partial must be compiled');
requireText(generator, 'builder.Services.AddModule025SowSell();', 'generated Program must register the Module 025 service');
requireText(generator, 'app.MapModule025SowSellEndpoints();', 'generated Program must map retained-version endpoints');

for (const marker of [
  'waitForDetailedScopeGeneration',
  'module025_detailed_scope_generation_queued',
  'Generate detailed scope',
  "action === 'generate'",
  "runAction('confirm'",
  "runAction('reopen'",
  '/sow.docx',
  '/gsd.xlsx'
]) requireText(editor, marker, `original SOW editor behavior missing: ${marker}`);
for (const marker of [
  "import SowGsdAuthoringWorkspace from './SowGsdAuthoringWorkspace.jsx';",
  "import SowRegister from './SowRegister.jsx';",
  'Keep the existing editor mounted',
  'm025-authoring-panel',
  'm025-register-panel'
]) requireText(shell, marker, `Module 025 shell integration missing: ${marker}`);
requireText(register, '/versions?page=', 'register must read retained versions');
requireText(register, '/versions/${version.versionId}/sow.docx', 'register must download the retained SOW bytes');
requireText(register, '/versions/${version.versionId}/gsd.xlsx', 'register must download the retained GSD bytes');

for (const marker of [
  'module025_sow_gsd_generation_snapshots',
  'module025_sow_gsd_versions',
  'module025_sow_gsd_artifact_issuance',
  'module025_sow_sell_submissions',
  'module025_sow_sell_receipts',
  'module025_capture_generation_snapshot',
  'module025_validate_sell_receipt',
  "VALUES('106_module025_sow_sell_register'",
  'ON CONFLICT (migration_id) DO NOTHING',
  'BEFORE UPDATE OR DELETE',
  'sow_sha256 <> v.sow_sha256 OR NEW.gsd_sha256 <> v.gsd_sha256'
]) requireText(migration, marker, `migration 106 contract missing: ${marker}`);

for (const marker of [
  '001_initial_schema.sql',
  '099_module025_sow_gsd_workspace.sql',
  '106_module025_sow_sell_register.sql',
  'ON CONFLICT (version_id,artifact_kind) DO NOTHING',
  'repeated_downloads_one_first_issuance_each',
  'mismatched receipt was not rejected',
  'MODULE025_SOW_SELL_REGISTER_MIGRATION_106=PASS'
]) requireText(migrationTest, marker, `migration 106 database test missing: ${marker}`);

for (const marker of [
  'CaptureConfirmedSowVersionAsync',
  'ContentSha256 == fingerprint',
  'ON CONFLICT (version_id,artifact_kind) DO NOTHING',
  'X-Content-SHA256',
  'No duplicate submission was created',
  'current_version_required'
]) requireText(module, marker, `retained-version interaction missing: ${marker}`);
for (const marker of [
  'FOR UPDATE OF d SKIP LOCKED',
  "sell_status='publishing'",
  'needs_reconciliation',
  'SELL_RECEIPT_MISMATCH',
  'module025_sow_sell_notification_outbox'
]) requireText(worker, marker, `SELL worker safety interaction missing: ${marker}`);
requireText(policy, 'SELL_DOCUMENT_WRITE_ADAPTER_REQUIRED', 'SELL must remain explicitly adapter-gated');
for (const marker of [
  'auth_request PUT "/api/module025/sow-gsd/$ENGAGEMENT_ID"',
  'auth_request POST "/api/module025/sow-gsd/$ENGAGEMENT_ID/confirm"',
  'auth_request POST "/api/module025/sow-gsd/$ENGAGEMENT_ID/versions"',
  'VERSION_REPEAT_RESPONSE',
  'download_twice sow.docx',
  'download_twice gsd.xlsx',
  'sha256sum "$first"',
  'MODULE025_RETAINED_VERSION_API_LIFECYCLE=PASS'
]) requireText(protectedUat, marker, `Protected-Test retained-version lifecycle missing: ${marker}`);
for (const marker of [
  'SOW Register & SELL',
  'Download SOW v1',
  'Download GSD v1',
  'File integrity',
  'await page.reload',
  'generation_posts',
  'MODULE025_REGISTER_BROWSER_DISPLAY=PASS'
]) requireText(browserLifecycle, marker, `Module 025 retained-version browser lifecycle missing: ${marker}`);

console.log('MODULE025_EFFECTIVE_GENERATED_SOURCES=PASSED');
console.log('MODULE025_ORIGINAL_EDITOR_AND_REGISTER=PASSED');
console.log('MODULE025_MIGRATION106_RETAINED_VERSION_CONTRACT=PASSED');
console.log('MODULE025_RETAINED_VERSION_AND_SELL_INTERACTIONS=PASSED');
