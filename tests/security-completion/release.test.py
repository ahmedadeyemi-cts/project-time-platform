import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import yaml

ROOT=Path(__file__).resolve().parents[2]
workflow=yaml.safe_load((ROOT/'.github/workflows/release.yml').read_text())
publisher=workflow['jobs']['publish-release']
code=publisher['steps'][0]['run'].split("<<'PY_RELEASE'\n",1)[1].split('\nPY_RELEASE',1)[0]

class ReleaseTests(unittest.TestCase):
    def execute(self, overrides=None):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            gh=root/'gh'
            gh.write_text('#!'+sys.executable+'\nimport json,os\nprint(json.dumps({"object":{"type":"commit","sha":os.environ.get("FAKE_TAG_SHA",os.environ["GITHUB_SHA"])}}))\n')
            gh.chmod(0o700)
            env={**os.environ,'PATH':directory+os.pathsep+os.environ['PATH'],
                 'GITHUB_REF_NAME':'v1.2.3','GITHUB_SHA':'a'*40,'GITHUB_REPOSITORY':'Owner/Repo',
                 'WEB_IMAGE':'ghcr.io/owner/repo/web','API_IMAGE':'ghcr.io/owner/repo/api',
                 'WEB_DIGEST':'sha256:'+'b'*64,'API_DIGEST':'sha256:'+'c'*64,**(overrides or {})}
            result=subprocess.run([sys.executable,'-c',code],env=env,cwd=root,capture_output=True)
            manifest=root/'release-digests.json'
            return result.returncode,json.loads(manifest.read_text()) if manifest.exists() else None
    def test_write_authority_is_separate_from_build_dependencies(self):
        self.assertEqual(workflow['permissions'],{'contents':'read'})
        self.assertEqual(workflow['jobs']['build-scan-publish']['permissions']['contents'],'read')
        self.assertEqual(publisher['permissions'],{'contents':'write'})
        self.assertEqual(publisher['needs'],'build-scan-publish')
        self.assertTrue(all('uses' not in s for s in publisher['steps']))
        self.assertNotIn('${{',publisher['steps'][0]['run'])
    def test_valid_manifest(self):
        status,manifest=self.execute()
        self.assertEqual(status,0)
        self.assertEqual(manifest['commit'],'a'*40)
        self.assertEqual(manifest['images']['api'],'ghcr.io/owner/repo/api@sha256:'+'c'*64)
    def test_tag_command_payload_never_becomes_manifest(self):
        for tag in ['v1.2.3";echo INJECTED','v1.2.3\nmalicious','v1.02.3']:
            status,manifest=self.execute({'GITHUB_REF_NAME':tag})
            self.assertNotEqual(status,0);self.assertIsNone(manifest)
    def test_retag_and_wrong_image_identity_fail_closed(self):
        for change in [{'FAKE_TAG_SHA':'d'*40},{'WEB_IMAGE':'ghcr.io/other/repo/web'}, {'API_DIGEST':'latest'}, {'GITHUB_SHA':'invalid'}]:
            status,manifest=self.execute(change)
            self.assertNotEqual(status,0);self.assertIsNone(manifest)

if __name__=='__main__': unittest.main()
