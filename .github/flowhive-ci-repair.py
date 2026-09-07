from pathlib import Path
import subprocess
import yaml

root = Path.cwd()
# Preserve all controller commands; move PR data out of GitHub's long format expression.
for name in ['projectpulse-release-test-control-ci.yml','projectpulse-release-test-control-ci-reregistered.yml']:
    p = root/'.github/workflows'/name
    text = p.read_text()
    start = text.index('      - name: Validate governed protected-Test controller\n')
    end = text.index('\n      - name:', start + 10)
    block = text[start:end]
    assert block.count('${{ github.event.pull_request.number }}') == 2
    block = block.replace('${{ github.event.pull_request.number }}', '$PR_NUMBER')
    anchor = '          BASE_SHA: ${{ github.event.pull_request.base.sha }}\n'
    assert block.count(anchor) == 1
    block = block.replace(anchor, anchor + '          PR_NUMBER: ${{ github.event.pull_request.number }}\n')
    p.write_text(text[:start] + block + text[end:])

# Each hosted job gets its own random password and loopback-only dynamic port.
start_template = '''      - name: Start isolated PostgreSQL fixture
        id: fixture_database
        shell: bash
        env:
          FIXTURE_DATABASE: DATABASE_NAME
        run: |
          set -Eeuo pipefail
          umask 077
          [[ "$FIXTURE_DATABASE" == flowhive_execution_test || "$FIXTURE_DATABASE" == flowhive_migrations_test ]]
          name="flowhive-ci-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}"
          [[ "$name" =~ ^flowhive-ci-[0-9]+-[0-9]+$ ]]
          echo "CI_POSTGRES_CONTAINER=$name" >> "$GITHUB_ENV"
          env_file="$(mktemp "$RUNNER_TEMP/flowhive-postgres-XXXXXX")"
          trap 'rm -f -- "$env_file"' EXIT
          trap 'docker rm -f "$name" >/dev/null 2>&1 || true' ERR
          password="$(openssl rand -hex 32)"
          echo "::add-mask::$password"
          printf 'POSTGRES_USER=flowhive\\nPOSTGRES_DB=%s\\nPOSTGRES_PASSWORD=%s\\n' "$FIXTURE_DATABASE" "$password" > "$env_file"
          docker run --detach --rm --name "$name" --publish 127.0.0.1::5432 \\
            --env-file "$env_file" --health-cmd "pg_isready -U flowhive -d $FIXTURE_DATABASE" \\
            --health-interval 2s --health-timeout 2s --health-retries 30 postgres:16 >/dev/null
          health='starting'
          for attempt in {1..30}; do
            health="$(timeout 5s docker inspect --format '{{.State.Health.Status}}' "$name")"
            [[ "$health" != healthy ]] || break
            [[ "$health" != unhealthy ]] || exit 1
            sleep 2
          done
          [[ "$health" == healthy ]] || { echo 'CI PostgreSQL readiness expired.' >&2; exit 1; }
          address="$(timeout 5s docker port "$name" 5432/tcp)"
          [[ "$address" =~ ^127\\.0\\.0\\.1:([0-9]{1,5})$ ]]
          port="${BASH_REMATCH[1]}"
          {
            printf 'PGHOST=127.0.0.1\\nPGPORT=%s\\nPGDATABASE=%s\\nPGUSER=flowhive\\nPGPASSWORD=%s\\n' "$port" "$FIXTURE_DATABASE" "$password"
            printf 'FLOWHIVE_TEST_DB=Host=127.0.0.1;Port=%s;Database=%s;Username=flowhive;Password=%s\\n' "$port" "$FIXTURE_DATABASE" "$password"
          } >> "$GITHUB_ENV"
          trap - ERR
'''
cleanup = '''      - name: Remove isolated PostgreSQL fixture
        if: always()
        shell: bash
        run: |
          set -Eeuo pipefail
          name="${CI_POSTGRES_CONTAINER:-}"
          [[ -n "$name" ]] || exit 0
          [[ "$name" == "flowhive-ci-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}" ]]
          docker rm -f "$name" >/dev/null
'''
for file, job, database, insertion in [
    ('flowhive-enterprise-psa-ci.yml', 'execution-database', 'flowhive_execution_test',
     '      - name: Execute real database deadline, cancellation and concurrent-save regressions\n'),
    ('flowhive-psa-release-control-ci.yml', 'migrations', 'flowhive_migrations_test',
     '      - name: Exercise approved SQL and the real image entrypoint in disposable PostgreSQL\n')]:
    p = root/'.github/workflows'/file
    text = p.read_text()
    start = text.index('  '+job+':\n')
    end = text.find('\n  ', start+len('  '+job+':\n'))
    # YAML top-level job header, not a nested property.
    import re
    match = re.search(r'\n  [a-z][a-z-]+:\n', text[start+len('  '+job+':\n'):])
    end = start+len('  '+job+':\n')+match.start()+1 if match else len(text)
    block = text[start:end]
    service = block.index('    services:\n')
    steps = block.index('    steps:\n')
    before = block[:service]
    if job == 'migrations':
        before += '    env:\n      FLOWHIVE_CANDIDATE_ROOT: ${{ github.workspace }}/candidate\n'
    block = before + block[steps:]
    assert block.count(insertion) == 1
    block = block.replace(insertion, start_template.replace('DATABASE_NAME',database)+insertion)
    block += cleanup
    p.write_text(text[:start]+block+text[end:])

p=root/'tests/flowhive-psa-release-workflow.test.py'
text=p.read_text()
anchor="class WorkflowContract(unittest.TestCase):\n"
assert text.count(anchor)==1
helper='''def verify_ci_script_limits(doc):
    for job in doc['jobs'].values():
        for step in job.get('steps', []):
            body = step.get('run', '')
            assert len(body) <= 21000, 'GitHub run script limit exceeded'
            # Embedded expressions cause GitHub to construct a format() expression
            # with escaped shell quotes/braces. Keep long scripts literal instead.
            assert len(body) < 19000 or '${{' not in body, 'Long CI script must use step environment inputs'

'''
text=text.replace(anchor,helper+anchor)
anchor="    def test_parsed_workflow(self):verify(self.doc)\n"
new='''    def test_controller_ci_script_limits_and_safe_pr_input(self):
        for name in ['projectpulse-release-test-control-ci.yml', 'projectpulse-release-test-control-ci-reregistered.yml']:
            doc=load((ROOT/'.github/workflows'/name).read_text())
            verify_ci_script_limits(doc)
            step=next(s for s in doc['jobs']['validate']['steps'] if s.get('name')=='Validate governed protected-Test controller')
            self.assertEqual(step['env']['PR_NUMBER'], '${{ github.event.pull_request.number }}')
            self.assertEqual(step['run'].count('"$PR_NUMBER"'), 2)
            self.assertNotIn('${{', step['run'])
            subprocess.run(['bash','-n'], input=step['run'], text=True, check=True, capture_output=True)
        for body in ['x'*21001, 'x'*19000+'${{ github.event.pull_request.number }}']:
            with self.assertRaises(AssertionError):
                verify_ci_script_limits({'jobs':{'fixture':{'steps':[{'run':body}]}}})

    def test_ci_databases_use_masked_ephemeral_credentials_and_loopback_only(self):
        # The control-only branch intentionally has no FlowHive feature CI.
        # Both changed fixture jobs are exercised on the feature candidate itself.
        feature=ROOT/'.github/workflows/flowhive-enterprise-psa-ci.yml'
        if not feature.exists():self.skipTest('Feature database fixtures are validated on the exact candidate.')
        for name,job_name in [('flowhive-enterprise-psa-ci.yml','execution-database'),('flowhive-psa-release-control-ci.yml','migrations')]:
            doc=load((ROOT/'.github/workflows'/name).read_text())
            job=doc['jobs'][job_name]
            self.assertNotIn('services', job)
            self.assertNotIn('PGPASSWORD', job.get('env',{}))
            self.assertNotIn('FLOWHIVE_TEST_DB', job.get('env',{}))
            steps=job['steps']
            setup=next(s for s in steps if s.get('id')=='fixture_database')
            body=setup['run']
            for required in ['openssl rand -hex 32','::add-mask::$password','--publish 127.0.0.1::5432','--env-file "$env_file"','trap - ERR','CI_POSTGRES_CONTAINER=$name','timeout 5s docker inspect']:
                self.assertIn(required,body)
            self.assertNotIn('${{', body)
            cleanup=steps[-1]
            self.assertEqual(cleanup['name'],'Remove isolated PostgreSQL fixture')
            self.assertEqual(cleanup['if'],'always()')
            self.assertIn('docker rm -f "$name"',cleanup['run'])
            for step in [setup,cleanup]:
                subprocess.run(['bash','-n'],input=step['run'],text=True,check=True,capture_output=True)
'''
assert text.count(anchor)==1
p.write_text(text.replace(anchor,anchor+new))
p=root/'docs/modules/module-066-project-flowhive/BOUNDED-PLANNER-VALIDATION.md'
p.write_text(p.read_text()+'''

## Combined PR874 candidate: CI repair (2026-09-07)

The combined candidate retains merge `55ebb51fda1917f202ce6561ed5f5e635468d01c`.
Two required controller workflows failed before jobs were created. Their first
run block was 20,920 characters with embedded PR-number expressions. Read-only
inspection run `34073102940` recovered GitHub's exact diagnostic: line 99,
column 14 exceeded the maximum expression length of 21,000. PR numbers now
enter through a step environment variable; the existing validation commands and
release boundaries remain intact. Parsed-workflow regressions reject overlong
run blocks and embedded expressions in long scripts.

The two disposable PostgreSQL CI jobs now generate a masked random password per
job, bind to a dynamic loopback-only port and remove the container after testing.
No application/provider credential is introduced or read. GitGuardian incident
37039936 refers to the historical localhost execution-test credential in commit
`662ff01516fd15686a6162ef39db9389cbefb0f4`. Removing it from the current workflow
is not evidence that a history-scanning check has cleared. Its test-credential
classification and a successful exact-head check are still required before
release; no scanner bypass or history rewrite is part of this repair.

Engineering CI, protected deployment and live SOW-to-WBS acceptance are separate
gates. These workflow repairs do not establish live AI or complete PSA acceptance.
''')
for file in root.glob('.github/workflows/*.yml'):
    if file.name in ['flowhive-enterprise-psa-ci.yml','flowhive-psa-release-control-ci.yml','projectpulse-release-test-control-ci.yml','projectpulse-release-test-control-ci-reregistered.yml']:
        yaml.load(file.read_text(),Loader=yaml.BaseLoader)
subprocess.run(['git','diff','--check'],check=True)
