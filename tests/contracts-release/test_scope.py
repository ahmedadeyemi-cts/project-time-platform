import importlib.util
import hashlib
import unittest
from pathlib import Path
p=Path(__file__).with_name('scope.py')
spec=importlib.util.spec_from_file_location('scope',p)
scope=importlib.util.module_from_spec(spec);spec.loader.exec_module(scope)
class ScopeTests(unittest.TestCase):
    def test_exact_paths(self):
        scope.verify_paths(['a'],{'files':['a']})
        for paths in [[],['a','b'],['.github/workflows/projectpulse-deploy-test.yml']]:
            with self.assertRaises(RuntimeError): scope.verify_paths(paths,{'files':['a']})
    def test_frozen_authority_cannot_be_allowlisted(self):
        paths=['.github/workflows/projectpulse-deploy-test.yml']
        with self.assertRaises(RuntimeError): scope.verify_paths(paths,{'files':paths})
    def test_hash_bound_content(self):
        digest=hashlib.sha256(b'reviewed').hexdigest()
        scope.verify_hash(b'reviewed',digest)
        with self.assertRaises(RuntimeError): scope.verify_hash(b'changed',digest)
unittest.main()
