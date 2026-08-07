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
}

const app = fs.readFileSync(path.join(sourceRoot, 'App.jsx'), 'utf8');
const loginStyles = fs.readFileSync(path.join(sourceRoot, 'pulse-login.css'), 'utf8');
const index = fs.readFileSync(path.join(webRoot, 'index.html'), 'utf8');

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

const invoiceModule = fs.readFileSync(path.join(backendRoot, 'Modules', 'InvoiceBillingModule.cs'), 'utf8');
const invoiceMigration = fs.readFileSync(path.join(repositoryRoot, 'database', 'migrations', '075_pulse_product_rebrand.sql'), 'utf8');
if (!invoiceModule.includes('$"PULSE-{seriesNumber.Value:000000}-{installmentNumber}"')) failures.push('invoice:pulse-prefix');
if (!invoiceMigration.includes(`^(${legacyAbbreviation}|PULSE)-`)) failures.push('invoice:legacy-compatibility');
if (!invoiceMigration.includes("'PULSE-'")) failures.push('invoice:migration-prefix');

console.log(`PULSE_REBRAND_VALIDATION=${failures.length === 0 ? 'PASSED' : 'FAILED'}`);
if (failures.length > 0) {
  console.error(`PULSE_REBRAND_FAILURES=${failures.join(',')}`);
  process.exitCode = 1;
}
