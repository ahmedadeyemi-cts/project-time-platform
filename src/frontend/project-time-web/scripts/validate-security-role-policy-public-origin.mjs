import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '..');
const repositoryRoot = path.resolve(root, '..', '..', '..');

const files = {
  main: path.join(root, 'src/main.jsx'),
  effectiveIdentity: path.join(root, 'src/role-workspace-effective-identity-compatibility.js'),
  transport: path.join(root, 'src/role-policy-authoritative-transport.js'),
  authoritative: path.join(root, 'src/projectpulse-authoritative-api.js'),
  globalMail: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs'),
  publicOrigin: path.join(repositoryRoot, 'src/backend/ProjectTime.Api/Modules/ProjectPulsePublicOriginCompatibility.cs'),
  nginx: path.join(repositoryRoot, 'deployment/containers/web/default.conf.template')
};

const read = (file) => fs.readFileSync(file, 'utf8');
const requireText = (source, expected, label) => {
  if (!source.includes(expected)) throw new Error(`${label} is missing: ${expected}`);
};
const rejectText = (source, forbidden, label) => {
  if (source.includes(forbidden)) throw new Error(`${label} contains forbidden text: ${forbidden}`);
};

for (const [label, file] of Object.entries(files)) {
  if (!fs.existsSync(file)) throw new Error(`${label} file is missing: ${file}`);
}

const main = read(files.main);
const effectiveIdentity = read(files.effectiveIdentity);
const transport = read(files.transport);
const authoritative = read(files.authoritative);
const globalMail = read(files.globalMail);
const publicOrigin = read(files.publicOrigin);
const nginx = read(files.nginx);

requireText(
  main,
  "import './runtime-data-compatibility.js';\nimport './role-workspace-effective-identity-compatibility.js';\nimport './role-policy-authoritative-transport.js';",
  'main import order'
);
requireText(effectiveIdentity, '/api/utilization/current-quarter', 'effective identity isolation route');
requireText(effectiveIdentity, 'not_applicable_for_effective_role', 'effective identity isolation response');
requireText(effectiveIdentity, 'grantsUtilizationAccess: false', 'effective identity least privilege');
requireText(effectiveIdentity, 'viewAsMutationAuthority: false', 'effective identity View-As boundary');
rejectText(effectiveIdentity, "status: 403", 'effective identity compatibility must not forge a denial');

requireText(transport, "import { authoritativeApi } from './projectpulse-authoritative-api.js';", 'role-policy transport');
requireText(transport, "requiredCollections: ['actions', 'scopes']", 'Module 012 catalog contract');
requireText(transport, "requiredCollections: ['roles', 'modules', 'grants']", 'Module 037 matrix contract');
requireText(transport, '/api/runtime/v2/role-policy/catalog', 'Module 012 v2 catalog route');
requireText(transport, '/api/runtime/v2/role-policy/matrix', 'Module 037 v2 matrix route');
requireText(transport, "if (requestMethod(input, init) !== 'GET')", 'read-only transport boundary');
requireText(transport, 'X-ProjectPulse-Role-Policy-Transport', 'transport evidence header');
requireText(transport, 'role_policy_authoritative_transport_failed', 'sanitized transport failure');
rejectText(transport, "method: 'POST'", 'role-policy transport');
rejectText(transport, "method: 'PUT'", 'role-policy transport');
requireText(authoritative, 'CAPTURED_NATIVE_FETCH', 'authoritative native fallback');
requireText(authoritative, 'xhr-success-missing-collections', 'authoritative collection recovery');

requireText(globalMail, 'app.UseProjectPulsePublicOriginCompatibility();', 'Module 065 public-origin registration');
requireText(globalMail, 'trusted_public_origin_unavailable', 'Module 065 fail-closed response');
requireText(globalMail, 'TryResolveProxyOrConfiguredOrigin', 'Module 065 trusted origin resolver');
requireText(globalMail, 'TryBrowserOrigin', 'Module 065 browser-origin compatibility');
rejectText(globalMail, 'invalid_forwarded_public_origin', 'obsolete forwarded-origin hard failure');

requireText(publicOrigin, 'trusted_forwarded_origin', 'trusted forwarded origin source');
requireText(publicOrigin, 'Public ProjectPulse environments are HTTPS-only', 'TLS termination compatibility');
requireText(publicOrigin, '.onenecklab.com', 'test-domain allowlist');
requireText(publicOrigin, '.ussignal.com', 'production-domain allowlist');
requireText(publicOrigin, 'PROJECTPULSE_INTEGRATION_SECRET_ENCRYPTION_KEY', 'Module 026 secure-store compatibility');
requireText(publicOrigin, 'ProjectPulse-CRM-ERP-Integration:', 'Module 026 encryption domain separation');
requireText(publicOrigin, 'CryptographicOperations.ZeroMemory', 'secret-derived key cleanup');
rejectText(publicOrigin, 'context.Request.Headers["Origin"].ToString()', 'direct browser-origin trust in global normalizer');

requireText(nginx, 'map $http_x_forwarded_proto $projectpulse_forwarded_proto', 'reverse-proxy scheme preservation');
requireText(nginx, 'proxy_set_header X-Forwarded-Proto $projectpulse_forwarded_proto;', 'API forwarded scheme');
rejectText(nginx, 'proxy_set_header X-Forwarded-Proto $scheme;', 'TLS-losing forwarded scheme');

console.log('SECURITY_ROLE_POLICY_PUBLIC_ORIGIN_VALIDATION=PASS');
