from pathlib import Path
import subprocess
root=Path.cwd()
p=root/'tests/flowhive-psa-release-control.mjs'
t=p.read_text()
anchor="export function verifyFiles(changed, manifest, mode = 'initial') {\n"
assert t.count(anchor)==1
replacement="""export const repairBase = '55ebb51fda1917f202ce6561ed5f5e635468d01c';
const repairRepository = 'ahmedadeyemi-cts/project-time-platform';
const repairBranch = 'release/flowhive-psa-protected-test-admission-20260906';
export function verifyRepairContext(context) {
  assert.equal(context?.eventName, 'pull_request', 'PR876 repair requires a pull-request event.');
  assert.equal(context?.repository, repairRepository, 'Wrong repair repository.');
  assert.equal(context?.base, repairBase, 'PR876 repair is bound to the reviewed PR874 merge base.');
  assert.match(context?.head || '', /^[0-9a-f]{40}$/, 'Exact repair head is required.');
  const event = context?.event;
  assert.equal(event?.number, 876, 'The seven-file exception is exclusive to PR876.');
  assert.equal(event?.repository?.full_name, repairRepository, 'Wrong event repository.');
  const pr = event?.pull_request;
  assert.equal(pr?.number, 876, 'Wrong repair pull request.');
  assert.equal(pr?.state, 'open', 'The repair must still be open.');
  assert.equal(pr?.base?.ref, 'main', 'Wrong repair base branch.');
  assert.equal(pr?.base?.sha, repairBase, 'The event base is not the reviewed repair base.');
  assert.equal(pr?.base?.repo?.full_name, repairRepository, 'Wrong repair base repository.');
  assert.equal(pr?.head?.ref, repairBranch, 'Wrong repair source branch.');
  assert.equal(pr?.head?.repo?.full_name, repairRepository, 'Forked repair source is not admitted.');
  assert.equal(pr?.head?.sha, context.head, 'The checked-out repair head does not match the event.');
}
export function verifyFiles(changed, manifest, mode = 'initial', context = null) {
"""
t=t.replace(anchor,replacement)
a="  assert.ok(['initial','pr874-digest-repair'].includes(mode), 'Unrecognized control repair.');\n"
assert t.count(a)==1
t=t.replace(a,a+"  if (mode === 'pr874-digest-repair') verifyRepairContext(context);\n")
a="  const isRepair = changed.length === repairFiles.length;\n  verifyFiles(changed, manifest, isRepair ? 'pr874-digest-repair' : 'initial');\n"
assert t.count(a)==1
b="""  const event = process.env.GITHUB_EVENT_PATH
    ? JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8')) : null;
  const context = { event, eventName: process.env.GITHUB_EVENT_NAME,
    repository: process.env.GITHUB_REPOSITORY, base, head: git('rev-parse', 'HEAD') };
  const isRepair = event?.number === 876;
  verifyFiles(changed, manifest, isRepair ? 'pr874-digest-repair' : 'initial', context);
"""
t=t.replace(a,b);p.write_text(t)
p=root/'tests/flowhive-psa-admission.test.mjs'
t=p.read_text()
a="import { files, repairFiles, verifyFiles, verifyController } from './flowhive-psa-release-control.mjs';"
b="import { files, repairFiles, repairBase, verifyFiles, verifyController } from './flowhive-psa-release-control.mjs';"
assert t.count(a)==1;t=t.replace(a,b)
a="test('PR874 digest repair retains the full boundary and accepts only its seven exact paths',()=>{\n"
assert t.count(a)==1
prefix="""function repairContext() {
  const repository = 'ahmedadeyemi-cts/project-time-platform';
  const head = 'a'.repeat(40);
  return { eventName: 'pull_request', repository, base: repairBase, head,
    event: { number: 876, repository: { full_name: repository },
      pull_request: { number: 876, state: 'open',
        base: { ref: 'main', sha: repairBase, repo: { full_name: repository } },
        head: { ref: 'release/flowhive-psa-protected-test-admission-20260906', sha: head,
          repo: { full_name: repository } } } } };
}
"""
t=t.replace(a,prefix+a)
t=t.replace("  verifyFiles(repairFiles,files,'pr874-digest-repair');", "  verifyFiles(repairFiles,files,'pr874-digest-repair',repairContext());")
t=t.replace("verifyFiles([...repairFiles,extra],files,'pr874-digest-repair')", "verifyFiles([...repairFiles,extra],files,'pr874-digest-repair',repairContext())")
t=t.replace("verifyFiles(repairFiles.slice(1),files,'pr874-digest-repair')", "verifyFiles(repairFiles.slice(1),files,'pr874-digest-repair',repairContext())")
t=t.replace("verifyFiles(repairFiles,repairFiles,'pr874-digest-repair')", "verifyFiles(repairFiles,repairFiles,'pr874-digest-repair',repairContext())")
t+="""
test('PR876 repair cannot be reused on another base, identity, event or checkout',()=>{
  assert.throws(()=>verifyFiles(repairFiles,files,'pr874-digest-repair'));
  const mutations = [
    x=>{ x.base='b'.repeat(40); },
    x=>{ x.eventName='push'; },
    x=>{ x.repository='outsider/project-time-platform'; },
    x=>{ x.event.number=877; },
    x=>{ x.event.repository.full_name='outsider/project-time-platform'; },
    x=>{ x.event.pull_request.number=877; },
    x=>{ x.event.pull_request.state='closed'; },
    x=>{ x.event.pull_request.base.ref='release'; },
    x=>{ x.event.pull_request.base.sha='b'.repeat(40); },
    x=>{ x.event.pull_request.base.repo.full_name='outsider/project-time-platform'; },
    x=>{ x.event.pull_request.head.ref='unrelated-repair'; },
    x=>{ x.event.pull_request.head.repo.full_name='outsider/project-time-platform'; },
    x=>{ x.event.pull_request.head.sha='b'.repeat(40); },
    x=>{ x.head=''; },
    x=>{ x.event=null; }
  ];
  for (const mutate of mutations) {
    const context=repairContext();mutate(context);
    assert.throws(()=>verifyFiles(repairFiles,files,'pr874-digest-repair',context));
  }
  verifyFiles(files,files,'initial');
  assert.throws(()=>verifyFiles(repairFiles,files,'initial',repairContext()));
});
"""
p.write_text(t)
p=root/'.github/flowhive-psa-protected-test-candidate.json'
t=p.read_text();assert t.count('2914eeeb7265e2d6998d2e68d2f723e6ad56c922')==1
p.write_text(t.replace('2914eeeb7265e2d6998d2e68d2f723e6ad56c922','1538548d5bc85c83c5ceb14795e1cfc8536d721c'))
p=root/'docs/releases/FLOWHIVE-PSA-PROTECTED-TEST-ADMISSION.md'
t=p.read_text().replace('2914eeeb7265e2d6998d2e68d2f723e6ad56c922','1538548d5bc85c83c5ceb14795e1cfc8536d721c')
t+='''

### PR876 review closure and exact-head refresh

The seven-path digest repair is now limited to open PR876 from the reviewed
repository and release branch onto main at the exact PR874 merge base
`55ebb51fda1917f202ce6561ed5f5e635468d01c`. Validation compares the actual checkout,
resolved Git base and GitHub pull-request event, not just a seven-file count.
Missing context, a different PR/base/branch/repository, a fork, a closed PR or a
stale checked-out head is rejected. The original twenty-file control boundary is
unchanged. Negative tests exercise each identity mismatch separately.

The refreshed candidate also repairs both required controller workflows' GitHub
expression-length failure and removes static PostgreSQL CI fixture credentials.
The historical test-credential finding in GitGuardian must be classified and the
exact-head security check cleared before the combined candidate is deployed.
No failed check is waived; the application PR remains draft. This control refresh
preserves the canonical deployment/dispatcher bytes and approved migration hashes.
'''
p.write_text(t)
subprocess.run(['git','diff','--check'],check=True)
