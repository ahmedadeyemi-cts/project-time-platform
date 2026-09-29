import copy
import hashlib
import unittest
import scope

class ScopeTests(unittest.TestCase):
    def setUp(self):
        self.paths=sorted(scope.ALLOWED)
        self.manifest={'base':scope.BASE,'files':self.paths,'sha256':{path:hashlib.sha256(b'a').hexdigest() for path in self.paths if path != scope.MANIFEST}}
    def test_exact_inventory(self): scope.verify_paths(self.paths,self.manifest)
    def test_added_deleted_unhashed_duplicate_paths(self):
        for paths in [self.paths+['src/extra.cs'],self.paths[1:]]:
            with self.assertRaises(RuntimeError): scope.verify_paths(paths,self.manifest)
        for change in ['missing_hash','duplicate','wrong_base']:
            manifest=copy.deepcopy(self.manifest)
            if change=='missing_hash': manifest['sha256']={}
            elif change=='duplicate': manifest['files']+=['src/a.cs']
            else: manifest['base']='0'*40
            with self.assertRaises(RuntimeError): scope.verify_paths(self.paths,manifest)
    def test_replaced_path_with_matching_manifest_rejected(self):
        manifest=copy.deepcopy(self.manifest)
        manifest['files'][0]='src/unregistered.cs'
        manifest['files'].sort()
        with self.assertRaises(RuntimeError): scope.verify_paths(manifest['files'],manifest)
    def test_wrong_identity(self):
        identity=[scope.BRANCH,scope.REPOSITORY,'main','1216']
        scope.verify_identity(*identity)
        with self.assertRaises(RuntimeError): scope.verify_identity(*identity[:3], '1212')
        for i in range(4):
            wrong=identity.copy();wrong[i]='other'
            with self.assertRaises(RuntimeError): scope.verify_identity(*wrong)
    def test_changed_bytes_symlinks_and_submodules(self):
        scope.verify_content(b'a',self.manifest['sha256'][scope.RESOLVER],'100644')
        for content,mode in [(b'b','100644'),(b'a','120000'),(b'a','160000')]:
            with self.assertRaises(RuntimeError): scope.verify_content(content,self.manifest['sha256'][scope.RESOLVER],mode)
    def test_authority_remains_frozen(self):
        for path in ['scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh','.github/workflows/projectpulse-deploy-test.yml','.github/workflows/projectpulse-deploy-production.yml',
                     '.github/workflows/module025-protected-uat-control.yml','.github/workflows/flowhive-psa-installed-acceptance.yml',
                     '.github/CODEOWNERS','.github/flowhive-psa-protected-test-candidate.json',
                     'scripts/release-test/flowhive-psa-admission.mjs','scripts/release-test/run-project-planning-document-authority-migration-job.sh']:
            self.assertTrue(scope.frozen(path),path)
            manifest={'base':scope.BASE,'files':sorted([path,scope.MANIFEST]),'sha256':{path:'a'}}
            with self.assertRaises(RuntimeError): scope.verify_paths(manifest['files'],manifest)
        for path in [scope.RESOLVER,scope.CI_WORKFLOW]: self.assertFalse(scope.frozen(path))

if __name__=='__main__': unittest.main()
