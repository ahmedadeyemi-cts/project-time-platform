import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {
  EXTERNAL_DOM_RECOVERY_WINDOW_MS,
  claimExternalDomMutationRecovery,
  isRecoverableExternalDomMutationError,
  protectReactOwnedRoot,
  publishExternalDomMutationRecovery
} from '../src/external-dom-mutation-resilience.js';

const frontendRoot = path.resolve(import.meta.dirname, '..');
const indexSource = fs.readFileSync(path.join(frontendRoot, 'index.html'), 'utf8');
const mainSource = fs.readFileSync(path.join(frontendRoot, 'src/main.jsx'), 'utf8');
const boundarySource = fs.readFileSync(path.join(frontendRoot, 'src/ApplicationErrorBoundary.jsx'), 'utf8');
const resilienceSource = fs.readFileSync(path.join(frontendRoot, 'src/external-dom-mutation-resilience.js'), 'utf8');

assert.match(indexSource, /<html[^>]+data-gramm="false"[^>]+data-gramm_editor="false"[^>]+data-enable-grammarly="false"/);
assert.match(indexSource, /<body[^>]+data-gramm="false"[^>]+data-gramm_editor="false"[^>]+data-enable-grammarly="false"/);
assert.ok(indexSource.includes('<div id="root"></div>'));
assert.ok(mainSource.includes("import { protectReactOwnedRoot } from './external-dom-mutation-resilience.js';"));
const imports = mainSource.slice(0, mainSource.indexOf('createRoot(')).trim();
assert.ok(imports.endsWith("import './enterprise-contrast-guard.css';"));
assert.ok(mainSource.includes("createRoot(\n  protectReactOwnedRoot(document.getElementById('root'))\n).render("));
assert.ok(boundarySource.includes('isRecoverableExternalDomMutationError(error)'));
assert.ok(boundarySource.includes('claimExternalDomMutationRecovery()'));
assert.ok(boundarySource.includes('publishExternalDomMutationRecovery(error, info)'));
assert.ok(boundarySource.includes('recoveryEpoch: current.recoveryEpoch + 1'));
assert.ok(boundarySource.includes('Try workspace again'));
assert.ok(!resilienceSource.includes('Node.prototype.removeChild'));
assert.ok(!resilienceSource.includes('Node.prototype.insertBefore'));

function fakeElement() {
  const attributes = new Map();
  return {
    attributes,
    setAttribute(name, value) {
      attributes.set(name, String(value));
    },
    getAttribute(name) {
      return attributes.get(name) ?? null;
    }
  };
}

function memoryStorage() {
  const values = new Map();
  return {
    getItem(key) {
      return values.has(key) ? values.get(key) : null;
    },
    setItem(key, value) {
      values.set(key, String(value));
    }
  };
}

const documentElement = fakeElement();
const body = fakeElement();
const root = fakeElement();
assert.equal(protectReactOwnedRoot(root, { documentElement, body }), root);
assert.throws(
  () => protectReactOwnedRoot(null, { documentElement, body }),
  /Pulse root mount is unavailable\./
);

for (const node of [documentElement, body, root]) {
  assert.equal(node.getAttribute('data-gramm'), 'false');
  assert.equal(node.getAttribute('data-gramm_editor'), 'false');
  assert.equal(node.getAttribute('data-enable-grammarly'), 'false');
}
assert.equal(root.getAttribute('data-projectpulse-react-owned-root'), 'true');

const removeChildError = Object.assign(
  new Error('Node.removeChild: The node to be removed is not a child of this node'),
  { name: 'NotFoundError' }
);
const insertBeforeError = Object.assign(
  new Error("Failed to execute 'insertBefore' on 'Node': The node before which the new node is to be inserted is not a child of this node"),
  { name: 'NotFoundError' }
);
const ordinaryApplicationError = new TypeError('Cannot read properties of undefined');

assert.equal(isRecoverableExternalDomMutationError(removeChildError), true);
assert.equal(isRecoverableExternalDomMutationError(insertBeforeError), true);
assert.equal(isRecoverableExternalDomMutationError(ordinaryApplicationError), false);

const storage = memoryStorage();
assert.equal(claimExternalDomMutationRecovery({ storage, routeKey: '/#dashboard', now: 1_000 }), true);
assert.equal(claimExternalDomMutationRecovery({ storage, routeKey: '/#dashboard', now: 1_001 }), false);
assert.equal(claimExternalDomMutationRecovery({ storage, routeKey: '/#timesheet', now: 1_002 }), true);
assert.equal(claimExternalDomMutationRecovery({
  storage,
  routeKey: '/#timesheet',
  now: 1_002 + EXTERNAL_DOM_RECOVERY_WINDOW_MS + 1
}), true);

const dispatchedEvents = [];
class FakeCustomEvent {
  constructor(type, init) {
    this.type = type;
    this.detail = init?.detail;
  }
}
const fakeWindow = {
  location: { pathname: '/', search: '', hash: '#dashboard' },
  CustomEvent: FakeCustomEvent,
  dispatchEvent(event) {
    dispatchedEvents.push(event);
    return true;
  }
};
const detail = publishExternalDomMutationRecovery(
  removeChildError,
  { componentStack: '\nsmall\narticle\nDashboard' },
  fakeWindow
);
assert.equal(detail.fingerprint, 'PP-DOM-OWNERSHIP');
assert.equal(detail.route, '/#dashboard');
assert.equal(dispatchedEvents.length, 1);
assert.equal(dispatchedEvents[0].type, 'projectpulse:ui-recovery');

console.log('EXTERNAL_DOM_MUTATION_SOURCE_WIRING=PASS');
console.log('EXTERNAL_DOM_MUTATION_ERROR_CLASSIFICATION=PASS');
console.log('WRITING_ASSISTANT_OPT_OUT=PASS');
console.log('ROOT_MOUNT_CONTRACT=PASS');
console.log('CONTRAST_GUARD_IMPORT_ORDER=PASS');
console.log('ROUTE_SCOPED_RECOVERY_RATE_LIMIT=PASS');
console.log('UI_RECOVERY_TELEMETRY=PASS');
console.log('EXTERNAL_DOM_MUTATION_RESILIENCE=PASS');
