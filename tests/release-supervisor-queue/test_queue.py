"""Queue-loss regression and exact-control preservation. No GitHub/Azure writes."""
import copy,itertools,json,subprocess,unittest
from pathlib import Path
import yaml
ROOT=Path(__file__).resolve().parents[2]
BASE="c21098d5dcefa5fecb4812d5a8d3a0ac84b61480"
GROUP="module025-protected-uat-control"
CHANGED=("module025-protected-uat-control.yml","module025-retained-register-check.yml")
SHARED=(*CHANGED,"flowhive-psa-protected-test-admission.yml")

def original(name):
    return subprocess.check_output(["git","show",BASE+":.github/workflows/"+name],cwd=ROOT,text=True)
def current(name):return (ROOT/".github/workflows"/name).read_text()
def verify_delta(name,text):
    old=original(name)
    marker="  group: "+GROUP+"\n  cancel-in-progress: false"
    expected=old.replace(marker,"  group: "+GROUP+"\n  queue: max\n  cancel-in-progress: false",1)
    if old.count(marker)!=1 or text!=expected:raise ValueError("Only the single queue-policy insertion is permitted")

def enqueue(pending,run,mode):
    # Model GitHub's documented default replacement vs max waiting queue.
    if mode=="single":return [run]
    if mode!="max":raise ValueError("unsupported queue mode")
    return pending+[run] if len(pending)<100 else pending

class QueueTests(unittest.TestCase):
    def test_exact_two_line_change(self):
        for n in CHANGED:verify_delta(n,current(n))
    def test_every_shared_workflow_preserves_pending_requests(self):
        for n in SHARED:
            c=yaml.safe_load(current(n))["concurrency"]
            self.assertEqual(c,{"group":GROUP,"queue":"max","cancel-in-progress":False})
    def test_authority_jobs_triggers_and_steps_are_identical(self):
        for n in CHANGED:
            before=yaml.safe_load(original(n));after=yaml.safe_load(current(n))
            after["concurrency"].pop("queue")
            self.assertEqual(before,after)
    def test_inherited_controller_recognition_is_exact(self):
        original_suffix="c7b3c7ae88aceb33a0c77f816a21a8ad28952fc4)$ ]]"
        accepted_suffix="c7b3c7ae88aceb33a0c77f816a21a8ad28952fc4|94fe4bf498c3d89347db62749279f566d0c26ce7)$ ]]"
        registration=json.loads((ROOT/"tests/security-release/controller_registration.json").read_text())
        self.assertEqual(registration["controllerBlob"],"94fe4bf498c3d89347db62749279f566d0c26ce7")
        for name in ("pr1139-uat-recovery-ci.yml","pr1140-uat-recovery-ci.yml"):
            old=original(name);self.assertEqual(old.count(original_suffix),1)
            self.assertEqual(current(name),old.replace(original_suffix,accepted_suffix,1))
            self.assertIn('git diff --exit-code "$BASE_SHA" HEAD -- .github/workflows/projectpulse-deploy-test.yml .github/workflows/projectpulse-deploy-production.yml',current(name))

    def test_baseline_can_replace_authorized_pending_release(self):
        pending=["authorized-release"]
        self.assertNotIn("authorized-release",enqueue(pending,"unrelated-comment","single"))
    def test_all_sibling_orderings_retain_authorized_release(self):
        for ordering in itertools.permutations(SHARED):
            pending=["authorized-release"]
            for n in ordering:pending=enqueue(pending,n,yaml.safe_load(current(n))["concurrency"]["queue"])
            self.assertEqual(pending[0],"authorized-release")
            self.assertEqual(len(pending),4)
    def test_max_queue_is_bounded_not_unlimited(self):
        full=[str(x) for x in range(100)]
        self.assertEqual(enqueue(full,"overflow","max"),full)
    def test_permission_or_gate_edits_are_rejected(self):
        for n in CHANGED:
            good=current(n)
            for before,after in [("cancel-in-progress: false","cancel-in-progress: true"),("github.actor == 'ahmedadeyemi-cts'","true"),("contents: read","contents: write")]:
                self.assertIn(before,good)
                with self.assertRaises(ValueError):verify_delta(n,good.replace(before,after,1))
    def test_original_api_release_and_trust_root_unchanged(self):
        for p in (".github/workflows/projectpulse-deploy-test.yml","scripts/validate-deployment-concurrency-governance.mjs",".github/workflows/deployment-concurrency-governance-ci.yml","src/backend/ProjectTime.Api/Program.cs","docs/security/security-closeout-register-20260930.json"):
            self.assertEqual((ROOT/p).read_bytes(),subprocess.check_output(["git","show",BASE+":"+p],cwd=ROOT))

if __name__=="__main__":unittest.main(verbosity=2)
