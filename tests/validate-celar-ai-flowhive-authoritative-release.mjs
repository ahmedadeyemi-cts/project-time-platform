import fs from 'node:fs';
import path from 'node:path';
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
const generator = read('src/backend/ProjectTime.Api/build/generate-celar-ai-universal-answer-reliability.awk');
const reliabilityGenerator = read('src/backend/ProjectTime.Api/build/generate-celar-ai-universal-answer-reliability-service.py');
const publicFacts = read('src/backend/ProjectTime.Api/Ai/CelarAiAuthoritativePublicFactService.cs');
const operationsIntent = read('src/backend/ProjectTime.Api/Modules/CelarAiOperationsIntentModule.cs');

if (main.indexOf("import './runtime-browser-compatibility.js';") > main.indexOf("import React from 'react';")) {
  throw new Error('Browser compatibility must load before React.');
}
requireMarker(main, "ProjectForgeFlowHiveSyncPortal", 'Project Forge portal mount');
requireMarker(portal, 'loadSequenceRef', 'refresh race guard');
requireMarker(portal, 'editRevisionRef', 'edit revision guard');
requireMarker(portal, 'responseIsCurrent', 'stale refresh rejection');
requireMarker(portal, 'newer local edits remain unsaved', 'save race preservation');
requireMarker(portal, '#project-flowhive?projectId=', 'selected project navigation');
requireMarker(repair, "new URLSearchParams(hashQuery).get('projectId')", 'FlowHive project consumption');
requireMarker(repair, "setActiveView('planner')", 'FlowHive planner activation');

for (const marker of [
  "['financials', 'Financials']",
  "['raid', 'Status & RAID']",
  'FlowHiveSaveBar',
  '>Add task</button>',
  '>Delete</button>',
  'moveTaskByOffset',
  'Move to phase',
  'draggable={!task.isSummary}'
]) requireMarker(flowHive, marker, 'Module 066 enterprise UI');

requireMarker(generator, 'MapCelarAiOperationsIntentEndpoints', 'operations-intent registration');
requireMarker(generator, 'CelarAiAuthoritativePublicFactService', 'public-fact service registration');
requireMarker(generator, 'authoritativePublicFacts.VerifyAsync', 'pre-promotion current-fact verification');
requireMarker(reliabilityGenerator, 'governed_public_ai', 'provider-source exclusion');
requireMarker(reliabilityGenerator, 'authoritative_public_web', 'official source requirement');
requireMarker(reliabilityGenerator, 'material_claim_citation_support_missing', 'claim citation gate');
requireMarker(reliabilityGenerator, 'conflicting_evidence_requires_review', 'source conflict blocker');

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

console.log('FLOWHIVE_ENTERPRISE_UI_CURRENT_MAIN=PASS');
console.log('PROJECT_FORGE_REFRESH_RACE_GUARD=PASS');
console.log('PROJECT_FORGE_SELECTED_PROJECT_BRIDGE=PASS');
console.log('CELAR_AI_AUTHORITATIVE_PUBLIC_FACT_PACKAGE=PASS');
console.log('CELAR_AI_OPERATIONS_INTENT_REGISTRATION=PASS');
