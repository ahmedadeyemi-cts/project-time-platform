import copy,hashlib,unittest,scope

class ScopeTests(unittest.TestCase):
    def setUp(self):
        self.paths=sorted(scope.ALLOWED)
        self.manifest={'base':scope.BASE,'files':self.paths,
            'sha256':{p:hashlib.sha256(b'a').hexdigest() for p in self.paths if p!=scope.MANIFEST}}
    def test_exact_inventory(self): scope.verify_paths(self.paths,self.manifest)
    def test_added_deleted_unhashed_duplicate_paths(self):
        for paths in [self.paths+['src/extra.cs'],self.paths[1:]]:
            with self.assertRaises(RuntimeError): scope.verify_paths(paths,self.manifest)
        for change in ['missing_hash','duplicate','wrong_base']:
            m=copy.deepcopy(self.manifest)
            if change=='missing_hash':m['sha256']={}
            elif change=='duplicate':m['files']+=['src/a.cs']
            else:m['base']='0'*40
            with self.assertRaises(RuntimeError):scope.verify_paths(self.paths,m)
    def test_wrong_identity(self):
        identity=[scope.BRANCH,scope.REPOSITORY,'main',scope.PR_NUMBER];scope.verify_identity(*identity)
        with self.assertRaises(RuntimeError):scope.verify_identity(*identity[:3],'1243')
        for i in range(4):
            wrong=identity.copy();wrong[i]='other'
            with self.assertRaises(RuntimeError):scope.verify_identity(*wrong)
    def test_changed_bytes_symlinks_submodules(self):
        scope.verify_content(b'a',hashlib.sha256(b'a').hexdigest(),'100644')
        for data,mode in [(b'b','100644'),(b'a','120000'),(b'a','160000')]:
            with self.assertRaises(RuntimeError):scope.verify_content(data,hashlib.sha256(b'a').hexdigest(),mode)
    def test_production_and_external_authority_remain_frozen(self):
        for path in ['.github/workflows/projectpulse-deploy-production.yml','.github/CODEOWNERS',
                     '.github/flowhive-psa-protected-test-candidate.json']:
            self.assertTrue(scope.frozen(path),path)
            m={'base':scope.BASE,'files':sorted([path,scope.MANIFEST]),'sha256':{path:'a'}}
            with self.assertRaises(RuntimeError):scope.verify_paths(m['files'],m)

if __name__=='__main__':unittest.main()
