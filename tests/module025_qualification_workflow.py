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
