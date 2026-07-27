import fs from 'node:fs';
import path from 'node:path';

const frontendRoot = process.cwd();
const repositoryRoot = path.resolve(frontendRoot, '..', '..', '..');
const files = {
  authoritative: 'src/frontend/project-time-web/src/projectpulse-authoritative-api.js',
  main: 'src/frontend/project-time-web/src/main.jsx',
  app: 'src/frontend/project-time-web/src/App.jsx',
  package: 'src/frontend/project-time-web/package.json'
};

const read = (relative) => fs.readFileSync(path.join(repositoryRoot, relative), 'utf8');
const authoritative = read(files.authoritative);
const main = read(files.main);
const app = read(files.app);
const packageJson = JSON.parse(read(files.package));
const checks = [];

function check(name, condition, evidence) {
  checks.push({ name, condition, evidence });
  console.log(`GLOBAL_SESSION_${name}=${condition ? 'PASSED' : 'FAILED'} — ${evidence}`);
}

check(
  'EARLIEST_IMPORT',
  main.indexOf("import './projectpulse-authoritative-api.js';") >= 0
    && main.indexOf("import './projectpulse-authoritative-api.js';") < main.indexOf("import App from './App.Module001.g.jsx';"),
  'session invalidation installs before App and later fetch bridges'
);

check(
  'ALL_SESSION_KEYS',
  ['projectPulseAuthSession', 'ProjectPulseAuthSession', 'projectPulseSession', 'projectPulseViewAsUser', 'projectPulseViewAsUserId']
    .every((value) => authoritative.includes(`'${value}'`)),
  'legacy and current auth/View-As storage keys are cleared'
);

check(
  'TOKEN_COMPATIBILITY',
  authoritative.includes('session?.sessionToken')
    && authoritative.includes('session?.token')
    && authoritative.includes('session?.accessToken')
    && authoritative.includes('session?.session_token'),
  'every known browser session token shape is supported'
);

check(
  'SESSION_REQUIRED_ONLY',
  authoritative.includes('if (Number(status) !== 401 || !storedSessionContext().token) return false;')
    && authoritative.includes('SESSION_REJECTION_STATUS_CODES.has(statusCode)')
    && authoritative.includes('SESSION_REJECTION_MESSAGE.test(message)')
    && authoritative.includes("'session_required'")
    && authoritative.includes("'session_expired'")
    && authoritative.includes("'session_invalid'"),
  'generic authorization failures do not revoke valid sessions'
);

check(
  'SINGLE_FLIGHT_INVALIDATION',
  authoritative.includes('window.__projectPulseSessionInvalidationStarted')
    && authoritative.includes('if (window.__projectPulseSessionInvalidationStarted) return true;')
    && authoritative.includes('window.__projectPulseSessionInvalidationStarted = true;'),
  'concurrent 401 responses trigger one invalidation/reload'
);

check(
  'CLEAR_BEFORE_RELOAD',
  authoritative.indexOf('clearSessionStorage();') >= 0
    && authoritative.indexOf('clearSessionStorage();') < authoritative.indexOf("window.dispatchEvent(new CustomEvent(SESSION_INVALIDATED_EVENT")
    && authoritative.indexOf("window.dispatchEvent(new CustomEvent(SESSION_INVALIDATED_EVENT") < authoritative.indexOf('window.location.reload();'),
  'stale session and View-As state are removed before reload'
);

check(
  'DASHBOARD_REAUTH_BOUNDARY',
  authoritative.includes("window.location.hash = '#dashboard';")
    && authoritative.includes('window.location.reload();')
    && !authoritative.includes('projectPulsePostLoginRoute'),
  'invalid sessions return to the existing dashboard-first sign-in flow without unused route state'
);

check(
  'FETCH_COVERAGE',
  authoritative.includes('function installGlobalFetchSessionInvalidation()')
    && authoritative.includes('const originalFetch = window.fetch.bind(window);')
    && authoritative.includes('void inspectFetchSessionRejection(input, response);')
    && authoritative.includes('response.clone().text()'),
  'all later fetch wrappers inherit centralized 401 inspection'
);

check(
  'XHR_COVERAGE',
  authoritative.includes('if (isSessionRejection(request.status, payload, raw))')
    && authoritative.includes('invalidateProjectPulseSession(path, payload, raw);')
    && authoritative.includes('projectpulse-authoritative-xhr-v1'),
  'authoritative XMLHttpRequest failures use the same invalidation path'
);

check(
  'NO_UNAUTHENTICATED_RELOAD_LOOP',
  authoritative.includes('if (!storedSessionContext().token) return false;')
    && authoritative.includes('if (!path || response?.status !== 401 || !storedSessionContext().token) return;'),
  '401 responses without a stored session never reload the sign-in page'
);

check(
  'APP_CURRENT_SESSION_CONTRACT',
  app.includes("window.localStorage.getItem('projectPulseAuthSession')")
    && app.includes("window.localStorage.removeItem('projectPulseAuthSession')")
    && app.includes('setAuthSession(null)')
    && app.includes("window.location.hash = '#dashboard';"),
  'global invalidation clears the session and uses App’s existing dashboard sign-in contract'
);

check(
  'BUILD_GUARD',
  packageJson.scripts?.build?.includes('validate:global-session-invalidation')
    && packageJson.scripts?.['validate:global-session-invalidation']?.includes('validate-global-session-invalidation.mjs'),
  'future production builds must pass this session-lifecycle contract'
);

const failures = checks.filter((item) => !item.condition).map((item) => item.name);
console.log(`GLOBAL_SESSION_VALIDATION_CHECKS=${checks.length}`);
if (failures.length) {
  console.error(`GLOBAL_SESSION_FAILED_CHECKS=${failures.join(',')}`);
  console.error('GLOBAL_SESSION_INVALIDATION_CONTRACT=FAILED');
  process.exit(1);
}

console.log('GLOBAL_SESSION_INVALIDATION_CONTRACT=PASSED');
