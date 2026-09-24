#!/usr/bin/env python3
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parent
import kit
spec = importlib.util.spec_from_file_location('opencloud_build', ROOT/'build-images.py')
build = importlib.util.module_from_spec(spec)
spec.loader.exec_module(build)

class PackagingTests(unittest.TestCase):
    def test_readiness_is_not_installation_success(self):
        state = kit.readiness()
        self.assertFalse(state['installableRelease'])
        self.assertFalse(state['development']['browserVpnRequired'])
        self.assertEqual(state['development']['pulse'], 'https://pulse-dev.ussignal.cloud')
        self.assertEqual(state['development']['celar'], 'https://celarai-dev.ussignal.cloud')
        self.assertEqual(len(state['requiredBeforeInstallationAcceptance']), 8)
        self.assertTrue(all(item['status'] == 'pending' for item in state['requiredBeforeInstallationAcceptance']))

    def test_original_runtime_hostname_guard_is_not_weakened(self):
        source = (ROOT.parents[1]/'src/backend/ProjectTime.Api/Ai/PulseAiExternalHttpsRuntimePolicy.cs').read_text()
        self.assertIn('ApprovedHost = "celarai.onenecklab.com"', source)
        self.assertIn('celar-hostname-authorization', (ROOT/'readiness.json').read_text())

    def test_api_recipe_preserves_build_and_embeds_revision(self):
        original = (ROOT.parent/'containers/api/Dockerfile').read_text()
        result = build.render_api(original, 'a'*40)
        self.assertIn('/p:ProjectPulseSourceRevision=${SOURCE_REVISION}', result)
        self.assertIn('dotnet publish', result)
        self.assertIn('antiword', result)
        self.assertIn('USER $APP_UID', result)
        self.assertIn('opencloud-api-entrypoint', result)

    def test_api_recipe_rejects_ambiguous_baseline(self):
        with self.assertRaises(ValueError):
            build.render_api('an unrecognized Dockerfile', 'a'*40)

    def test_source_revision_rejects_shell_and_ref_inputs(self):
        for value in ['main', 'abc', 'a'*40+';echo bad', 'a'*39, 'A'*40]:
            with self.subTest(value=value), self.assertRaises(ValueError):
                build.render_api('', value)

    def test_configuration_packages_are_allowlisted_and_checksummed(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, PACKAGING_SOURCE_COMMIT='b'*40):
            files = kit.bundle(Path(directory))
            self.assertEqual(len(files), 2)
            for filename in files:
                with zipfile.ZipFile(Path(directory)/filename) as archive:
                    manifest = json.loads(archive.read('PACKAGE-MANIFEST.json'))
                    self.assertFalse(manifest['includesImages'])
                    self.assertFalse(manifest['includesModelWeights'])
                    self.assertFalse(manifest['includesDatabase'])
                    self.assertFalse(manifest['includesSecrets'])
                    self.assertFalse(manifest['installableRelease'])
                    self.assertEqual(set(archive.namelist()), set(manifest['filesSha256'])|{'PACKAGE-MANIFEST.json'})
                    for name, expected in manifest['filesSha256'].items():
                        self.assertEqual(hashlib.sha256(archive.read(name)).hexdigest(), expected)
                        self.assertNotIn('.local', name)
                        self.assertFalse(name.endswith(('.dump','.pem','.key','.pfx','.ttf','.woff','.onnx','.safetensors')))

    def test_schema_guard_uses_actual_declared_relations(self):
        check = (ROOT/'pulse/schema-check.sql').read_text()
        native = (ROOT.parents[1]/'database/migrations/032_projectpulse_native_administration_documents.sql').read_text()
        self.assertIn('projectpulse_native_admin_documents', native)
        self.assertIn('projectpulse_native_admin_documents', check)
        laya = (ROOT.parent/'laya/schema.sql').read_text()
        self.assertIn('celar_laya_settings', laya)
        self.assertIn('BEGIN READ ONLY', check)
        self.assertNotRegex(check.upper(), r'\b(CREATE TABLE|DROP TABLE|DELETE FROM|TRUNCATE)\b')

    def test_startup_does_not_forge_ai_or_billing_approval(self):
        script = (ROOT/'pulse/api-entrypoint.sh').read_text()
        self.assertIn('legacy database connection overrides', script)
        self.assertIn('/run/secrets/api-database-password', script)
        self.assertNotIn('PROJECTPULSE_AI_RELEASE_PHASE=', script)
        self.assertNotIn('TRAINING_ENABLED=', script)
        self.assertNotIn('docker.sock', script)

    def test_laya_and_maintenance_gaps_are_explicit(self):
        compose = (ROOT/'celar-ai/compose.celar-ai.yaml').read_text()
        edge = (ROOT/'celar-ai/Caddyfile').read_text()
        self.assertIn('${CELAR_LAYA_IMAGE:?', compose)
        self.assertIn('network_mode: none', compose)
        self.assertIn('503', edge)
        self.assertIn('maintenance', edge)

    def test_no_automatic_publication_or_deployment(self):
        workflow = (ROOT.parents[1]/'.github/workflows/opencloud-packaging-ci.yml').read_text()
        self.assertNotIn('packages: write', workflow)
        self.assertNotIn('self-hosted', workflow)
        self.assertNotIn('secrets.', workflow)
        self.assertNotIn('pull_request_target', workflow)
        self.assertNotIn('docker push', workflow)
        self.assertNotIn('compose up', workflow)

if __name__ == '__main__':
    unittest.main(verbosity=2)
