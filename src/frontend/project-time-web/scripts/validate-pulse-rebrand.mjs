import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const repositoryRoot = path.resolve(webRoot, '..', '..', '..');
const sourceRoot = path.join(webRoot, 'src');
const backendRoot = path.join(repositoryRoot, 'src', 'backend', 'ProjectTime.Api');
const legacyProductName = ['Project', 'Health', 'Dashboard'].join(' ');
const legacyAbbreviation = ['P', 'H', 'D'].join('');
const legacyLoginStem = ['project', 'health', 'dashboard', 'login'].join('-');
const retiredAiName = ['Pulse', 'AI'].join(' ');
const retiredSpacedProductName = ['Project', 'Pulse'].join(' ');
const retiredCollapsedProductName = ['Project', 'Pulse'].join('');
const failures = [];

function sourceFiles(root) {
  return fs.readdirSync(root, { withFileTypes: true }).flatMap((entry) => {
    const absolutePath = path.join(root, entry.name);
    if (entry.isDirectory()) return sourceFiles(absolutePath);
    return /\.(?:cs|css|html|js|jsx|mjs)$/.test(entry.name) ? [absolutePath] : [];
  });
}

const runtimeFiles = [path.join(webRoot, 'index.html'), ...sourceFiles(sourceRoot), ...sourceFiles(backendRoot)];
for (const filePath of runtimeFiles) {
  const source = fs.readFileSync(filePath, 'utf8');
  const relativePath = path.relative(webRoot, filePath);
  if (source.includes(legacyProductName)) failures.push(`${relativePath}:legacy-product-name`);
  if (new RegExp(`\\b${legacyAbbreviation}\\b`).test(source)) failures.push(`${relativePath}:legacy-abbreviation`);
  if (source.includes(retiredAiName)) failures.push(`${relativePath}:retired-ai-name`);
  if (source.includes(retiredSpacedProductName)) failures.push(`${relativePath}:retired-spaced-product-name`);

  source.split(/\\r?\\n/).forEach((line, index) => {
    if (!new RegExp(`\\b${retiredCollapsedProductName}\\b`).test(line)) return;

    const isCompatibilityContract = [
      /X-ProjectPulse-/,
      /ConnectionStrings:ProjectPulse/,
      /ProjectPulse-native/,
      /sourceSystem:\s*['"]ProjectPulse['"]/,
      /UserAgent\.ParseAdd\("ProjectPulse-/,
      /Encoding\.UTF8\.GetBytes\(\$?"ProjectPulse[:-]/
    ].some((pattern) => pattern.test(line));

    if (!isCompatibilityContract) {
      failures.push(`${relativePath}:${index + 1}:retired-collapsed-product-name`);
    }
  });
}

const app = fs.readFileSync(path.join(sourceRoot, 'App.jsx'), 'utf8');
const loginStyles = fs.readFileSync(path.join(sourceRoot, 'pulse-login.css'), 'utf8');
const index = fs.readFileSync(path.join(webRoot, 'index.html'), 'utf8');
const integratedAppGenerator = fs.readFileSync(path.join(scriptDirectory, 'generate-module-001-integrated-app.mjs'), 'utf8');

if (!index.includes('<title>Pulse</title>')) failures.push('index:title');
if (!app.includes("const PULSE_CAPABILITIES = Object.freeze([")) failures.push('app:capabilities');
if (!app.includes('function PulseLoginHero({ showSecureMotion = false })')) failures.push('app:login-hero');
if (!app.includes('Welcome to Pulse')) failures.push('app:welcome');
if (!app.includes('US Signal Pulse')) failures.push('app:official-brand-lockup');
if (!loginStyles.includes('.pulse-auth-shell')) failures.push('styles:pulse-namespace');
if (/(^|[^a-z])phd-(?:auth|platform|secure|ai|capability|motion)/i.test(`${app}\n${loginStyles}`)) {
  failures.push('runtime:legacy-login-namespace');
}
if (fs.existsSync(path.join(sourceRoot, `${legacyLoginStem}.css`))) failures.push('styles:legacy-file');
if (fs.existsSync(path.join(scriptDirectory, `validate-${legacyLoginStem}.mjs`))) failures.push('validator:legacy-file');
if (integratedAppGenerator.includes(`[${retiredCollapsedProductName} optional module request]`)
  || !integratedAppGenerator.includes("console.error('[Pulse optional module request]'")) {
  failures.push('generator:optional-module-diagnostic');
}

const invoiceModule = fs.readFileSync(path.join(backendRoot, 'Modules', 'InvoiceBillingModule.cs'), 'utf8');
const invoiceMigration = fs.readFileSync(path.join(repositoryRoot, 'database', 'migrations', '075_pulse_product_rebrand.sql'), 'utf8');
const flowHiveMigration = fs.readFileSync(path.join(repositoryRoot, 'database', 'migrations', '074_module_066_project_flowhive_production.sql'), 'utf8');
const canonicalLabelMigration = fs.readFileSync(path.join(repositoryRoot, 'database', 'migrations', '082_pulse_celar_ai_canonical_labels.sql'), 'utf8');
const packageManifest = JSON.parse(fs.readFileSync(path.join(webRoot, 'package.json'), 'utf8'));
if (!invoiceModule.includes('$"PULSE-{seriesNumber.Value:000000}-{installmentNumber}"')) failures.push('invoice:pulse-prefix');
if (!invoiceMigration.includes(`^(${legacyAbbreviation}|PULSE)-`)) failures.push('invoice:legacy-compatibility');
if (!invoiceMigration.includes("'PULSE-'")) failures.push('invoice:migration-prefix');
if (!flowHiveMigration.includes("replace(permission_name, 'Pulse AI', 'Celar AI')")) failures.push('migration074:legacy-ai-convergence');
if (!flowHiveMigration.includes("permission_name ILIKE '%Pulse AI%'")) failures.push('migration074:legacy-ai-source-filter');
if (!canonicalLabelMigration.includes("'082_pulse_celar_ai_canonical_labels'")) failures.push('migration082:ledger');
if (!canonicalLabelMigration.includes("current_setting('projectpulse.project_number_issuance', TRUE)")) failures.push('migration082:project-number-compatibility');
if (!canonicalLabelMigration.includes('source-system values')
  || !canonicalLabelMigration.includes('API headers')
  || !canonicalLabelMigration.includes('configuration keys')) {
  failures.push('migration082:compatibility-boundary');
}
if (packageManifest.name !== 'project-pulse-web') failures.push('package:technical-identity-changed');

console.log(`PULSE_REBRAND_VALIDATION=${failures.length === 0 ? 'PASSED' : 'FAILED'}`);
if (failures.length > 0) {
  console.error(`PULSE_REBRAND_FAILURES=${failures.join(',')}`);
  process.exitCode = 1;
}
