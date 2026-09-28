import assert from 'node:assert/strict';
import { safeCsvCell, isInternalNavigation } from '../src/frontend/project-time-web/src/security-output.mjs';

for (const value of ['=1+1', ' +SUM(A1:A2)', '\t@SUM(A1)', "-cmd|' /C test'!A0", '\uFEFF=2+2'])
  assert.ok(safeCsvCell(value).replace(/^"/, '').startsWith("'"));
for (const value of ['-12.50', '+12', '-1.5e2', 'ordinary']) assert.equal(safeCsvCell(value), value);
assert.equal(safeCsvCell('a,"b"\r\nc'), '"a,""b""\r\nc"');
for (const target of ['javascript:alert(1)', 'data:text/html,bad', 'https://other.invalid', '//other.invalid', '/\\other.invalid', '\n/projects', ''])
  assert.equal(isInternalNavigation(target), false);
for (const target of ['#module066', '/projects/123', '/projects?tab=billing']) assert.equal(isInternalNavigation(target), true);
console.log('SECURITY_OUTPUT_TESTS=PASS');
