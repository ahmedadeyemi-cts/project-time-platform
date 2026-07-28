import assert from 'node:assert/strict';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

class MemoryStorage {
  constructor(initial = {}) {
    this.values = new Map(Object.entries(initial));
  }

  getItem(key) {
    return this.values.has(key) ? this.values.get(key) : null;
  }

  setItem(key, value) {
    this.values.set(key, String(value));
  }

  removeItem(key) {
    this.values.delete(key);
  }
}

const listeners = new Map();
const calls = [];
const sessionToken = 'runtime-test-session-token';
const localStorage = new MemoryStorage({
  projectPulseAuthSession: JSON.stringify({
    sessionToken,
    expiresAt: new Date(Date.now() + 60_000).toISOString()
  })
});

class TestCustomEvent {
  constructor(type, options = {}) {
    this.type = type;
    this.detail = options.detail;
  }
}

globalThis.CustomEvent = TestCustomEvent;
globalThis.window = {
  location: {
    origin: 'https://phd-west-test.onenecklab.com',
    hash: '#role-admin'
  },
  localStorage,
  sessionStorage: new MemoryStorage(),
  setTimeout,
  clearTimeout,
  requestAnimationFrame: (callback) => setTimeout(callback, 0),
  addEventListener(type, callback) {
    if (!listeners.has(type)) listeners.set(type, new Set());
    listeners.get(type).add(callback);
  },
  removeEventListener(type, callback) {
    listeners.get(type)?.delete(callback);
  },
  dispatchEvent(event) {
    for (const callback of listeners.get(event.type) || []) callback(event);
  }
};

function jsonResponse(payload, status = 200) {
  return new Response(JSON.stringify(payload), {
    status,
    headers: { 'Content-Type': 'application/json' }
  });
}

window.fetch = async (input, init = {}) => {
  const url = new URL(typeof input === 'string' ? input : input.url, window.location.origin);
  const headers = new Headers(init.headers || (input instanceof Request ? input.headers : undefined));
  calls.push({ path: url.pathname, headers });

  assert.equal(headers.get('X-ProjectPulse-Session'), sessionToken, `${url.pathname} must retain the ProjectPulse session`);
  assert.equal(headers.get('Authorization'), `Bearer ${sessionToken}`, `${url.pathname} must retain the bearer session`);

  switch (url.pathname) {
    case '/api/role-policy/summary':
      return jsonResponse({ data: { Roles: [{ roleCode: 'SUPER_ADMINISTRATOR' }], Modules: [{ moduleCode: '012' }] } });
    case '/api/runtime/v2/role-policy/summary':
      return jsonResponse({});
    case '/api/role-policy/catalog':
      return jsonResponse({});
    case '/api/runtime/v2/role-policy/catalog':
      return jsonResponse({ result: { actions: [{ actionCode: 'MODULE_ACCESS' }], scopes: [{ scopeCode: 'ORGANIZATION' }] } });
    case '/api/role-policy/matrix':
      return jsonResponse({ roles: [{ roleCode: 'SUPER_ADMINISTRATOR' }], modules: [{ moduleCode: '037' }], grants: [{ roleCode: 'SUPER_ADMINISTRATOR', moduleCode: '037' }] });
    case '/api/runtime/v2/role-policy/matrix':
      return jsonResponse({});
    case '/api/role-policy/versions':
    case '/api/runtime/v2/role-policy/versions':
      return jsonResponse({});
    default:
      return jsonResponse({ status: 'unexpected_path', path: url.pathname }, 404);
  }
};

const frontendRoot = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
await import(`${pathToFileURL(path.join(frontendRoot, 'src/runtime-data-compatibility.js')).href}?runtime-test=${Date.now()}`);

calls.length = 0;
let response = await window.fetch('/api/role-policy/summary');
assert.equal(response.status, 200);
let payload = await response.json();
assert.equal(payload.roles.length, 1);
assert.equal(payload.modules.length, 1);
assert.deepEqual(calls.map((call) => call.path), ['/api/role-policy/summary']);
assert.equal(calls[0].headers.get('X-ProjectPulse-Module-Number'), '012');

calls.length = 0;
response = await window.fetch('/api/runtime/v2/role-policy/summary');
payload = await response.json();
assert.equal(response.status, 200);
assert.equal(payload.roles.length, 1);
assert.deepEqual(calls.map((call) => call.path), ['/api/role-policy/summary']);

calls.length = 0;
response = await window.fetch('/api/role-policy/catalog');
payload = await response.json();
assert.equal(response.status, 200);
assert.equal(payload.actions.length, 1);
assert.equal(payload.scopes.length, 1);
assert.deepEqual(calls.map((call) => call.path), [
  '/api/role-policy/catalog',
  '/api/runtime/v2/role-policy/catalog'
]);
assert.equal(calls[1].headers.get('X-ProjectPulse-Role-Policy-Client'), 'projectpulse-role-policy-direct-fetch-v3');

window.location.hash = '#roles-permissions-matrix';
calls.length = 0;
response = await window.fetch('/api/runtime/v2/role-policy/matrix');
payload = await response.json();
assert.equal(response.status, 200);
assert.equal(payload.grants.length, 1);
assert.equal(calls[0].headers.get('X-ProjectPulse-Module-Number'), '037');

window.location.hash = '#role-admin';
calls.length = 0;
response = await window.fetch('/api/role-policy/versions');
payload = await response.json();
assert.equal(response.status, 502);
assert.equal(payload.status, 'role_policy_contract_mismatch');
assert.deepEqual(payload.requiredCollections, ['versions']);
assert.equal(payload.attempts.length, 2);
assert.deepEqual(calls.map((call) => call.path), [
  '/api/role-policy/versions',
  '/api/runtime/v2/role-policy/versions'
]);

console.log('ADMIN_RUNTIME_STABILITY_EXECUTABLE_ROLE_POLICY_TEST=PASS');
