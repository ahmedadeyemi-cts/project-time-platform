import copy
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
source = (ROOT/'deployment/azure/scripts/az09b-configure-west-custom-domain-tls.sh').read_text()
code = source.split("<<'PY_GATEWAY_HTTPS'\n", 1)[1].split('\nPY_GATEWAY_HTTPS', 1)[0]
namespace = {'__name__': 'gateway_test'}
exec(compile(code, 'gateway_inline', 'exec'), namespace)
plan = namespace['redirect_plan']

class GatewayTests(unittest.TestCase):
    def setUp(self):
        self.gateway = {'tags': {'environment': 'test'}, 'httpListeners': [
            {'id': 'http', 'protocol': 'Http'},
            {'id': 'https', 'protocol': 'Https', 'hostName': 'phd-west-test.onenecklab.com'}],
            'redirectConfigurations': [{'id': 'redirect', 'name': 'canonical',
                'targetListener': {'id': 'https'}, 'redirectType': 'Permanent',
                'includePath': True, 'includeQueryString': True}],
            'requestRoutingRules': [
                {'name': 'catch-all', 'ruleType': 'Basic', 'httpListener': {'id': 'http'},
                 'backendAddressPool': {'id': 'backend'}, 'backendHttpSettings': {'id': 'settings'},
                 'redirectConfiguration': None},
                {'name': 'secure', 'ruleType': 'Basic', 'httpListener': {'id': 'https'},
                 'backendAddressPool': {'id': 'backend'}}]}
    def test_catch_all_repaired_and_https_backend_preserved(self):
        self.assertEqual(plan(self.gateway, 'canonical'), ['catch-all'])
        rule = self.gateway['requestRoutingRules'][0]
        rule.update(backendAddressPool=None, backendHttpSettings=None, redirectConfiguration={'id': 'redirect'})
        self.assertEqual(plan(self.gateway, 'canonical'), [])
    def test_wrong_environment_or_redirect_refused(self):
        for field, value in [('redirectType', 'Found'), ('includePath', False), ('includeQueryString', False), ('targetListener', {'id': 'http'})]:
            changed = copy.deepcopy(self.gateway)
            changed['redirectConfigurations'][0][field] = value
            with self.assertRaises(ValueError): plan(changed, 'canonical')
        self.gateway['tags']['environment'] = 'production'
        with self.assertRaises(ValueError): plan(self.gateway, 'canonical')
    def test_unknown_listener_path_map_and_untrusted_host_refused(self):
        for field, value in [('httpListener', {'id': 'missing'}), ('ruleType', 'PathBasedRouting'), ('urlPathMap', {'id': 'map'})]:
            changed = copy.deepcopy(self.gateway)
            changed['requestRoutingRules'][0][field] = value
            with self.assertRaises(ValueError): plan(changed, 'canonical')
        self.gateway['httpListeners'][1]['hostName'] = 'untrusted.invalid'
        with self.assertRaises(ValueError): plan(self.gateway, 'canonical')

if __name__ == '__main__': unittest.main()
