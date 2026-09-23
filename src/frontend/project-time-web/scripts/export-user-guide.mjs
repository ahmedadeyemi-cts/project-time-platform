import fs from 'node:fs';
import path from 'node:path';
import { PROJECTPULSE_MODULES } from '../src/module-availability-registry.js';
import { GUIDE_BASELINE, GUIDE_EDITION } from '../src/guide/guide-model.js';
import { buildGuideCatalog } from '../src/guide/guide-catalog.js';
import { ROLE_REFERENCE, ROLE_NOTES, PLATFORM_GUIDES, HANDOFFS, GLOSSARY } from '../src/guide/guide-content.js';

const at=process.argv.indexOf('--output');
if(at<0 || !process.argv[at+1]) throw new Error('Provide --output <markdown-path>.');
const lines=[`# Pulse System User Guide`, `Edition ${GUIDE_EDITION}. Implementation baseline: \`${GUIDE_BASELINE}\`.`,
  'This learning reference does not grant permissions. Actual role, record state, configuration and service readiness govern every action. The in-application guide verifies navigation access before offering workspace launch links.',
  '## Platform essentials'];
const steps=items=>items.forEach((text,i)=>lines.push(`${i+1}. ${text}`));
const bullets=items=>items.forEach(text=>lines.push(`- ${text}`));
for(const entry of PLATFORM_GUIDES){lines.push(`### ${entry.title}`);steps(entry.steps);lines.push(`Expected result: ${entry.outcome}`);}
lines.push('## Role responsibilities');
for(const role of ROLE_REFERENCE){
  lines.push(`### ${role.title} (${role.code})`,role.purpose,`Responsibility boundary: ${role.boundary}`);bullets(ROLE_NOTES[role.code]);
  for(const step of role.steps) lines.push(`#### ${step.title}`,`Workspace: \`#${step.route}\` (reference only)`, `Input: ${step.input}`,`Action: ${step.action}`,`Output: ${step.output}`,`Acceptance: ${step.acceptance}`,`Next owner: ${step.nextOwner}`,`Exception: ${step.exception}`);
}
lines.push('## Module how-to library');
for(const entry of buildGuideCatalog(PROJECTPULSE_MODULES)){
  const guide=entry.guide;
  lines.push(`### ${entry.moduleNumber || 'Platform'} — ${entry.title}`,`Route: \`#${entry.route}\`. Category: ${entry.group}.`,guide.responsibility,`Audience reference: ${guide.roles.join(', ')}`, '#### Before you begin');bullets(guide.prerequisites);
  for(const procedure of guide.procedures){lines.push(`#### ${procedure.title}`);steps(procedure.steps);lines.push(`Expected result: ${procedure.outcome}`,`Next owner / handoff: ${procedure.handoff}`);}
  lines.push('#### Limitations and important distinctions');bullets(guide.limitations);
  lines.push('Implementation references (repository-relative paths):');bullets(guide.sources.map(source=>`\`${source}\``));
}
lines.push('## Cross-team handoffs');
for(const flow of HANDOFFS){lines.push(`### ${flow.title}`);steps(flow.stages.map(([owner,route,action])=>`${owner}: ${action} Reference workspace: \`#${route}\`.`));}
lines.push('## Terms and status meanings');
for(const [term,definition] of GLOSSARY) lines.push(`### ${term}`,definition);
lines.push('## Reporting a documentation gap','Include guide edition, module/route, role, exact procedure and expected versus actual behavior. Use sanitized evidence; never include session tokens, passwords, API keys or unnecessary customer data.');
const destination=path.resolve(process.argv[at+1]);fs.mkdirSync(path.dirname(destination),{recursive:true});fs.writeFileSync(destination,lines.join('\n\n')+'\n');
console.log(`USER_GUIDE_MARKDOWN=${destination}`);
