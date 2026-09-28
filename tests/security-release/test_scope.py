import copy
import hashlib
import unittest
import scope

class ScopeTests(unittest.TestCase):
    def setUp(self):
        self.paths = ['src/a.cs', scope.MANIFEST]
        self.manifest = {'base': scope.BASE, 'implementation': scope.IMPLEMENTATION,
                         'files': self.paths, 'contentHashes': [{'path': 'src/a.cs', 'sha256': hashlib.sha256(b'a').hexdigest()}]}
    def test_exact_inventory(self):
        scope.verify_paths(self.paths, self.manifest)
    def test_added_deleted_and_unhashed_files(self):
        for paths in (self.paths + ['evil'], self.paths[1:]):
            with self.assertRaises(RuntimeError): scope.verify_paths(paths, self.manifest)
        changed = copy.deepcopy(self.manifest)
        changed['contentHashes'] = []
        with self.assertRaises(RuntimeError): scope.verify_paths(self.paths, changed)
    def test_mutated_content(self):
        with self.assertRaises(RuntimeError): scope.verify_content(b'b', self.manifest['contentHashes'][0]['sha256'], '100644')
    def test_rehashed_controller_cannot_change(self):
        with self.assertRaises(RuntimeError): scope.verify_content(b'b', hashlib.sha256(b'b').hexdigest(), '100644', b'a')
    def test_symlink_and_submodule(self):
        for mode in ('120000', '160000'):
            with self.assertRaises(RuntimeError): scope.verify_content(b'a', self.manifest['contentHashes'][0]['sha256'], mode)
    def test_identity_fences(self):
        values = [scope.BRANCH, scope.REPOSITORY, 'main', '1209']
        scope.verify_identity(*values)
        for index in range(4):
            wrong = values.copy(); wrong[index] = 'other'
            with self.assertRaises(RuntimeError): scope.verify_identity(*wrong)
    def test_baseline(self):
        for key in ('base', 'implementation'):
            wrong = copy.deepcopy(self.manifest); wrong[key] = 'other'
            with self.assertRaises(RuntimeError): scope.verify_paths(self.paths, wrong)
    def test_control_boundaries(self):
        for path in ('.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/projectpulse-deploy-production.yml',
                     '.github/flowhive-psa-protected-test-candidate.json', '.github/CODEOWNERS',
                     'scripts/release-test/flowhive-psa-admission.mjs', 'deployment/containers/api/entrypoint.sh'):
            self.assertTrue(scope.deployment_control(path), path)
        for path in scope.CI_DISPATCH: self.assertFalse(scope.deployment_control(path))

if __name__ == '__main__': unittest.main()
