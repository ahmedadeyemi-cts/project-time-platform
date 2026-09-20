import { test } from 'node:test';
import assert from 'node:assert/strict';
globalThis.window = { __projectPulseFriendlyErrorPresentationInstalled: true };
const { isTechnicalErrorText } = await import('../src/frontend/project-time-web/src/api-error-presentation.js');

test('successful UAT HTTP statuses remain diagnostic text', () => {
  for (const text of ['HTTP 200 · 83 ms', 'HTTP 204', 'status: 200', '/api/version returned HTTP 200', 'HTTP 302']) assert.equal(isTechnicalErrorText(text), false, text);
});
test('actual failures retain friendly-error handling', () => {
  for (const text of ['HTTP 403 · 83 ms', '/api/version returned HTTP 500', 'permission denied', '/api/version could not be verified']) assert.equal(isTechnicalErrorText(text), true, text);
});
