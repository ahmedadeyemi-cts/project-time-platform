#!/usr/bin/env python3
"""Negative tests for the precise app scope and narrow framing boundary."""
import importlib.util
from pathlib import Path
import unittest

spec=importlib.util.spec_from_file_location('scope',Path(__file__).with_name('teams-delivery-release-scope.py'))
scope=importlib.util.module_from_spec(spec);spec.loader.exec_module(scope)

class ScopeTests(unittest.TestCase):
    def test_only_exact_identity(self):
        scope.validate_identity(scope.BASE,scope.BRANCH,'1156')
        for args in [('0'*40,scope.BRANCH,'1156'),(scope.BASE,'unrelated','1156'),(scope.BASE,scope.BRANCH,'1157')]:
            with self.assertRaises(RuntimeError):scope.validate_identity(*args)
    def test_complete_regular_set(self):
        scope.validate_files(scope.FILES,dict.fromkeys(scope.FILES,'100644'))
    def test_unrelated_file_denied(self):
        for addition in ['.github/workflows/projectpulse-deploy-test.yml','database/migrations/999.sql','src/backend/ProjectTime.Api/Program.cs']:
            with self.assertRaises(RuntimeError):scope.validate_files(scope.FILES|{addition},dict.fromkeys(scope.FILES|{addition},'100644'))
    def test_missing_work_denied(self):
        for name in scope.FILES:
            files=scope.FILES-{name}
            with self.assertRaises(RuntimeError):scope.validate_files(files,dict.fromkeys(files,'100644'))
    def test_symlinks_and_executables_denied(self):
        for mode in ['120000','100755','160000']:
            modes=dict.fromkeys(scope.FILES,'100644');modes[scope.NGINX]=mode
            with self.assertRaises(RuntimeError):scope.validate_files(scope.FILES,modes)
    def test_registration_only_additive(self):
        original='#!/usr/bin/env bash\nset -Eeuo pipefail\n# prior classification\n'
        scope.validate_prepare(original,original+scope.REGISTRATION)
        for bad in [scope.REGISTRATION, original+scope.REGISTRATION+scope.REGISTRATION, original.replace('pipefail','pipefail || true')+scope.REGISTRATION]:
            with self.assertRaises(RuntimeError):scope.validate_prepare(original,bad)
    def test_only_exact_landing_added(self):
        before="server {\n    add_header X-Frame-Options SAMEORIGIN always;\n    location = /index.html {\n        try_files $uri =404;\n    }\n}\n"
        after=before.replace('    location = /index.html {',scope.LANDING+'    location = /index.html {')
        scope.validate_nginx(before,after)
        for bad in [after.replace('SAMEORIGIN','ALLOWALL'),after.replace('location = /teams-notifications/index.html','location /'),after.replace("https://*.cloud.microsoft","*"),after+scope.LANDING]:
            with self.assertRaises(RuntimeError):scope.validate_nginx(before,bad)
    def test_ambiguous_index_denied(self):
        with self.assertRaises(RuntimeError):scope.validate_nginx('    location = /index.html {\n'*2,scope.LANDING)
    def test_public_page_only_not_api(self):
        self.assertIn('location = /teams-notifications/index.html',scope.LANDING)
        self.assertNotIn('location /api',scope.LANDING)
        self.assertNotIn("'unsafe-inline'",scope.LANDING)
        self.assertIn('form-action',scope.LANDING)
    def test_no_controller_or_runtime_scope_expansion(self):
        self.assertEqual(len(scope.FILES),19)
        self.assertNotIn('.github/workflows/projectpulse-deploy-test.yml',scope.FILES)
        self.assertNotIn('.github/workflows/module025-protected-uat-control.yml',scope.FILES)
        self.assertNotIn('scripts/release-test/validate-protected-test-controller-branches.sh',scope.FILES)

unittest.main()
