import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const frontendRoot = path.resolve(scriptDirectory, '..');
const sourcePath = path.join(frontendRoot, 'src', 'ProjectFlowHiveCenter.jsx');

const malformed = '</small></strong><button type="button" disabled={!enterprise?.access?.canManage} onClick={() => addTask(task.wbsNumber)}>Add task</button>';
const repaired = '</small></span><button type="button" disabled={!enterprise?.access?.canManage} onClick={() => addTask(task.wbsNumber)}>Add task</button>';

let source = fs.readFileSync(sourcePath, 'utf8');
const malformedCount = source.split(malformed).length - 1;
const repairedCount = source.split(repaired).length - 1;

if (malformedCount > 1) {
  throw new Error(`Module 066 generated source contains ${malformedCount} malformed phase-action tag sequences; expected at most one.`);
}

if (malformedCount === 1) {
  source = source.replace(malformed, repaired);
  fs.writeFileSync(sourcePath, source, 'utf8');
}

const finalSource = fs.readFileSync(sourcePath, 'utf8');
const finalRepairedCount = finalSource.split(repaired).length - 1;
if (finalRepairedCount !== 1) {
  throw new Error(`Module 066 phase-action control is missing or duplicated after repair; found ${finalRepairedCount}, expected one.`);
}
if (finalSource.includes(malformed)) {
  throw new Error('Module 066 generated source still contains the mismatched closing tag.');
}

console.log(`MODULE_066_GENERATED_JSX_REPAIR=${malformedCount === 1 ? 'REPAIRED' : 'ALREADY_VALID'}`);
