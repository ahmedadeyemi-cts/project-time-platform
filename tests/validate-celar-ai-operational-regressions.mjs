import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

const root = process.cwd();
const file = (relative) => path.join(root, relative);
const read = (relative) => fs.readFileSync(file(relative), 'utf8');

function requireMarker(name, condition) {
  if (!condition) throw new Error(`${name}=FAILED`);
  console.log(`${name}=PASSED`);
}

const generator = read('src/backend/ProjectTime.Api/build/generate-celar-ai-universal-answer-reliability.awk');
const props = read('src/backend/ProjectTime.Api/Directory.Build.props');
const transformer = file('src/backend/ProjectTime.Api/build/repair-project-management-summary-schema.py');
const canonicalProgram = read('src/backend/ProjectTime.Api/Program.cs');
const intentModule = read('src/backend/ProjectTime.Api/Modules/CelarAiOperationsIntentModule.cs');
const migration077 = read('database/migrations/077_module_082_enterprise_project_risk_register.sql');

requireMarker(
  'CELAR_OPERATIONS_INTENT_ROUTE_DEFINED',
  intentModule.includes('/api/celar-ai/v1/operations/intent')
    && intentModule.includes('MapCelarAiOperationsIntentEndpoints')
);
requireMarker(
  'CELAR_OPERATIONS_INTENT_ROUTE_REGISTERED',
  generator.split('endpoints.MapCelarAiOperationsIntentEndpoints();').length - 1 === 1
);
requireMarker(
  'CELAR_PROJECT_SUMMARY_REPAIR_TARGET',
  props.includes('RepairCelarAiOperationalGeneratedSources')
    && props.includes('repair-project-management-summary-schema.py')
    && props.includes('DependsOnTargets="GenerateScopedRbacSources"')
    && props.includes('BeforeTargets="CoreCompile"')
);
requireMarker(
  'CELAR_MIGRATION_077_ENTERPRISE_RISK_SCHEMA',
  migration077.includes('probability_score SMALLINT')
    && migration077.includes('overall_impact_score SMALLINT GENERATED ALWAYS AS')
    && migration077.includes('mitigation_actions TEXT')
    && migration077.includes('response_plan TEXT')
);
requireMarker(
  'CELAR_LEGACY_SUMMARY_QUERY_PRESENT_ONLY_IN_CANONICAL_SOURCE',
  canonicalProgram.includes('pr.probability, pr.impact, pr.risk_status, pr.mitigation_plan')
);

const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'celar-project-summary-schema-'));
const generatedProgram = path.join(tempRoot, 'Program.ScopedRbac.g.cs');
try {
  fs.writeFileSync(generatedProgram, canonicalProgram, 'utf8');
  execFileSync('python3', [transformer, '--input', generatedProgram], { stdio: 'inherit' });
  const repaired = fs.readFileSync(generatedProgram, 'utf8');
  requireMarker(
    'CELAR_PROJECT_SUMMARY_MIGRATION_077_COLUMNS',
    repaired.includes('pr.probability_score')
      && repaired.includes('pr.overall_impact_score')
      && repaired.includes('pr.mitigation_actions')
      && repaired.includes('pr.response_plan')
  );
  requireMarker(
    'CELAR_PROJECT_SUMMARY_RETIRED_COLUMNS_REMOVED',
    !repaired.includes('pr.probability,')
      && !repaired.includes('pr.impact,')
      && !repaired.includes('pr.mitigation_plan')
  );
  requireMarker(
    'CELAR_PROJECT_SUMMARY_RESPONSE_CONTRACT_PRESERVED',
    repaired.includes('END AS probability')
      && repaired.includes('END AS impact')
      && repaired.includes('AS mitigation_plan')
      && repaired.includes('probability = reader.GetString(2)')
      && repaired.includes('impact = reader.GetString(3)')
  );
} finally {
  fs.rmSync(tempRoot, { recursive: true, force: true });
}

console.log('CELAR_AI_OPERATIONAL_REGRESSIONS=PASS');
