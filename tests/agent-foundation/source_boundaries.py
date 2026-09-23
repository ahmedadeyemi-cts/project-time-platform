"""Static guardrails supplement compiled tests; neither is live UAT proof."""
from pathlib import Path
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[2]
BASE = '4c31007053359b1cf41681d686742f6c08fbb3b6'
AGENTS = ROOT / 'src/backend/ProjectTime.Api/Agents'

class Boundaries(unittest.TestCase):
    def test_only_new_foundation_paths(self):
        names = subprocess.check_output(['git', 'diff', '--name-only', BASE], cwd=ROOT, text=True).splitlines()
        permitted = ('src/backend/ProjectTime.Api/Agents/', 'src/frontend/project-time-web/src/agents/',
                     'tests/AgentFoundationTests/', 'tests/agent-foundation/', 'docs/agent-foundation/')
        for path in names:
            self.assertTrue(path.startswith(permitted) or path == '.github/workflows/celar-agent-foundation-ci.yml', path)
            old = subprocess.run(['git', 'cat-file', '-e', f'{BASE}:{path}'], cwd=ROOT, capture_output=True)
            self.assertNotEqual(old.returncode, 0, f'Existing application/controller changed: {path}')

    def test_no_new_provider_route_or_direct_client(self):
        source = '\n'.join(path.read_text() for path in AGENTS.glob('*.cs'))
        bridge = (AGENTS / 'Module064AgentDecisionSource.cs').read_text()
        self.assertIn('CelarAiCapabilityRouter router', bridge)
        self.assertIn('router.GenerateAsync(request, execution, () => "", token)', bridge)
        self.assertIn('CelarAiCapabilityCatalog.HelpAssistant', bridge)
        self.assertIn('false, [], "011"', bridge)
        for forbidden in ('new HttpClient', 'IProjectPulseAiProvider', 'new NpgsqlConnection', 'Process.Start(', 'HttpMethod.Post', 'ApiKey'):
            self.assertNotIn(forbidden, source)
        self.assertIn('agent_model_refused', bridge)
        self.assertIn('result.Provider == CelarAiCapabilityTargets.Local', bridge)

    def test_no_activation_or_business_mutation(self):
        source = '\n'.join(path.read_text() for path in AGENTS.glob('*.cs'))
        self.assertIn('bool Enabled = false', source)
        self.assertIn('value.Environment != "test"', source)
        for forbidden in ('MapPost(', 'AddHostedService', 'PublishAsync(', 'ExecuteNonQuery', 'SendMailAsync', 'ApproveAsync('):
            self.assertNotIn(forbidden, source)
        self.assertIn('public bool Applied => false', source)
        self.assertIn('public bool NotificationSent => false', source)
        self.assertIn('DenyAllAgentAuthority', source)

    def test_capability_metadata_is_not_role_grant(self):
        kernel = (AGENTS / 'AgentKernel.cs').read_text()
        self.assertNotIn('.Audience', kernel)
        self.assertNotIn('SUPER_ADMINISTRATOR', kernel)
        self.assertIn('authority.CheckAsync', kernel)
        self.assertIn('actor.IsViewAs || actor.ActualUserId != actor.EffectiveUserId', kernel)
        self.assertIn('recipient.Role != AgentCapabilityCatalog.RecipientRole(handoff)', kernel)

    def test_component_is_not_a_live_entrypoint(self):
        component = (ROOT / 'src/frontend/project-time-web/src/agents/WorkAssistantPanel.jsx').read_text()
        self.assertNotIn('dangerouslySetInnerHTML', component)
        self.assertNotIn('fetch(', component)
        self.assertIn('AbortController', component)
        self.assertIn('activeContext.current === key', component)
        self.assertIn('run?.handoff', component)
        self.assertIn('not been delivered or approved', component)

    def test_ci_cannot_write_or_deploy(self):
        workflow = (ROOT / '.github/workflows/celar-agent-foundation-ci.yml').read_text()
        self.assertIn('permissions:\n  contents: read', workflow)
        for forbidden in ('secrets.', 'environment:', 'contents: write', 'actions: write', 'workflow_dispatch:', 'git push', 'azure/login'):
            self.assertNotIn(forbidden, workflow)
        self.assertIn('dotnet build src/backend/ProjectTime.Api', workflow)
        self.assertIn('npm run build', workflow)

if __name__ == '__main__': unittest.main()
