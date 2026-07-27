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
  'session readiness and native recovery are captured before App and later fetch bridges'
);

check(
  'TOKEN_COMPATIBILITY',
  authoritative.includes('session?.sessionToken')
    && authoritative.includes('session?.token')
    && authoritative.includes('session?.accessToken')
    && authoritative.includes('session?.session_token'),
  'all known browser session token shapes remain supported'
);

check(
  'EXPIRATION_AWARE',
  authoritative.includes('function sessionIsExpired(session)')
    && authoritative.includes('Date.parse(session.expiresAt)')
    && authoritative.includes('token && !sessionIsExpired(session)'),
  'expired stored sessions are not used for protected requests'
);

check(
  'PUBLIC_AUTH_EXEMPTIONS',
  ['/health', '/api/auth/', '/api/public/', '/api/bootstrap/', '/api/app-config', '/api/config']
    .every((value) => authoritative.includes(`'${value}'`)),
  'login, bootstrap, public, health, and configuration routes bypass the readiness gate'
);

check(
  'FETCH_READINESS_GATE',
  authoritative.includes('function installProtectedFetchReadinessGate()')
    && authoritative.includes('let token = requestSessionToken(input, init);')
    && authoritative.includes('if (!token) token = (await waitForUsableSession()).token;')
    && authoritative.includes('if (!token) return createSessionNotReadyResponse(path);')
    && authoritative.includes('applySessionHeaders('),
  'protected fetch calls wait for a usable session and never leave the browser without one'
);

check(
  'BOUNDED_SESSION_WAIT',
  authoritative.includes('const SESSION_WAIT_MS = 1200;')
    && authoritative.includes("window.addEventListener('projectpulse:auth-session-ready', handleSignal)")
    && authoritative.includes('window.setTimeout(finish, Math.max(0, Number(timeoutMs || 0)))'),
  'pre-login requests wait briefly for the successful login event without polling indefinitely'
);

check(
  'NO_AUTOMATIC_SESSION_DELETION',
  !authoritative.includes('clearSessionStorage')
    && !authoritative.includes('invalidateProjectPulseSession')
    && !authoritative.includes('projectpulse:session-invalidated')
    && !authoritative.includes('projectpulse-authoritative-session-invalidation-v1')
    && !authoritative.includes('window.location.reload()')
    && !authoritative.includes("window.location.hash = '#dashboard'"),
  'the transport cannot clear browser sessions, reload the app, or change routes'
);

check(
  'GLOBAL_XHR_BRIDGE_PRESENT',
  app.includes('function installProjectPulse050BFinalXhrBridge()')
    && app.includes('xhrPrototype.__projectPulse050BFinalWrapped = true;')
    && app.includes("this.setRequestHeader('X-ProjectPulse-Session', token)")
    && app.includes("this.setRequestHeader('X-Project-Pulse-Session', token)")
    && app.includes("this.setRequestHeader('X-Session-Token', token)")
    && app.includes('this.setRequestHeader(\'Authorization\', `Bearer ${token}`)'),
  'App retains the single global XHR session-header bridge'
);

check(
  'BRIDGE_TOKEN_CONTRACT',
  authoritative.includes('function globalXhrBridgeToken()')
    && authoritative.includes("parseStoredJson(window.localStorage, 'projectPulseAuthSession')")
    && authoritative.includes('Do not broaden it without broadening that bridge'),
  'authoritative transport mirrors the exact storage/token contract the App bridge can supply'
);

check(
  'NO_DUPLICATE_XHR_HEADERS',
  authoritative.includes('function globalXhrBridgeCanSupplyToken(token)')
    && authoritative.includes('globalXhrBridgeToken() === token')
    && authoritative.includes('if (token && !globalXhrBridgeCanSupplyToken(token))')
    && authoritative.includes('Defer only when App.jsx\'s global XHR bridge can supply this exact token.')
    && !authoritative.includes("if (token) {\n      request.setRequestHeader('Authorization'"),
  'authoritative XHR defers only when the global bridge will inject the same exact token'
);

check(
  'LEGACY_DIRECT_HEADER_FALLBACK',
  authoritative.includes('Legacy/session-storage/session_token sessions use the direct fallback')
    && authoritative.includes("request.setRequestHeader('Authorization', `Bearer ${token}`)")
    && authoritative.includes("request.setRequestHeader('X-ProjectPulse-Session', token)")
    && authoritative.includes("request.setRequestHeader('X-Project-Pulse-Session', token)")
    && authoritative.includes("request.setRequestHeader('X-Session-Token', token)"),
  'legacy and session-storage token forms retain a direct-header path when the bridge has no token'
);

check(
  'CONFLICT_FAILS_LOCAL',
  authoritative.includes('function sessionTransportConflictError(path)')
    && authoritative.includes("error.code = 'session_transport_conflict';")
    && authoritative.includes('bridgeToken !== token')
    && authoritative.includes('throw sessionTransportConflictError(path);'),
  'a different stale bridge token stops locally rather than creating an appended invalid header'
);

check(
  'CAPTURED_NATIVE_TRANSPORT',
  authoritative.includes('const CAPTURED_NATIVE_FETCH')
    && authoritative.includes('window.fetch.bind(window)')
    && authoritative.indexOf('const CAPTURED_NATIVE_FETCH') < authoritative.indexOf('function installProtectedFetchReadinessGate()')
    && authoritative.includes("const NATIVE_FALLBACK_MARKER = 'projectpulse-authoritative-native-fetch-fallback-v1'"),
  'one clean native fetch reference is captured before all later global wrappers'
);

check(
  'SHAPE_AWARE_NORMALIZATION',
  authoritative.includes('function normalizePayload(payload, requiredCollections = [])')
    && authoritative.includes('function payloadCandidates(payload, requiredCollections)')
    && authoritative.includes('normalizeCollectionKeys(candidate, required)')
    && authoritative.includes('required.every((name) => Array.isArray(normalized[name]))')
    && authoritative.includes('if (requiredCollections.length === 1)'),
  'root, envelope, case-insensitive, and direct-array payload shapes are normalized without discarding valid collections'
);

check(
  'ENVELOPE_UNWRAP_COMPATIBILITY',
  authoritative.includes('const hasNonEnvelopeKey = rootKeys.some((key) => !ENVELOPE_KEYS.includes(key));')
    && authoritative.includes('const nestedCandidate = candidates')
    && authoritative.includes('.slice(1)')
    && authoritative.includes('return nestedCandidate || payload;'),
  'envelope-only responses without required collections unwrap to the populated inner object'
);

check(
  'EMPTY_XHR_NATIVE_RECOVERY',
  authoritative.includes('async function nativeFetchAuthoritative(path, options)')
    && authoritative.includes("recoveredFrom: 'xhr-success-missing-collections'")
    && authoritative.includes("finishSuccess(fallback.payload, fallback.status, 'native-fetch-fallback'")
    && authoritative.includes("method === 'GET' && options.nativeFallback !== false")
    && authoritative.includes('const fallbackMissing = collectionMissing(fallback.payload, requiredCollections);'),
  'HTTP 200 XHR responses missing required collections are retried once through the captured native transport'
);

check(
  'NO_COLLECTION_FABRICATION',
  !authoritative.includes('requiredCollections.map((name) => [name, []])')
    && !authoritative.includes('new Array(requiredCollections')
    && !authoritative.includes('missingCollections.forEach((name) => payload[name] = [])'),
  'the recovery transport never fabricates required collections to silence errors'
);

check(
  'FALLBACK_DIAGNOSTICS',
  authoritative.includes('xhrRawResponseType')
    && authoritative.includes('xhrRawResponseKeys')
    && authoritative.includes('fallbackRawResponseType')
    && authoritative.includes('fallbackRawResponseKeys')
    && authoritative.includes("transport: 'native-fetch-fallback'"),
  'recovered and failed response shapes remain visible in runtime diagnostics'
);

check(
  'SILENT_SESSION_NOT_READY',
  authoritative.includes("error.code = 'session_not_ready';")
    && authoritative.includes('error.silent = true;')
    && authoritative.includes('throw sessionNotReadyError(path);'),
  'session-not-ready requests stop locally without authoritative console errors'
);

check(
  'ERROR_DEDUPLICATION',
  authoritative.includes('function shouldPublishError(diagnostic)')
    && authoritative.includes('now - previous.at >= 15000')
    && authoritative.includes('if (!diagnostic.ok && shouldPublishError(diagnostic))'),
  'real API errors remain visible but repeated identical console messages are throttled'
);

check(
  'AUTHORITATIVE_CONTRACT',
  authoritative.includes('projectpulse-authoritative-xhr-v1')
    && authoritative.includes('requiredCollections')
    && authoritative.includes('collectionMissing')
    && authoritative.includes("request.setRequestHeader('X-ProjectPulse-Authoritative-Client', DIAGNOSTIC_MARKER)"),
  'existing authoritative response validation and diagnostics remain intact'
);

check(
  'BUILD_GUARD',
  packageJson.scripts?.build?.includes('validate:global-session-invalidation')
    && packageJson.scripts?.['validate:global-session-invalidation']?.includes('validate-global-session-invalidation.mjs'),
  'future production builds must pass the safe session-transport contract'
);

const failures = checks.filter((item) => !item.condition).map((item) => item.name);
console.log(`GLOBAL_SESSION_VALIDATION_CHECKS=${checks.length}`);
if (failures.length) {
  console.error(`GLOBAL_SESSION_FAILED_CHECKS=${failures.join(',')}`);
  console.error('GLOBAL_SESSION_TRANSPORT_CONTRACT=FAILED');
  process.exit(1);
}

console.log('GLOBAL_SESSION_TRANSPORT_CONTRACT=PASSED');
