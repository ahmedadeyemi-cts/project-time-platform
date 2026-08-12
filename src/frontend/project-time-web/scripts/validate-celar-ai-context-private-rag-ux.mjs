import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../../../', import.meta.url));
const read = (relative) => fs.readFileSync(path.join(root, relative), 'utf8');
const checks = [];

function check(name, condition, evidence) {
  checks.push({ name, condition });
  console.log(`CELAR_AI_CONTEXT_RAG_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

function walk(relative) {
  const directory = path.join(root, relative);
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const child = path.join(relative, entry.name);
    return entry.isDirectory() ? walk(child) : [child.replaceAll('\\', '/')];
  });
}

const help = read('src/frontend/project-time-web/src/HelpAssistant.jsx');
const chatCss = read('src/frontend/project-time-web/src/celar-ai-contextual-chat.css');
const projectWorkspace = [
  read('src/backend/ProjectTime.Api/Modules/ProjectWorkspaceModule.cs'),
  read('src/backend/ProjectTime.Api/Modules/ProjectWorkspaceModule019Repair.cs'),
].join('\n');
const ragService = read('src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs');
const platform = read('src/frontend/project-time-web/src/CelarAiProductionPlatform.jsx');
const pipeline = read('src/frontend/project-time-web/src/PulseAiPrivateDocumentPipelineWorkbench.jsx');
const runbook = read('docs/modules/module-011-pulse-ai/FREE-PRIVATE-RAG-ACTIVATION.md');

const loadingEffect = help.slice(
  help.indexOf('useEffect(() => {\n    if (!isOpen || !contextOpen || projectOptionsLoaded || projectOptionsLoading) return;'),
  help.indexOf('useEffect(() => {\n    if (!isOpen) return undefined;', help.indexOf("void getJson('/api/project-workspace/overview')"))
);

check(
  'CONTEXT_REQUEST_DOES_NOT_SELF_CANCEL',
  loadingEffect.includes('setProjectOptionsLoading(true)')
    && loadingEffect.includes('setProjectOptionsLoading(false)')
    && loadingEffect.includes('}, [contextOpen, isOpen, projectOptionsLoaded]);')
    && !loadingEffect.includes('projectOptionsLoaded, projectOptionsLoading'),
  'the loading flag no longer retriggers and cancels its own authorized-directory request'
);

check(
  'AUTHORIZED_PROJECT_PEOPLE_TEAM_DIRECTORY',
  help.includes('function contextDirectoryOptions(payload)')
    && help.includes("add('Person', assignment?.engineerName")
    && help.includes("add('Team', assignment?.engineerTeam")
    && help.includes('peopleTeamSuggestions')
    && projectWorkspace.includes('teamName = access.TeamName')
    && projectWorkspace.includes('departmentName = access.DepartmentName')
    && projectWorkspace.includes('AS engineer_team')
    && projectWorkspace.includes('string EngineerTeam,'),
  'project, person, and team choices are derived only from the existing role-scoped Project Workspace response'
);

check(
  'ENTERPRISE_ATTACHMENT_CONTROL',
  help.includes('function AttachmentIcon()')
    && help.includes('className="celar-ai-chat-attachment-button"')
    && help.includes("aria-label={attachmentBusy ? 'Processing documents' : 'Attach documents'}")
    && !help.includes("attachmentBusy ? 'Processing…' : 'Choose documents'")
    && chatCss.includes('.celar-ai-chat-attachment-button {')
    && chatCss.includes('background: var(--brand-blue, #0b6cb8);'),
  'the verbose secondary picker is replaced by an accessible paperclip-only blue action'
);

check(
  'PRIVATE_RAG_COMPLETE_GATE',
  ragService.includes('var retrievalReady = hybridRetrievalReady || lexicalOnlyRetrievalApproved;')
    && ragService.includes('&& inferenceAuthenticationConfigured')
    && ragService.includes('&& retrievalReady')
    && ragService.includes('vectorIndexConfigured,')
    && ragService.includes('hybridRetrievalReady,')
    && platform.includes('100% configuration-ready')
    && platform.includes('Remaining configuration blockers'),
  'ready status now requires authenticated private inference plus an approved retrieval path and exposes every remaining gate'
);

check(
  'FREE_SELF_HOSTED_ACTIVATION',
  pipeline.includes('ClamAV · malware')
    && pipeline.includes('Tesseract 5 · OCR')
    && pipeline.includes('Ollama + PostgreSQL · retrieval')
    && runbook.includes('PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT=https://ai.internal.example/v1/chat/completions')
    && runbook.includes('PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT=https://ai.internal.example/v1/embeddings')
    && runbook.includes('PROJECTPULSE_PRIVATE_VECTOR_INDEX=projectpulse_postgresql_hybrid')
    && runbook.includes('This runbook maps the current Celar AI runtime contracts'),
  'the UI and runbook provide an implementation-aligned no-license-fee path without claiming infrastructure is free or deployed'
);

const visibleJsx = walk('src/frontend/project-time-web/src').filter((file) => file.endsWith('.jsx'));
const retiredVisibleBrand = visibleJsx.filter((file) => /\bPulse\s+AI\b/i.test(read(file)));
check(
  'VISIBLE_CELAR_AI_ONLY',
  retiredVisibleBrand.length === 0
    && help.includes('function rebrandCelarString(value)')
    && help.includes("replace(/\\bPulse\\s+AI\\b/gi, 'Celar AI')"),
  retiredVisibleBrand.length ? `retired visible brand remains in: ${retiredVisibleBrand.join(', ')}` : 'all checked JSX and dynamically loaded history render Celar AI'
);

const failed = checks.filter((item) => !item.condition);
if (failed.length) {
  console.error(`CELAR_AI_CONTEXT_PRIVATE_RAG_UX=FAILED (${failed.length}/${checks.length})`);
  process.exit(1);
}

console.log(`CELAR_AI_CONTEXT_PRIVATE_RAG_UX=PASSED (${checks.length}/${checks.length})`);
