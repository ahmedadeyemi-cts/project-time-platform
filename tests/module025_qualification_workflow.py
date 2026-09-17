"""Project qualification guards into their unchanged normal-deployment behavior.

Separate delta tests require every existing step body and native protection to
match the immutable pre-qualification controller. No deployment step is dropped.
"""
import copy

GATE = "(inputs.qualification_provider == '' || inputs.qualification_provider == 'none')"
NAMES = {'Validate qualification-only selection', 'Compile the isolated one-phase runner',
         'Qualify one phase in the Test private network', 'Upload bounded provider qualification evidence'}
SHARED = {'Check out exact authorized release', 'Verify admitted controller identity before deployment mutations',
          'Set up .NET 10', 'Sign in to protected Test subscription'}

# Explicit, separately exercised additions to the normal scoped deployment.
# Reverse only these exact deltas when comparing the rest of the controller
# against its immutable baseline; never omit a whole protected step.
RETENTION_MIGRATION_DELTA = '''if [[ "$ACCEPTANCE_SCOPE" == sow_role ]]; then
  bash scripts/release-test/build-and-run-module025-retention-migration-106.sh
fi
'''
ORIGINAL_SCOPED_RUN = '''"$RUNNER_TEMP/flowhive-psa-browser/bin/python" scripts/release-test/run-module025-installed-sa-uat.py
"$RUNNER_TEMP/flowhive-psa-browser/bin/python" scripts/release-test/run-flowhive-my-role-browser.py
'''
COMPLETE_SCOPED_RUN = '''sow_result=0
"$RUNNER_TEMP/flowhive-psa-browser/bin/python" scripts/release-test/run-module025-installed-sa-uat.py || sow_result=$?
if (( sow_result == 130 || sow_result == 143 )); then exit "$sow_result"; fi
my_role_result=0
"$RUNNER_TEMP/flowhive-psa-browser/bin/python" scripts/release-test/run-flowhive-my-role-browser.py || my_role_result=$?
jq -n --argjson sowExit "$sow_result" --argjson myRoleExit "$my_role_result" \\
  '{sowLifecycleExit:$sowExit,myRoleExit:$myRoleExit,fullRequestedScopePassed:false}' > "$EVIDENCE_DIR/module025-scoped-results.json"
(( sow_result == 0 && my_role_result == 0 ))
'''


def previous_acceptance_projection(doc):
    doc = deployment_projection(doc)
    steps = {s['name']: s for s in doc['jobs']['deploy']['steps']}
    migration = steps['Apply and verify Migrations 086, 088, and 093 through 100 inside Test private network']
    assert migration['run'].count(RETENTION_MIGRATION_DELTA) == 1
    migration['run'] = migration['run'].replace(RETENTION_MIGRATION_DELTA, '')
    scoped = steps['Verify Module 025 scoped deployment identity and lifecycle']
    assert scoped['run'].count(COMPLETE_SCOPED_RUN) == 1
    scoped['run'] = scoped['run'].replace(COMPLETE_SCOPED_RUN, ORIGINAL_SCOPED_RUN)
    return doc


def deployment_projection(doc):
    doc = copy.deepcopy(doc)
    trigger = doc.get('on', doc.get(True))
    dispatch = trigger.get('workflow_dispatch') if isinstance(trigger, dict) else None
    if not isinstance(dispatch, dict) or 'qualification_provider' not in dispatch.get('inputs', {}):
        return doc
    trigger['workflow_dispatch']['inputs'].pop('qualification_provider')
    steps = []
    for step in doc['jobs']['deploy']['steps']:
        if step['name'] in NAMES:
            continue
        if step['name'] not in SHARED:
            condition = step.get('if', '')
            if condition == GATE:
                step.pop('if')
            else:
                prefix = '${{ ' + GATE + ' && ('
                assert condition.startswith(prefix) and condition.endswith(') }}'), step['name']
                condition = condition[len(prefix):-4]
                # The original style varies; compare semantics in the delta test.
                if step['name'] in {'Run protected-Test assigned-work visibility UAT',
                    'Run protected-Test utilization role-scoping UAT',
                    'Enable exact-run Module 025 protected-Test authorization fixture',
                    'Run protected-Test Module 025 SOW/GSD generation lifecycle UAT',
                    'Restore exact prior Test images after application failure',
                    'Rollback protected Test API configuration on failure'}:
                    condition = '${{ ' + condition + ' }}'
                step['if'] = condition
        steps.append(step)
    doc['jobs']['deploy']['steps'] = steps
    return doc
