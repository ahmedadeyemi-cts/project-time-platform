import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const requireMarker = (source, marker, label) => {
  if (!source.includes(marker)) throw new Error(`${label}: missing ${marker}`);
};
const forbidMarker = (source, marker, label) => {
  if (source.includes(marker)) throw new Error(`${label}: prohibited ${marker}`);
};

const main = read('src/frontend/project-time-web/src/main.jsx');
const portal = read('src/frontend/project-time-web/src/ProjectForgeFlowHiveSyncPortal.jsx');
const flowHive = read('src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx');
const repair = read('src/frontend/project-time-web/scripts/repair-module-066-generated-jsx.mjs');
const generatorPath = path.join(root, 'src/backend/ProjectTime.Api/build/generate-celar-ai-universal-answer-reliability.awk');
const productionPath = path.join(root, 'src/backend/ProjectTime.Api/Modules/CelarAiProductionPlatformModule.cs');
const generator = fs.readFileSync(generatorPath, 'utf8');
const generatedProduction = execFileSync(
  'awk',
  ['-v', 'mode=production', '-f', generatorPath, productionPath],
  { encoding: 'utf8', maxBuffer: 8 * 1024 * 1024 }
);
const reliabilityGenerator = read('src/backend/ProjectTime.Api/build/generate-celar-ai-universal-answer-reliability-service.py');
const publicFacts = read('src/backend/ProjectTime.Api/Ai/CelarAiAuthoritativePublicFactService.cs');
const operationsIntent = read('src/backend/ProjectTime.Api/Modules/CelarAiOperationsIntentModule.cs');
const projectSummaryPatch = read('deployment/rocky-linux/apply-remaining-psa-module-api-patch.sh');

if (main.indexOf("import './runtime-browser-compatibility.js';") > main.indexOf("import React from 'react';")) {
  throw new Error('Browser compatibility must load before React.');
}
requireMarker(main, "ProjectForgeFlowHiveSyncPortal", 'Project Forge portal mount');
requireMarker(portal, 'loadSequenceRef', 'refresh race guard');
requireMarker(portal, 'editRevisionRef', 'edit revision guard');
requireMarker(portal, 'const current = sequence === loadSequenceRef.current', 'stale refresh rejection');
requireMarker(portal, 'newer PM edits were preserved', 'refresh/edit race preservation');
requireMarker(portal, 'newer local edits remain unsaved', 'save race preservation');
requireMarker(portal, '#project-flowhive?projectId=', 'selected project navigation');
requireMarker(repair, "new URLSearchParams(hashQuery).get('projectId')", 'FlowHive project consumption');
requireMarker(repair, "setActiveView('planner')", 'FlowHive planner activation');

for (const marker of [
  "{ id: 'financials', label: 'Financials' }",
  "{ id: 'status', label: 'Status & RAID' }",
  'FlowHiveSaveBar',
  '>Add task</button>',
  '>Delete</button>',
  'moveFlowHiveTaskByOffset',
  'Move to phase',
  'draggable={Boolean(enterprise?.access?.canManage)}'
]) requireMarker(flowHive, marker, 'Module 066 enterprise UI');

requireMarker(generator, 'MapCelarAiOperationsIntentEndpoints', 'operations-intent registration');
requireMarker(generator, 'CelarAiAuthoritativePublicFactService', 'public-fact service registration');
requireMarker(generator, 'PulseAiQuestionPlanner questionPlanner', 'stable product-knowledge planner injection');
requireMarker(generator, 'IsStableProductKnowledgeQuestion', 'stable product-knowledge classifier');
requireMarker(generator, 'ProductKnowledgeAnswer', 'stable product-knowledge deterministic answer');
requireMarker(generator, 'celar_ai_governed_product_knowledge', 'stable product-knowledge provider marker');
requireMarker(generator, 'PersistAuthoritativePublicFactAsync', 'current-fact persistence fast path');
requireMarker(generator, 'authoritativePublicFactPreverified', 'current-fact preverification guard');
requireMarker(generator, 'authoritativePublicFacts.VerifyAsync', 'pre-promotion current-fact verification');
requireMarker(reliabilityGenerator, 'governed_public_ai', 'provider-source exclusion');
requireMarker(reliabilityGenerator, 'authoritative_public_web', 'official source requirement');
requireMarker(reliabilityGenerator, 'material_claim_citation_support_missing', 'claim citation gate');
requireMarker(reliabilityGenerator, 'conflicting_evidence_requires_review', 'source conflict blocker');

const chatStart = generatedProduction.indexOf('private static async Task<IResult> ChatAsync');
if (chatStart < 0) throw new Error('generated runtime chat: ChatAsync was not generated');
const productPlanIndex = generatedProduction.indexOf('questionPlanner.PlanHelpSearch(question).DirectKnowledgeAnswer', chatStart);
const productFastPathIndex = generatedProduction.indexOf('ProductKnowledgeAnswer(directProductKnowledge)', chatStart);
const preverifyIndex = generatedProduction.indexOf('var publicFactVerified = await authoritativePublicFacts.VerifyAsync(', chatStart);
const providerIndex = generatedProduction.indexOf('result = await system.AskAsync(', chatStart);
const fastPersistIndex = generatedProduction.indexOf('result = await PersistAuthoritativePublicFactAsync(', chatStart);
if (productPlanIndex < 0 || productFastPathIndex < 0 || preverifyIndex < 0 || providerIndex < 0 || fastPersistIndex < 0) {
  throw new Error('generated runtime chat: stable product, current-fact, or normal provider path is missing');
}
if (!(productPlanIndex < productFastPathIndex && productFastPathIndex < providerIndex)) {
  throw new Error('generated runtime chat: governed stable product knowledge must resolve before any normal provider generation');
}
if (!(preverifyIndex < fastPersistIndex && fastPersistIndex < providerIndex)) {
  throw new Error('generated runtime chat: authoritative current-fact verification must occur before any normal provider generation');
}
requireMarker(generatedProduction, 'normalized.Contains("flowhive", StringComparison.Ordinal)', 'FlowHive stable product signal');
requireMarker(generatedProduction, 'normalized.Contains("purpose", StringComparison.Ordinal)', 'FlowHive purpose signal');
requireMarker(generatedProduction, 'if (directProductKnowledge is not null)', 'stable product fast-path branch');
requireMarker(generatedProduction, 'if (!ReferenceEquals(publicFactVerified, publicFactSeed))', 'recognized public-fact profile gate');
requireMarker(generatedProduction, 'if (!authoritativePublicFactPreverified)', 'duplicate public-fact retrieval guard');
requireMarker(generatedProduction, 'sourceCount = provisional.Sources.Count', 'persisted authoritative source evidence');

for (const marker of [
  'https://www.whitehouse.gov/administration/',
  'https://rhc.jo/en/jordans-governing-system',
  'https://rhc.jo/en/king-abdullah',
  'SourceType: "authoritative_public_web"',
  'Freshness: "live_retrieved_current"',
  'No provider response was promoted as factual evidence.'
]) requireMarker(publicFacts, marker, 'authoritative public-fact service');
for (const prohibited of [
  'ProjectCode:',
  'ProjectName:',
  'AttachmentIds:',
  'private document text',
  'tool.ResponseJson'
]) forbidMarker(publicFacts, prohibited, 'public retrieval privacy boundary');

requireMarker(operationsIntent, '/api/celar-ai/v1/operations/intent', 'operations-intent route');
requireMarker(operationsIntent, 'serverAuthoritative = true', 'server intent authority');
requireMarker(projectSummaryPatch, 'pr.probability_score', 'Migration 077 probability compatibility');
requireMarker(projectSummaryPatch, 'pr.overall_impact_score', 'Migration 077 impact compatibility');
requireMarker(projectSummaryPatch, 'pr.mitigation_actions', 'Migration 077 mitigation compatibility');
requireMarker(projectSummaryPatch, 'pr.response_plan', 'Migration 077 response-plan compatibility');
forbidMarker(projectSummaryPatch, 'pr.probability, pr.impact', 'retired Migration 011 risk fields');
forbidMarker(projectSummaryPatch, 'pr.mitigation_plan', 'retired Migration 011 mitigation field');

console.log('FLOWHIVE_ENTERPRISE_UI_CURRENT_MAIN=PASS');
console.log('PROJECT_FORGE_REFRESH_RACE_GUARD=PASS');
console.log('PROJECT_FORGE_SELECTED_PROJECT_BRIDGE=PASS');
console.log('CELAR_AI_AUTHORITATIVE_PUBLIC_FACT_PACKAGE=PASS');
console.log('CELAR_AI_CURRENT_FACT_FAST_PATH=PASS');
console.log('CELAR_AI_STABLE_PRODUCT_FAST_PATH=PASS');
console.log('CELAR_AI_OPERATIONS_INTENT_REGISTRATION=PASS');
console.log('PROJECT_MANAGEMENT_MIGRATION_077_COMPATIBILITY=PASS');
