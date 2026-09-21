"""Static source/rollout checks, not proof of live AI success."""
from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[2]
def read(path): return (ROOT / path).read_text()

class Boundaries(unittest.TestCase):
    def test_provider_payload_is_explicit_and_scope_only(self):
        s = read('src/backend/ProjectTime.Api/Ai/Module025ServiceScopePolicy.cs')
        self.assertIn('JsonSerializer.Serialize(new { serviceScope = evidence.ServiceOverview })', s)
        self.assertNotIn('Serialize(evidence)', s)
        self.assertNotIn('BuildPrivateComposePrompt', s)
        self.assertNotIn('Regex', s)
        self.assertIn('ServiceScopeFullTextApproved', s)
        self.assertIn('ServiceScopeApprovalSchemaReady', s)
    def test_legacy_adoption_is_explicit(self):
        s = read('src/frontend/project-time-web/src/module025/SowGsdAuthoringWorkspace.jsx')
        self.assertIn('Use existing input as Service Scope', s)
        self.assertIn('engagement.serviceScope != null ? { serviceScope: engagement.serviceScope }', s)
        self.assertIn('Service Overview (generated, editable for review)', s)
    def test_migration_does_not_backfill_input_or_consent(self):
        s = read('database/migrations/124_module025_service_scope.sql')
        self.assertNotIn('UPDATE module025_sow_gsd_engagements', s)
        self.assertNotIn('UPDATE ai_capability_routes', s)
        self.assertIn('service_scope_full_text_approved BOOLEAN NOT NULL DEFAULT FALSE', s)
        self.assertIn('ADD COLUMN IF NOT EXISTS service_scope TEXT', s)
    def test_migration_is_in_existing_guarded_runner(self):
        s = read('scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh')
        self.assertIn('sha256sum --check --status database/module025-service-scope.sha256', s)
        self.assertIn('psql -X -v ON_ERROR_STOP=1 --file "$ROOT/database/migrations/124_module025_service_scope.sql"', s)
        self.assertIn('psql -X -v ON_ERROR_STOP=1 --file "$ROOT/database/verify-module025-service-scope.sql"', s)
    def test_original_scope_and_manual_overview_are_separate(self):
        s = read('src/backend/ProjectTime.Api/Modules/Module025SowGsdModule.cs')
        self.assertIn('"service_scope_changed"', s)
        self.assertIn('previousServiceScope = current.EffectiveServiceScope', s)
        self.assertIn('"service_scope_package_replaced"', s)
        self.assertIn('Module025ServiceScopeWorkspace.SaveOverviewAsync', s)
        self.assertIn('Module025TaskDrafts.PreserveExisting', s)
    def test_authority_is_not_duplicated(self):
        s = read('src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs')
        self.assertIn('var orderedTargets = CelarAiRouteExecutionPolicy.Order(route,', s)
        self.assertIn('module025_full_service_scope_approval_required', s)
        policy = read('src/backend/ProjectTime.Api/Ai/CelarAiRouteExecutionPolicy.cs')
        self.assertIn('route.Targets.ToArray();', policy)
        self.assertNotIn('OrderBy', policy)
    def test_approval_audit_is_append_only(self):
        s = read('src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs')
        self.assertIn('previous_scope_full_text_approved, new_scope_full_text_approved', s)
        self.assertNotIn('UPDATE ai_capability_route_audit', s)
    def test_final_ci_has_no_write_or_deployment_authority(self):
        s = read('.github/workflows/module025-service-scope-ci.yml')
        self.assertIn('contents: read', s)
        self.assertNotIn('contents: write', s)
        self.assertNotIn('secrets.', s)
        self.assertNotIn('environment:', s)
        self.assertNotIn('git push', s)

if __name__ == '__main__': unittest.main()
