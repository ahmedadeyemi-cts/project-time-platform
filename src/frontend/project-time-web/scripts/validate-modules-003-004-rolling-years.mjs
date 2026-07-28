import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  PROJECTPULSE_ROLLING_YEAR_WINDOW,
  getRollingYearOptions
} from '../src/rolling-year-window.js';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, '..');
const read = (relativePath) => fs.readFileSync(path.join(webRoot, relativePath), 'utf8');

assert.deepEqual(PROJECTPULSE_ROLLING_YEAR_WINDOW, {
  previousYears: 3,
  currentYears: 1,
  futureYears: 6,
  totalYears: 10
});

const years2026 = getRollingYearOptions(2026);
assert.deepEqual(years2026, [2023, 2024, 2025, 2026, 2027, 2028, 2029, 2030, 2031, 2032]);
assert.equal(years2026.length, 10);
assert.equal(years2026.indexOf(2026), 3);
assert.equal(years2026[0], 2026 - 3);
assert.equal(years2026.at(-1), 2026 + 6);

const years2030 = getRollingYearOptions(2030);
assert.deepEqual(years2030, [2027, 2028, 2029, 2030, 2031, 2032, 2033, 2034, 2035, 2036]);
assert.equal(new Set(years2030).size, years2030.length);

assert.throws(() => getRollingYearOptions('not-a-year'), /positive integer year/);

const utilization = read('src/YearlyUtilizationPanel.jsx');
assert.match(utilization, /import \{ getRollingYearOptions \} from '\.\/rolling-year-window\.js';/);
assert.match(utilization, /const \[selectedYear, setSelectedYear\] = useState\(currentYear\);/);
assert.match(utilization, /getRollingYearOptions\(currentYear\)/);
assert.doesNotMatch(utilization, /2026 \+ index/);
assert.doesNotMatch(utilization, /currentYear >= 2026/);
assert.doesNotMatch(utilization, /length: 11/);

const managerUtilization = read('src/EngineeringTeamLeadUtilizationPanel.jsx');
const managerUtilizationCss = read('src/engineering-team-lead-utilization.css');
assert.match(managerUtilization, /import '\.\/engineering-team-lead-utilization\.css';/);
assert.match(managerUtilization, /<table className="engineering-utilization-table">/);
assert.match(managerUtilization, /<colgroup>/);
assert.match(managerUtilization, /<th scope="col">Engineer<\/th>/);
assert.match(managerUtilization, /<th scope="col">Team<\/th>/);
assert.match(managerUtilization, /<th scope="col">Annual utilization<\/th>/);
assert.match(managerUtilization, /<th scope="col">Billable hours<\/th>/);
for (const quarter of [1, 2, 3, 4]) {
  assert.match(managerUtilization, new RegExp(`<th scope="col" className="quarter-heading">Q${quarter}<\\/th>`));
}
assert.match(managerUtilization, /<th scope="row" className="engineer-cell">/);
assert.match(managerUtilization, /className="engineer-cell-content"/);
assert.match(managerUtilization, /className="annual-utilization-content"/);
assert.match(managerUtilization, /quarterFor\(member, quarterNumber\)/);
assert.match(managerUtilization, /colSpan="8"/);
assert.doesNotMatch(managerUtilization, /className="team-member-table"/);
assert.doesNotMatch(managerUtilization, /className="member-quarter-list"/);

assert.match(managerUtilizationCss, /\.engineering-utilization-table\s*\{/);
assert.match(managerUtilizationCss, /border-collapse:\s*separate/);
assert.match(managerUtilizationCss, /table-layout:\s*fixed/);
assert.match(managerUtilizationCss, /border-right:\s*1px solid var\(--border\)/);
assert.match(managerUtilizationCss, /overflow-x:\s*auto/);
assert.match(managerUtilizationCss, /\.engineer-cell-content\s*\{[^}]*display:\s*flex/s);
assert.match(managerUtilizationCss, /\.annual-utilization-content\s*\{[^}]*display:\s*grid/s);
assert.doesNotMatch(managerUtilizationCss, /\.engineer-cell\s*\{[^}]*display:/s);
assert.doesNotMatch(managerUtilizationCss, /\.annual-utilization-cell\s*\{[^}]*display:/s);
assert.match(managerUtilizationCss, /\.quarter-cell/);
assert.match(managerUtilizationCss, /@media \(max-width: 640px\)/);

const generator = read('scripts/generate-module-001-integrated-app.mjs');
assert.match(generator, /import \{ getRollingYearOptions \} from '\.\/rolling-year-window\.js';/);
assert.match(generator, /const holidayYearOptions = getRollingYearOptions\(\)\.map\(String\);/);
assert.match(generator, /Generated app could not locate the Module 004 hard-coded holiday year window/);
assert.match(generator, /rollingYears=modules003004/);

const packageJson = JSON.parse(read('package.json'));
assert.equal(
  packageJson.scripts['validate:modules003004-rolling-years'],
  'node ./scripts/validate-modules-003-004-rolling-years.mjs'
);
assert.match(packageJson.scripts.build, /validate:modules003004-rolling-years/);

console.log('MODULES_003_004_ROLLING_YEARS_VALIDATION=PASS reference2026=2023-2032 reference2030=2027-2036 total=10 managerTable=aligned-8-columns-native-cells');
