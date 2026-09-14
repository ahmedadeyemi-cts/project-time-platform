#!/usr/bin/env node
// Pulse Azure DEV Environment — single-source gate runner (scaffolded by Verity; stage 82, ADR-0029).
//
// Executes the committed gate definition `.verity/gates.json` — the SAME file
// the Verity local gate runner (`verity gates run`) executes — so CI can never
// drift from the gates that were green locally ("tests exist but CI never runs
// them" is impossible by construction).
//
// Judgment is by EXIT CODE ONLY (Verity's test-honesty invariant, ADR-0028):
// each command runs with inherited stdio — output is never piped, never
// parsed — and the FIRST nonzero exit fails this process with that same code.
// NO definition, an unreadable one, or an EMPTY gate list FAILS LOUDLY:
// a repository whose gates are missing must never report a green check.
//
// Definition shape (ordered — run top to bottom, stop at first failure):
//   { "schema": 1, "gates": [ { "name": "test", "command": "npm test" } ] }
const fs = require('fs');
const { spawnSync } = require('child_process');

const FILE = '.verity/gates.json';

function fail(message, code) {
  console.error('FAIL: ' + message);
  process.exit(code || 1);
}

if (!fs.existsSync(FILE)) {
  fail(
    'no gate definition at ' +
      FILE +
      ' — define your gates (shape in the header of this file) and commit it. ' +
      'A repository with no committed gates must never report a green check (Verity ADR-0029).',
  );
}

let def;
try {
  def = JSON.parse(fs.readFileSync(FILE, 'utf8'));
} catch (err) {
  fail(FILE + ' is not valid JSON: ' + err.message);
}
if (!def || def.schema !== 1 || !Array.isArray(def.gates) || def.gates.length === 0) {
  fail(
    FILE +
      ' must be { "schema": 1, "gates": [ { "name": ..., "command": ... }, ... ] } with at ' +
      'least one gate — an empty gate list proves nothing and must never read green.',
  );
}
for (const gate of def.gates) {
  // Non-empty, matching the engine's parseGateDefinition exactly (Verity
  // stage 84): an empty name is unnameable in a record and an empty command
  // is a shell no-op that would read as a passing gate — never green.
  if (
    !gate ||
    typeof gate.name !== 'string' ||
    gate.name === '' ||
    typeof gate.command !== 'string' ||
    gate.command === ''
  ) {
    fail('every gates[] entry needs a non-empty string name and a non-empty string command');
  }
}

for (const gate of def.gates) {
  console.log('gate ' + gate.name + ': ' + gate.command);
  const run = spawnSync(gate.command, { shell: true, stdio: 'inherit' });
  const code = run.status === 0 ? 0 : Number.isInteger(run.status) ? run.status : 1;
  if (code !== 0) {
    fail('gate ' + gate.name + ' exited ' + code + ' (judged by exit code only)', code);
  }
}
console.log('all ' + def.gates.length + ' gate(s) green (exit-code judged)');
