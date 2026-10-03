"""Exact reviewed service-order projection; never grants deployment authority.
All existing UAT remains mandatory and runs against the activated services.
"""
import hashlib
BASE_SHA256='6c587203f890a5525e041c750fc5076b688db81aa5c51345d0a659706a7449ff'
LEGACY_SHA256='11a5cbc14270c720518a37a5bcd539bac650d8c49b374f6fb155b901147c74d3'
CURRENT_SHA256='6c7d701fc02599626f5fcfb638f4f5d4809e27102dbeb2085ac5c7f81532ca62'
PREREQUISITE_SHA256='941c13231b28d0fa40e0e72af02a079c31f4a6b6a78c27e2af156bf93ee2eae6'
PREREQUISITE_PATH_SHA256='b1583d34382fc774c9b40ac5b145f2a5f7154ab06e48203310a1f402e262fdef'
STALE_SOW_MAINTENANCE_SHA256='267e6105d955f372617ac9376ff5bb4ae16e6089fb0490ea6feaead7d3b4b71b'
BLOCK_SHA256='4c235d3c70ae9e93dc237b9fa62ebe86f818e4b5a87074c309ce6130d7689ce5'
PRE_BLOCK_SHA256='2231356843b8111d165b2986b008cd67a100a61eb591119a12162ef1e63d60c0'
POST_BLOCK_SHA256='88800a50a5637fb41d51b60c3fa4edd52c6f57500696f7203dc7d4252929663d'
START=b'      - name: Build private Pulse service images\n'
MIDDLE=b'      - name: Run protected-Test authenticated functional UAT\n'
FINISH=b'      - name: Finalize private Pulse activation after full application acceptance\n'
END=b'      - name: Restore exact prior Test images after application failure\n'
OLD_ROLLBACK=b"&& steps.uat.outputs.deployment_health_verified != 'true' && steps.psa_live_uat.outputs.deployment_health_verified != 'true' && steps.sow_role_uat.outputs.deployment_health_verified != 'true') }}"
POST_ACCEPTANCE_ROLLBACK=b"&& ((steps.uat.outputs.deployment_health_verified != 'true' && steps.psa_live_uat.outputs.deployment_health_verified != 'true' && steps.sow_role_uat.outputs.deployment_health_verified != 'true') || steps.pulse_private_services_final.outcome == 'failure')) }}"
PREREQUISITE_MIGRATION_STEP=b'          PROJECTPULSE_RELEASE_ROOT="$PWD" bash "$GITHUB_WORKSPACE/control/scripts/release-test/build-and-run-celar-ai-private-runtime-migrations.sh"\n'
PREREQUISITE_PATH_MIGRATION_STEP=b'          PROJECTPULSE_RELEASE_ROOT="$PWD" bash scripts/release-test/build-and-run-celar-ai-private-runtime-migrations.sh\n'
PREREQUISITE_EVIDENCE=b'migrations:["080_celar_ai_internal_data_intelligence","081_celar_ai_private_runtime_activation",'
CURRENT_EVIDENCE=b'migrations:['
STALE_SOW_INPUTS=b"      stale_sow_cleanup_mode:\n        description: 'Optional post-UAT stale SOW maintenance in Protected Test'\n        required: false\n        default: none\n        type: choice\n        options:\n          - none\n          - dry-run\n          - apply\n      stale_sow_cleanup_confirmation:\n        description: 'Apply only: type DELETE STALE TEST SOWS'\n        required: false\n        default: ''\n        type: string\n"
STALE_SOW_VALIDATION=b'\n      - name: Validate stale SOW maintenance request\n        shell: bash\n        env:\n          STALE_SOW_MODE: ${{ inputs.stale_sow_cleanup_mode || \'none\' }}\n          STALE_SOW_CONFIRMATION: ${{ inputs.stale_sow_cleanup_confirmation }}\n        run: |\n          set -Eeuo pipefail\n          case "$STALE_SOW_MODE" in\n            none|dry-run|apply) ;;\n            *) echo \'Unsupported stale SOW maintenance mode.\' >&2; exit 1 ;;\n          esac\n          if [[ "$STALE_SOW_MODE" != none ]]; then\n            [[ "${{ inputs.qualification_provider || \'none\' }}" == none ]]\n            [[ "$TARGET_RELEASE_BRANCH" == main ]]\n            [[ "$ACCEPTANCE_SCOPE" == full ]]\n          fi\n          if [[ "$STALE_SOW_MODE" == apply ]]; then\n            [[ "$STALE_SOW_CONFIRMATION" == \'DELETE STALE TEST SOWS\' ]]\n          fi\n'
STALE_SOW_EXECUTION=b'      - name: Evaluate or clean stale Work Register SOWs\n        id: stale_sow_maintenance\n        if: ${{ success() && inputs.release_branch == \'main\' && inputs.acceptance_scope == \'full\' && (inputs.stale_sow_cleanup_mode || \'none\') != \'none\' && steps.pulse_private_services_final.outcome == \'success\' }}\n        shell: bash\n        working-directory: release\n        env:\n          PROJECTPULSE_M087_PASSWORD: ${{ secrets.PROJECTPULSE_M087_PASSWORD }}\n          PROJECTPULSE_STALE_SOW_MODE: ${{ inputs.stale_sow_cleanup_mode }}\n          PROJECTPULSE_STALE_SOW_CONFIRMATION: ${{ inputs.stale_sow_cleanup_confirmation }}\n          PROJECTPULSE_STALE_SOW_REPORT: ${{ runner.temp }}/stale-sow-cleanup-summary.json\n        run: |\n          set -Eeuo pipefail\n          python3 scripts/release-test/cleanup-stale-sows-protected-test.py\n          jq \'{environment,origin,mode,summary,projects}\' "$PROJECTPULSE_STALE_SOW_REPORT"\n\n      - name: Upload stale SOW maintenance evidence\n        if: ${{ always() && inputs.release_branch == \'main\' && inputs.acceptance_scope == \'full\' && (inputs.stale_sow_cleanup_mode || \'none\') != \'none\' }}\n        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02\n        with:\n          name: protected-test-stale-sow-maintenance-${{ github.run_id }}-${{ github.run_attempt }}\n          path: ${{ runner.temp }}/stale-sow-cleanup-summary.json\n          if-no-files-found: error\n          retention-days: 30\n\n'
def normalize(data):
    if isinstance(data,str):data=data.encode()
    digest=hashlib.sha256(data).hexdigest()
    if digest==BASE_SHA256:return data
    if digest==STALE_SOW_MAINTENANCE_SHA256:
        for block in (STALE_SOW_INPUTS,STALE_SOW_VALIDATION,STALE_SOW_EXECUTION):
            assert data.count(block)==1,'Stale-SOW maintenance controller block changed'
            data=data.replace(block,b'',1)
        digest=hashlib.sha256(data).hexdigest()
        assert digest==PREREQUISITE_PATH_SHA256,'Stale-SOW maintenance projection changed'
    if digest in (PREREQUISITE_SHA256,PREREQUISITE_PATH_SHA256):
        step=PREREQUISITE_MIGRATION_STEP if digest==PREREQUISITE_SHA256 else PREREQUISITE_PATH_MIGRATION_STEP
        assert data.count(step)==1,'Private-runtime migration invocation changed'
        assert data.count(PREREQUISITE_EVIDENCE)==1,'Private-runtime migration evidence changed'
        data=data.replace(step,b'',1)
        data=data.replace(PREREQUISITE_EVIDENCE,CURRENT_EVIDENCE,1)
        digest=hashlib.sha256(data).hexdigest()
        assert digest==CURRENT_SHA256,'Private-runtime prerequisite projection changed'
    assert digest in (LEGACY_SHA256,CURRENT_SHA256),'Unregistered controller content'
    assert data.count(START)==1 and data.count(END)==1,'Controller insertion boundary changed'
    lo=data.index(START)
    if digest==LEGACY_SHA256:
        hi=data.index(END,lo)
        assert hashlib.sha256(data[lo:hi]).hexdigest()==BLOCK_SHA256,'Legacy insertion changed'
        parent=data[:lo]+data[hi:]
    else:
        assert data.count(MIDDLE)==1 and data.count(FINISH)==1,'Post-activation acceptance boundary changed'
        mid=data.index(MIDDLE,lo);tail=data.index(FINISH,mid);end=data.index(END,tail)
        assert hashlib.sha256(data[lo:mid]).hexdigest()==PRE_BLOCK_SHA256,'Service preparation changed'
        assert hashlib.sha256(data[tail:end]).hexdigest()==POST_BLOCK_SHA256,'Acceptance finalization changed'
        parent=data[:lo]+data[mid:tail]+data[end:]
        assert parent.count(POST_ACCEPTANCE_ROLLBACK)==1,'Mandatory post-activation rollback changed'
        parent=parent.replace(POST_ACCEPTANCE_ROLLBACK,OLD_ROLLBACK,1)
    assert hashlib.sha256(parent).hexdigest()==BASE_SHA256,'Existing execution or authorization changed'
    return parent
