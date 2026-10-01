"""Read-only incident-tool source fences and unconfigured-host rejection."""
from pathlib import Path
import json,subprocess,unittest,os
ROOT=Path(__file__).resolve().parent
class DiagnosticContract(unittest.TestCase):
    def test_fixed_read_only_scope(self):
        s=(ROOT/'Program.cs').read_text()
        for exact in ('SET TRANSACTION READ ONLY',"SET LOCAL statement_timeout='5000'",'LIMIT 4','2026-10-01T03:49:00Z','2026-10-01T03:52:00Z','26d34e5abad4a81f0158e4f40af53fcdaf4ef03b','transaction.RollbackAsync'):
            self.assertIn(exact,s)
        for verb in ('UPDATE ','DELETE ','INSERT ','ALTER ','DROP ','TRUNCATE ','GRANT '):self.assertNotIn(verb,s)
        self.assertNotIn('args[',s)
        self.assertIn('command.CommandTimeout=5',s)
        self.assertIn('free_text_not_exported',s)
    def test_no_secret_or_identifier_logging(self):
        s=(ROOT/'Program.cs').read_text()
        self.assertNotIn('Console.WriteLine(connectionString',s)
        self.assertNotIn('e.Message',s)
        self.assertNotIn('SELECT run_id',s)
        self.assertNotIn('Console.WriteLine(reader',s)
    def test_wrong_environment_rejected_before_loading_database(self):
        env=os.environ.copy();env['PROJECTPULSE_ENVIRONMENT']='production';env['PROJECTPULSE_SOURCE_COMMIT']='wrong'
        r=subprocess.run(['dotnet',str(ROOT/'bin/Release/net10.0/PulsePlannerDiagnostic.dll')],env=env,capture_output=True,text=True,timeout=5)
        self.assertEqual(r.returncode,1)
        result=json.loads(r.stdout);self.assertTrue(result['readOnly']);self.assertEqual(result['status'],'diagnostic_unavailable');self.assertFalse(result['rawContentPublished'])
if __name__=='__main__':unittest.main(verbosity=2)
