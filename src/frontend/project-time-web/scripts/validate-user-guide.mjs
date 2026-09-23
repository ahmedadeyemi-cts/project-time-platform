import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { PROJECTPULSE_MODULES } from '../src/module-availability-registry.js';
import { ROLE_GUIDANCE } from '../src/role-permission-model.js';
import { GUIDE_TOPICS, GUIDE_BY_ROUTE, ROLE_REFERENCE, ROLE_NOTES, PLATFORM_GUIDES, HANDOFFS, GLOSSARY, assignedGuideRoles } from '../src/guide/guide-content.js';
import { GUIDE_BASELINE, filterGuideEntries, guideLinkAllowed, safeGuideRoute } from '../src/guide/guide-model.js';
import { buildGuideCatalog } from '../src/guide/guide-catalog.js';

const web = fileURLToPath(new URL('../', import.meta.url));
const repository = path.resolve(web, '../../..');
const catalog = buildGuideCatalog(PROJECTPULSE_MODULES);
const routes = new Set(catalog.map(entry => entry.route));
let assertions = 0;
const check = (condition, message) => { assertions += 1; assert.ok(condition, message); };
check(GUIDE_TOPICS.length === new Set(GUIDE_TOPICS.map(entry => entry.route)).size, 'Duplicate authored topic');
const app = fs.readFileSync(path.join(web, 'src/App.jsx'), 'utf8');
const start = app.indexOf('function getInstalledProjectPulseModuleRegistry()');
const end = app.indexOf('\nfunction ', start + 10);
check(start >= 0 && end > start, 'Installed registry boundary not found');
const installedRoutes = [...app.slice(start, end).matchAll(/route:\s*['"]([^'"]+)['"]/g)].map(match => match[1]);
for (const route of [...PROJECTPULSE_MODULES.map(module => module.route), ...installedRoutes, 'celar-ai-runtime-version']) {
  check(Boolean(GUIDE_BY_ROUTE[route]), `Missing reviewed instructions for ${route}`);
}
for (const entry of GUIDE_TOPICS) {
  check(routes.has(entry.route) && safeGuideRoute(entry.route), `Unknown/unsafe route ${entry.route}`);
  check(entry.roles.length > 0 && entry.roles.every(code => code === '*' || ROLE_REFERENCE.some(role => role.code === code)), `${entry.route}: audience gap`);
  check(entry.responsibility.length >= 40, `${entry.route}: responsibility missing`);
  check(entry.prerequisites.length > 0 && entry.limitations.length > 0, `${entry.route}: prerequisites/limitations missing`);
  check(entry.procedures.length > 0, `${entry.route}: no how-to`);
  check(entry.sources.length > 0, `${entry.route}: no implementation reference`);
  for (const source of entry.sources) {
    check(!source.includes('..') && !path.isAbsolute(source) && fs.existsSync(path.join(repository, source)), `${entry.route}: broken source ${source}`);
  }
  for (const procedure of entry.procedures) {
    check(procedure.title.length > 8 && procedure.steps.length >= 3, `${entry.route}: generic/incomplete procedure`);
    check(procedure.steps.every(step => typeof step === 'string' && step.length > 25), `${entry.route}: empty step`);
    check(procedure.outcome.length > 25 && procedure.handoff.length > 15, `${entry.route}: missing acceptance/handoff`);
  }
}
for (const code of Object.keys(ROLE_GUIDANCE)) check(ROLE_REFERENCE.some(role => role.code === code), `Missing canonical role ${code}`);
for (const role of ROLE_REFERENCE) {
  check(!Object.hasOwn(role, 'assigned'), 'Reference roles must not claim assignment');
  check(role.steps.length >= 3 && ROLE_NOTES[role.code]?.length >= 2, `${role.code}: incomplete responsibility handbook`);
  for (const step of role.steps) {
    check(routes.has(step.route), `${role.code}: unknown handoff route ${step.route}`);
    for (const field of ['input','action','output','acceptance','nextOwner','exception']) check(typeof step[field] === 'string' && step[field].trim().length >= (field === 'nextOwner' ? 2 : 10), `${role.code}: missing ${field}`);
  }
}
for (const global of PLATFORM_GUIDES) check(global.steps.length >= 3 && global.outcome.length > 15, `Incomplete platform guide ${global.id}`);
for (const flow of HANDOFFS) for (const [owner,route,action] of flow.stages) check(owner && routes.has(route) && action.length > 20, `Invalid handoff ${flow.title}`);
check(GLOSSARY.length >= 15, 'Status glossary missing');
const context = { state: 'ready', accessReady: true, roleCodes: ['ENGINEERING'], modules: [{ route: 'timesheet' }] };
check(guideLinkAllowed('timesheet', context), 'Verified link should be offered');
for (const candidate of [null, {}, {...context, accessReady:false}, {...context,state:'verifying'}, {...context,state:'unavailable'}, {...context,modules:[]}]) check(!guideLinkAllowed('timesheet', candidate), 'Unverified link must stay closed');
for (const route of ['javascript:alert(1)', '//outside.invalid', '#timesheet', '../timesheet', 'timesheet?grant=admin']) check(!guideLinkAllowed(route, {...context,modules:[{route}]}), 'Unsafe route accepted');
check(!guideLinkAllowed('user-admin', {...context,roleCodes:['SUPER_ADMINISTRATOR']}), 'A role/reference label must never grant a link');
check(assignedGuideRoles(context).length === 1 && assignedGuideRoles(context)[0].code === 'ENGINEERING', 'Assignment must match verified role');
check(assignedGuideRoles({...context,roleCodes:['ENGINEER','PROJECT_MANAGER']}).length === 2, 'Role aliases missing');
check(assignedGuideRoles({...context,roleCodes:[]}).length === 0, 'Empty roles escalated');
check(assignedGuideRoles({...context,accessReady:false}).length === 0, 'Pending roles exposed');
check(assignedGuideRoles({...context,roleCodes:['CUSTOM_TEST_ROLE']})[0].unreviewed, 'Custom role must be explicit');
check(filterGuideEntries(catalog, {availableOnly:true,context}).every(entry => entry.route === 'timesheet'), 'Learning audience widened access');
check(filterGuideEntries(catalog, {query:'PTO transfer'}).some(entry => entry.route === 'sow-generator'), 'Procedure search missing');
check(filterGuideEntries(catalog, {query:'this-term-does-not-exist'}).length === 0, 'Empty search result incorrect');
check(filterGuideEntries(catalog, {role:'ENGINEERING'}).every(entry => entry.guide.roles.includes('*') || entry.guide.roles.includes('ENGINEERING')), 'Audience filter incorrect');
const extra = buildGuideCatalog(PROJECTPULSE_MODULES, [{route:'future-test-route',title:'Future'}, {route:'timesheet',title:'Wrong'}, {route:'https://outside.invalid'}]);
check(extra.find(entry => entry.route === 'future-test-route')?.guide === null, 'Unknown route silently counted as documented');
check(extra.find(entry => entry.route === 'timesheet').title === 'Timesheet', 'Legacy alias replaced canonical identity');
check(!extra.some(entry => entry.route.includes('https:')), 'Unsafe catalog route');
check(GUIDE_BY_ROUTE['time-reallocation'].roles.join(',') === 'PROJECT_TEAM_COORDINATOR,SUPER_ADMINISTRATOR', '001B creator boundary changed');
const creator = GUIDE_BY_ROUTE['create-work-register'];
check(!creator.roles.includes('PROJECT_MANAGEMENT'), '055D must not imply PM creation authority');
const guideSource = fs.readFileSync(path.join(web,'src/SystemUserGuide.jsx'),'utf8');
check(/  timesheet: \{[\s\S]*?\n  \},\n  'manager-approval': \{/.test(guideSource), 'Active generator foundation boundary missing');
for (const marker of ['GROUP_7_SYSTEM_GUIDE_LOGO','GROUP_7_SYSTEM_GUIDE_GOVERNANCE_START','<SystemUserGuideGovernancePanel />','<USSignalLogo size="large" />','.sort(compareProjectPulseModules);']) check(guideSource.includes(marker), `Guide integration marker missing: ${marker}`);
const report = {
  status:'passed', baseline:GUIDE_BASELINE, authoredModuleTopics:GUIDE_TOPICS.length,
  numberedWorkspaces:catalog.filter(entry=>entry.moduleNumber).length,
  moduleProcedures:GUIDE_TOPICS.reduce((sum,entry)=>sum+entry.procedures.length,0),
  roleHandbooks:ROLE_REFERENCE.length, roleSteps:ROLE_REFERENCE.reduce((sum,role)=>sum+role.steps.length,0),
  globalHowTos:PLATFORM_GUIDES.length, crossTeamFlows:HANDOFFS.length, glossaryTerms:GLOSSARY.length,
  assertions, sourceReferences:new Set(GUIDE_TOPICS.flatMap(entry=>entry.sources)).size,
  scope:'Authored guide coverage and source/permission-boundary contracts, not exhaustive live role or integration acceptance.'
};
const outputIndex=process.argv.indexOf('--output');
if(outputIndex>=0){fs.mkdirSync(path.dirname(process.argv[outputIndex+1]),{recursive:true});fs.writeFileSync(process.argv[outputIndex+1],JSON.stringify(report,null,2)+'\n');}
console.log(JSON.stringify(report,null,2));
