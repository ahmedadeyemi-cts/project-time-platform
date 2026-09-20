import test from 'node:test';
import assert from 'node:assert/strict';
import { loadFinancialPortfolio } from '../src/frontend/project-time-web/src/project-financial-portfolio.js';
test('includes projects and AEs beyond the first server page without duplicating rows', async () => {
  const calls = [];
  const result = await loadFinancialPortfolio(async path => {
    calls.push(path);
    return calls.length === 1 ? { projects: [{ projectId: 'a' }], summary: { totalProjects: 2 }, nextOffset: 1 }
      : { projects: [{ projectId: 'a' }, { projectId: 'b', accountExecutive: { userId: 'second-ae' } }], nextOffset: null };
  }, { workspace: 'sales', limit: 250 });
  assert.equal(result.projects.length, 2);
  assert.equal(result.projects[1].accountExecutive.userId, 'second-ae');
  assert.match(calls[1], /offset=1/);
  assert.deepEqual(result.summary, { totalProjects: 2 });
});
test('identity change discards all old pages and stops fetching', async () => {
  let current = true;
  const result = await loadFinancialPortfolio(async () => {
    current = false;
    return { projects: [{ projectId: 'old-user-project' }], nextOffset: 1 };
  }, { workspace: 'sales' }, () => current);
  assert.equal(result, null);
});
test('a broken pagination response fails rather than looping forever', async () => {
  await assert.rejects(loadFinancialPortfolio(async () => ({ projects: [{ projectId: 'a' }], nextOffset: 0 }), {}), /did not advance/);
});
test('failure after a successful page does not show a partial portfolio as complete', async () => {
  let calls = 0;
  await assert.rejects(loadFinancialPortfolio(async () => {
    if (++calls === 2) throw new Error('Offline');
    return { projects: [{ projectId: 'a' }], nextOffset: 1 };
  }, {}), /Offline/);
});
