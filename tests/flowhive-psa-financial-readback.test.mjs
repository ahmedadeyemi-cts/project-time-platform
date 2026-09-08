import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

const center = fs.readFileSync(new URL('../src/frontend/project-time-web/src/ProjectForgeCenter.jsx', import.meta.url), 'utf8');
const api = fs.readFileSync(new URL('../src/frontend/project-time-web/src/project-forge/projectForgeApi.js', import.meta.url), 'utf8');

test('Project Forge financial view reads the authorized canonical endpoint and reloads it with the project', () => {
  assert.match(api, /financialReadback\(projectId, options = \{\}\)/);
  assert.match(api, /\/api\/project-forge\/projects\/\$\{encodeURIComponent\(projectId\)\}\/financial-readback/);
  assert.match(center, /result\.access\?\.canViewFinancials && !result\.access\?\.isViewAs/);
  assert.match(center, /financialReadback = await projectForgeApi\.financialReadback\(nextProject/);
  assert.match(center, /onClick=\{\(\) => load\(\{ pm: selectedPm, project: currentProjectId/);
});

test('financial view labels distinct measures and preserves unknown source values', () => {
  for (const label of [
    'Original estimate',
    'Approved estimate',
    'Logged hours',
    'Approved hours',
    'Budget hours remaining',
    'Current estimate to complete',
    'Budget remaining after known actual costs',
    'Budget remaining after actual costs',
    'Forecast variance',
    'Incomplete source evidence',
    'Derived assumptions'
  ]) assert.match(center, new RegExp(label));
  assert.match(center, /function nullableHours\(value\)/);
  assert.match(center, /function nullableMoney\(value, currency\)/);
  assert.match(center, /value == null \? 'Unknown'/);
  assert.match(center, /Billing or unclassified task rates remain unknown/);
  assert.match(center, /Expenses and commitments are not added to derived labor forecasts or counted twice/);
});
