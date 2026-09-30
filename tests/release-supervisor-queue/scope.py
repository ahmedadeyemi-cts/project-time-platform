"""Exact PR1226 queue-repair scope. Does not grant deployment authority."""
from pathlib import Path
import hashlib,json,os,subprocess
ROOT=Path(__file__).resolve().parents[2]
BASE="c21098d5dcefa5fecb4812d5a8d3a0ac84b61480"
BRANCH="fix/protected-uat-supervisor-queue-20260930"
REPO="ahmedadeyemi-cts/project-time-platform"
MANIFEST="tests/release-supervisor-queue/manifest.json"
ALLOWED={".github/workflows/module025-protected-uat-control.yml",
".github/workflows/module025-retained-register-check.yml",
".github/workflows/pr1139-uat-recovery-ci.yml",".github/workflows/pr1140-uat-recovery-ci.yml",
".github/workflows/release-supervisor-queue-ci.yml",
"scripts/release-test/validate-protected-test-controller-branches.sh",
"tests/release-supervisor-queue/test_queue.py","tests/release-supervisor-queue/scope.py",MANIFEST}
def git(*args):return subprocess.check_output(["git",*args],cwd=ROOT)
def need(value,message):
    if not value:raise ValueError(message)
def main():
    branch=os.getenv("GITHUB_HEAD_REF") or git("branch","--show-current").decode().strip()
    need(branch==BRANCH and os.getenv("GITHUB_REPOSITORY",REPO)==REPO,"Wrong repository/branch")
    need(os.getenv("GITHUB_BASE_REF","main")=="main" and os.getenv("PR_NUMBER","1226")=="1226","Wrong target/PR")
    if os.getenv("GITHUB_EVENT_PATH"):
        e=json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text())
        if "pull_request" in e:
            p=e["pull_request"]
            need(e["number"]==1226 and p["head"]["ref"]==BRANCH and p["head"]["repo"]["full_name"]==REPO
                 and p["base"]["ref"]=="main" and p["base"]["repo"]["full_name"]==REPO,"Unexpected event identity")
    subprocess.run(["git","merge-base","--is-ancestor",BASE,"HEAD"],cwd=ROOT,check=True)
    m=json.loads((ROOT/MANIFEST).read_text());paths=git("diff","--name-only",BASE,"HEAD").decode().splitlines()
    need(m["base"]==BASE and set(paths)==ALLOWED and m["files"]==sorted(ALLOWED),"Unreviewed source path")
    need(set(m["sha256"])==ALLOWED-{MANIFEST},"Incomplete source hash inventory")
    for p in paths:
        entry=git("ls-tree","HEAD","--",p).decode().split()
        need(entry and entry[0]==("100755" if p=="scripts/release-test/validate-protected-test-controller-branches.sh" else "100644") and not (ROOT/p).is_symlink(),"Unsafe source mode")
        data=(ROOT/p).read_bytes();need(data==git("show","HEAD:"+p),"Dirty source")
        if p!=MANIFEST:need(hashlib.sha256(data).hexdigest()==m["sha256"][p],"Source bytes differ")
    need(git("rev-parse","HEAD:.github/workflows/projectpulse-deploy-test.yml").decode().strip()=="94fe4bf498c3d89347db62749279f566d0c26ce7","Canonical controller identity changed")
    subprocess.run(["git","diff","--check",BASE,"HEAD"],cwd=ROOT,check=True)
    print("SUPERVISOR_QUEUE_EXACT_SCOPE=PASS deployment_authority=UNCHANGED")
if __name__=="__main__":main()
