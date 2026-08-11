import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = fileURLToPath(new URL('../', import.meta.url));
const productionPath = path.join(webRoot, 'src', 'CelarAiProductionPlatform.jsx');
let source = fs.readFileSync(productionPath, 'utf8');

function replaceOnce(anchor, replacement, label) {
  const occurrences = source.split(anchor).length - 1;
  if (occurrences !== 1) {
    throw new Error(`CELAR_AI_UNIVERSAL_ANSWER_INJECTOR_${label}=FAILED expected=1 actual=${occurrences}`);
  }
  source = source.replace(anchor, replacement);
}

if (!source.includes("import CelarAiAnswerReliabilityWorkbench from './CelarAiAnswerReliabilityWorkbench.jsx';")) {
  replaceOnce(
    "import CelarAiEnterprisePlatform from './CelarAiEnterprisePlatform.jsx';",
    "import CelarAiAnswerReliabilityWorkbench from './CelarAiAnswerReliabilityWorkbench.jsx';\nimport CelarAiEnterprisePlatform from './CelarAiEnterprisePlatform.jsx';",
    'IMPORT'
  );
}

if (!source.includes("['reliability', 'Answer Reliability'")) {
  replaceOnce(
    "  ['tools', 'Tools & Coverage', 'Live APIs, governed tools, troubleshooting, and system coverage'],\n  ['datasets', 'Datasets', 'Reviewed immutable training and evaluation inputs'],",
    "  ['tools', 'Tools & Coverage', 'Live APIs, governed tools, troubleshooting, and system coverage'],\n  ['reliability', 'Answer Reliability', 'Authoritative tools, evidence planning, freshness, citations, and fail-closed quality gates'],\n  ['datasets', 'Datasets', 'Reviewed immutable training and evaluation inputs'],",
    'TAB'
  );
}

if (!source.includes("activeTab === 'reliability'")) {
  replaceOnce(
    "    if (activeTab === 'tools') return <PulseAiSystemIntelligenceWorkbench />;\n    if (activeTab === 'datasets') return <DatasetWorkspace data={records.datasets} refresh={refresh} canManage={canManage} />;",
    "    if (activeTab === 'tools') return <PulseAiSystemIntelligenceWorkbench />;\n    if (activeTab === 'reliability') return <CelarAiAnswerReliabilityWorkbench />;\n    if (activeTab === 'datasets') return <DatasetWorkspace data={records.datasets} refresh={refresh} canManage={canManage} />;",
    'MOUNT'
  );
}

for (const marker of [
  "import CelarAiAnswerReliabilityWorkbench from './CelarAiAnswerReliabilityWorkbench.jsx';",
  "['reliability', 'Answer Reliability'",
  "activeTab === 'reliability'"
]) {
  if (!source.includes(marker)) {
    throw new Error(`CELAR_AI_UNIVERSAL_ANSWER_INJECTOR_MARKER=FAILED marker=${marker}`);
  }
}

fs.writeFileSync(productionPath, source, 'utf8');
console.log('CELAR_AI_UNIVERSAL_ANSWER_UI_IMPORT=INJECTED');
console.log('CELAR_AI_UNIVERSAL_ANSWER_UI_TAB=INJECTED');
console.log('CELAR_AI_UNIVERSAL_ANSWER_UI_MOUNT=INJECTED');

await import('./inject-celar-ai-ask-operations.mjs');
await import('./inject-module-076-celar-ai-operations.mjs');
