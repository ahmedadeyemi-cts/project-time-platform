"""Exact reviewed service-order projection; never grants deployment authority.
All existing UAT remains mandatory and runs against the activated services.
"""
import hashlib
CORE_ONLY_SHA256='ad2e0cd771591b6ee5da99bb78e2c5a978c8b6950c840afc5afa6c7bb6bfc232'
BASE_SHA256='6c587203f890a5525e041c750fc5076b688db81aa5c51345d0a659706a7449ff'
LEGACY_SHA256='11a5cbc14270c720518a37a5bcd539bac650d8c49b374f6fb155b901147c74d3'
CURRENT_SHA256='6c7d701fc02599626f5fcfb638f4f5d4809e27102dbeb2085ac5c7f81532ca62'
PREREQUISITE_SHA256='941c13231b28d0fa40e0e72af02a079c31f4a6b6a78c27e2af156bf93ee2eae6'
PREREQUISITE_PATH_SHA256='b1583d34382fc774c9b40ac5b145f2a5f7154ab06e48203310a1f402e262fdef'
STALE_SOW_MAINTENANCE_SHA256='267e6105d955f372617ac9376ff5bb4ae16e6089fb0490ea6feaead7d3b4b71b'
FINANCE_BILLING_SHA256='9f96b44aeabff2055c2630071b4c63d7ce9d2eb61e630a237634f251700ab122'
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
FINANCE_BILLING_REPLACEMENTS=(
    (b'migrations:["086_module_066_flowhive_enterprise_pm","088_systemwide_enterprise_reliability","093_assigned_work_canonical_visibility_repair","094_flowhive_canonical_sow_authority","095_project_planning_collaboration_access","096_project_planning_document_authority","097_project_planning_identity_safe_admission","115_module_066_task_notifications","121_flowhive_sequential_phase_checkpoints","122_flowhive_automatic_first_draft","123_module064_external_generation_approval","124_module025_service_scope","125_automatic_document_admission_laya","127_flowhive_pm_automatic_planning_defaults","133_finance_billing_handoff_notifications","134_customer_billing_notification_profiles"]', b'migrations:["086_module_066_flowhive_enterprise_pm","088_systemwide_enterprise_reliability","093_assigned_work_canonical_visibility_repair","094_flowhive_canonical_sow_authority","095_project_planning_collaboration_access","096_project_planning_document_authority","097_project_planning_identity_safe_admission","115_module_066_task_notifications","121_flowhive_sequential_phase_checkpoints","122_flowhive_automatic_first_draft","123_module064_external_generation_approval","124_module025_service_scope","125_automatic_document_admission_laya","127_flowhive_pm_automatic_planning_defaults"]'),
    (b'          install -m 0644 database/migrations/133_finance_billing_handoff_notifications.sql "$MIGRATION_CONTEXT/database/migrations/133_finance_billing_handoff_notifications.sql"\n          install -m 0644 database/migrations/134_customer_billing_notification_profiles.sql "$MIGRATION_CONTEXT/database/migrations/134_customer_billing_notification_profiles.sql"\n', b''),
    (b'            "$ROOT/database/migrations/114_module064_private_admin_override.sql" \\\n            "$ROOT/database/migrations/133_finance_billing_handoff_notifications.sql" \\\n            "$ROOT/database/migrations/134_customer_billing_notification_profiles.sql"; do', b'            "$ROOT/database/migrations/114_module064_private_admin_override.sql"; do'),
    (b"(SELECT count(*) FROM schema_migrations WHERE migration_id IN ('112_optional_ai_providers','113_module065_teams_notifications','114_module064_private_admin_override','133_finance_billing_handoff_notifications','134_customer_billing_notification_profiles')) = 5", b"(SELECT count(*) FROM schema_migrations WHERE migration_id IN ('112_optional_ai_providers','113_module065_teams_notifications','114_module064_private_admin_override')) = 3"),
    (b"            AND EXISTS(\n              SELECT 1 FROM enterprise_notification_policies\n              WHERE policy_code='CLOSEOUT_STARTED'\n                AND recipient_strategy='billing_project_team'\n                AND subject_template LIKE 'ProjectPulse: Ready for billing%'\n            )\n            AND EXISTS(\n              SELECT 1 FROM enterprise_notification_policies\n              WHERE policy_code='CLOSEOUT_COMPLETED'\n                AND recipient_strategy='billing_project_team'\n            )\n            AND to_regclass('public.customer_billing_notification_profiles') IS NOT NULL\n            AND to_regclass('public.customer_billing_notification_profile_audit') IS NOT NULL\n            AND EXISTS(\n              SELECT 1 FROM enterprise_notification_policies\n              WHERE policy_code='CUSTOMER_INVOICE_WORKFLOW_ACTION_REQUIRED'\n                AND recipient_strategy='billing_project_team'\n                AND producer_contract='module_021_042_customer_billing_profile_v1'\n            )\n", b''),
    (b"          echo 'MIGRATIONS_112_113_114_133_134=APPLIED_AND_VERIFIED'", b"          echo 'MIGRATIONS_112_113_114=APPLIED_AND_VERIFIED'"),
    (b'migrations:["080_celar_ai_internal_data_intelligence","081_celar_ai_private_runtime_activation","086_module_066_flowhive_enterprise_pm","088_systemwide_enterprise_reliability","093_assigned_work_canonical_visibility_repair","094_flowhive_canonical_sow_authority","095_project_planning_collaboration_access","096_project_planning_document_authority","097_project_planning_identity_safe_admission","098_module_management_owner_storage_reconciliation","098_customer_directory_source_authority","099_module025_sow_gsd_workspace","100_module001b_catalog_ownership_reconciliation","109_module025_project_name","115_module_066_task_notifications","121_flowhive_sequential_phase_checkpoints","122_flowhive_automatic_first_draft","123_module064_external_generation_approval","124_module025_service_scope","125_automatic_document_admission_laya","127_flowhive_pm_automatic_planning_defaults","133_finance_billing_handoff_notifications","134_customer_billing_notification_profiles"]', b'migrations:["080_celar_ai_internal_data_intelligence","081_celar_ai_private_runtime_activation","086_module_066_flowhive_enterprise_pm","088_systemwide_enterprise_reliability","093_assigned_work_canonical_visibility_repair","094_flowhive_canonical_sow_authority","095_project_planning_collaboration_access","096_project_planning_document_authority","097_project_planning_identity_safe_admission","098_module_management_owner_storage_reconciliation","098_customer_directory_source_authority","099_module025_sow_gsd_workspace","100_module001b_catalog_ownership_reconciliation","109_module025_project_name","115_module_066_task_notifications","121_flowhive_sequential_phase_checkpoints","122_flowhive_automatic_first_draft","123_module064_external_generation_approval","124_module025_service_scope","125_automatic_document_admission_laya","127_flowhive_pm_automatic_planning_defaults"]'),
)
STALE_SOW_EXECUTION=b'      - name: Evaluate or clean stale Work Register SOWs\n        id: stale_sow_maintenance\n        if: ${{ success() && inputs.release_branch == \'main\' && inputs.acceptance_scope == \'full\' && (inputs.stale_sow_cleanup_mode || \'none\') != \'none\' && steps.pulse_private_services_final.outcome == \'success\' }}\n        shell: bash\n        working-directory: release\n        env:\n          PROJECTPULSE_M087_PASSWORD: ${{ secrets.PROJECTPULSE_M087_PASSWORD }}\n          PROJECTPULSE_STALE_SOW_MODE: ${{ inputs.stale_sow_cleanup_mode }}\n          PROJECTPULSE_STALE_SOW_CONFIRMATION: ${{ inputs.stale_sow_cleanup_confirmation }}\n          PROJECTPULSE_STALE_SOW_REPORT: ${{ runner.temp }}/stale-sow-cleanup-summary.json\n        run: |\n          set -Eeuo pipefail\n          python3 scripts/release-test/cleanup-stale-sows-protected-test.py\n          jq \'{environment,origin,mode,summary,projects}\' "$PROJECTPULSE_STALE_SOW_REPORT"\n\n      - name: Upload stale SOW maintenance evidence\n        if: ${{ always() && inputs.release_branch == \'main\' && inputs.acceptance_scope == \'full\' && (inputs.stale_sow_cleanup_mode || \'none\') != \'none\' }}\n        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02\n        with:\n          name: protected-test-stale-sow-maintenance-${{ github.run_id }}-${{ github.run_attempt }}\n          path: ${{ runner.temp }}/stale-sow-cleanup-summary.json\n          if-no-files-found: error\n          retention-days: 30\n\n'
def normalize(data):
    if isinstance(data,str):data=data.encode()
    digest=hashlib.sha256(data).hexdigest()
    if digest==CORE_ONLY_SHA256:
        import importlib.util
        from pathlib import Path
        spec=importlib.util.spec_from_file_location('core_projection',Path(__file__).resolve().parents[1]/'core_test_controller_projection.py')
        module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
        data=module.normalize(data)
        digest=hashlib.sha256(data).hexdigest()
    if digest==BASE_SHA256:return data
    if digest==FINANCE_BILLING_SHA256:
        for finance_current, finance_prior in FINANCE_BILLING_REPLACEMENTS:
            assert data.count(finance_current)==1,'Finance billing controller projection changed'
            data=data.replace(finance_current,finance_prior,1)
        digest=hashlib.sha256(data).hexdigest()
        assert digest==STALE_SOW_MAINTENANCE_SHA256,'Finance billing controller predecessor changed'
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
