# syntax=docker/dockerfile:1

FROM node:24-alpine AS build

# Preserve the repository-relative path used by the frontend validation
# scripts while copying the governed source paths they inspect. The final
# build-stage COPY below also supplies the complete non-secret context for
# newly integrated modules without changing the static Nginx runtime image.
WORKDIR /workspace

COPY src/frontend/project-time-web/package.json \
     src/frontend/project-time-web/package-lock.json \
     src/frontend/project-time-web/

WORKDIR /workspace/src/frontend/project-time-web

RUN npm ci --no-audit --no-fund

WORKDIR /workspace

COPY src/frontend/project-time-web/ \
     src/frontend/project-time-web/

COPY .github/ISSUE_TEMPLATE/projectpulse-defect.yml \
     .github/ISSUE_TEMPLATE/projectpulse-defect.yml

COPY src/backend/ProjectTime.Api/Program.cs \
     src/backend/ProjectTime.Api/Program.cs

COPY src/backend/ProjectTime.Api/Directory.Build.targets \
     src/backend/ProjectTime.Api/Directory.Build.targets

COPY src/backend/ProjectTime.Api/Modules/IdentityProfileModule.cs \
     src/backend/ProjectTime.Api/Modules/IdentityProfileModule.cs

COPY src/backend/ProjectTime.Api/Modules/ProjectWorkspaceModule.cs \
     src/backend/ProjectTime.Api/Modules/ProjectWorkspaceModule.cs

COPY src/backend/ProjectTime.Api/Modules/ApprovalCenterModule.cs \
     src/backend/ProjectTime.Api/Modules/ApprovalCenterModule.cs

COPY src/backend/ProjectTime.Api/Ai/ \
     src/backend/ProjectTime.Api/Ai/

COPY src/backend/ProjectTime.Api/ProjectPulseAiTimeEntrySuggestionService.cs \
     src/backend/ProjectTime.Api/ProjectPulseAiTimeEntrySuggestionService.cs

COPY src/backend/ProjectTime.Api/Modules/Module001MultiTimerModule.cs \
     src/backend/ProjectTime.Api/Modules/Module001MultiTimerFinalization.cs \
     src/backend/ProjectTime.Api/Modules/Module001MultiTimerStart.cs \
     src/backend/ProjectTime.Api/Modules/Module001MultiTimerStop.cs \
     src/backend/ProjectTime.Api/Modules/

COPY src/backend/ProjectTime.Api/Modules/AiProviderConfigurationModule.cs \
     src/backend/ProjectTime.Api/Modules/AiProviderConfigurationModule.cs

COPY src/backend/ProjectTime.Api/Modules/CelarAiBrandModule.cs \
     src/backend/ProjectTime.Api/Modules/CelarAiCapabilityRoutingModule.cs \
     src/backend/ProjectTime.Api/Modules/CelarAiEnterprisePlatformModule.cs \
     src/backend/ProjectTime.Api/Modules/PulseAiSystemIntelligenceModule.cs \
     src/backend/ProjectTime.Api/Modules/

COPY src/backend/ProjectTime.Api/Modules/EnterpriseNotificationContracts.cs \
     src/backend/ProjectTime.Api/Modules/EnterpriseNotificationOrchestrationService.cs \
     src/backend/ProjectTime.Api/Modules/EnterpriseNotificationRecipientResolver.cs \
     src/backend/ProjectTime.Api/Modules/EnterpriseNotificationRepository.cs \
     src/backend/ProjectTime.Api/Modules/EnterpriseNotificationSourceScanner.cs \
     src/backend/ProjectTime.Api/Modules/EnterpriseNotificationTemplateRenderer.cs \
     src/backend/ProjectTime.Api/Modules/

COPY src/backend/ProjectTime.Api/Modules/ModuleAvailabilityOverridesModule.cs \
     src/backend/ProjectTime.Api/Modules/PermanentRoleAuthorityCompatibility.cs \
     src/backend/ProjectTime.Api/Modules/ProjectPulseActualSessionAuthority.cs \
     src/backend/ProjectTime.Api/Modules/QualificationsCertificationSelfServiceModule.cs \
     src/backend/ProjectTime.Api/Modules/RoleAccessAuditModule.cs \
     src/backend/ProjectTime.Api/Modules/

COPY src/backend/ProjectTime.Api/Modules/EntraSecretAdministrationModule.cs \
     src/backend/ProjectTime.Api/Modules/EntraSecretAdministrationModule.cs

COPY src/backend/ProjectTime.Api/Modules/EntraSecretRotationContracts.cs \
     src/backend/ProjectTime.Api/Modules/EntraSecretRotationContracts.cs

COPY src/backend/ProjectTime.Api/Modules/ProjectFlowHiveModule.cs \
     src/backend/ProjectTime.Api/Modules/ProjectFlowHiveModule.cs

COPY src/backend/ProjectTime.Api/Modules/ProjectFlowHivePlanningContracts.cs \
     src/backend/ProjectTime.Api/Modules/ProjectFlowHivePlanningContracts.cs

COPY src/backend/ProjectTime.Api/Modules/ProjectFlowHiveScheduleEngine.cs \
     src/backend/ProjectTime.Api/Modules/ProjectFlowHiveScheduleEngine.cs

COPY src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiRequestFactory.cs \
     src/backend/ProjectTime.Api/Modules/ProjectFlowHiveAiRequestFactory.cs

COPY src/backend/ProjectTime.Api/Modules/ProjectFlowHiveBrandAssets.cs \
     src/backend/ProjectTime.Api/Modules/ProjectFlowHiveBrandAssets.cs

COPY src/backend/ProjectTime.Api/Modules/ProjectFlowHiveArtifactRenderer.cs \
     src/backend/ProjectTime.Api/Modules/ProjectFlowHiveArtifactRenderer.cs

COPY src/backend/ProjectTime.Api/Modules/SecurityOperationsResponseModule.cs \
     src/backend/ProjectTime.Api/Modules/SecurityOperationsResponseModule.cs

COPY src/backend/ProjectTime.Api/Modules/SecurityDiagnosticsOperations.cs \
     src/backend/ProjectTime.Api/Modules/SecurityDiagnosticsOperations.cs

COPY src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs \
     src/backend/ProjectTime.Api/Modules/MicrosoftIntegrationSecurityCompatibility.cs \
     src/backend/ProjectTime.Api/Modules/MicrosoftIntegrationModule.cs \
     src/backend/ProjectTime.Api/Modules/MicrosoftSsoConnectionProfilesModule.cs \
     src/backend/ProjectTime.Api/Modules/MicrosoftSsoInteractiveStartActivation.cs \
     src/backend/ProjectTime.Api/Modules/MicrosoftEnvironmentRuntimeResolver.cs \
     src/backend/ProjectTime.Api/Modules/MicrosoftMailRuntimeConfigurationModule.cs \
     src/backend/ProjectTime.Api/Modules/MicrosoftMailTransportTestModule.cs \
     src/backend/ProjectTime.Api/Modules/MicrosoftSmtpCredentialProjectionCompatibility.cs \
     src/backend/ProjectTime.Api/Modules/MicrosoftSsoRuntimeCompatibility.cs \
     src/backend/ProjectTime.Api/Modules/MicrosoftServicesRuntimeCompatibility.cs \
     src/backend/ProjectTime.Api/Modules/MicrosoftDirectorySyncModule.cs \
     src/backend/ProjectTime.Api/Modules/AzureDirectoryImportModule.cs \
     src/backend/ProjectTime.Api/Modules/ProjectPulsePublicOriginCompatibility.cs \
     src/backend/ProjectTime.Api/Modules/ModuleAvailabilityReadContinuityCompatibility.cs \
     src/backend/ProjectTime.Api/Modules/

COPY src/backend/ProjectTime.Api/Modules/SystemArchitectureModule.cs \
     src/backend/ProjectTime.Api/Modules/SystemArchitectureModule.cs

COPY src/backend/ProjectTime.Api/Modules/QualificationsCertificationModule.cs \
     src/backend/ProjectTime.Api/Modules/QualificationsCertificationModule.cs

COPY src/backend/ProjectTime.Api/Modules/CapacityPipelineForecastModule.cs \
     src/backend/ProjectTime.Api/Modules/CapacityPipelineForecastModule.cs

COPY src/backend/ProjectTime.Api/Modules/OnCallSchedulingModule.cs \
     src/backend/ProjectTime.Api/Modules/OnCallSchedulingModule.cs

COPY src/backend/ProjectTime.Api/Modules/OneAssistRoutingDirectoryModule.cs \
     src/backend/ProjectTime.Api/Modules/OneAssistRoutingDirectoryModule.cs

COPY src/backend/ProjectTime.Api/Modules/Module071072NativePersistence.cs \
     src/backend/ProjectTime.Api/Modules/Module071072NativePersistence.cs

COPY src/backend/ProjectTime.Api/Modules/Module064074NativeAdministration.cs \
     src/backend/ProjectTime.Api/Modules/Module064074NativeAdministration.cs

COPY src/backend/ProjectTime.Api/Modules/SalesCoverageAlignmentModule.cs \
     src/backend/ProjectTime.Api/Modules/SalesCoverageAlignmentModule.cs

COPY src/backend/ProjectTime.Api/Modules/OemVendorDirectoryModule.cs \
     src/backend/ProjectTime.Api/Modules/OemVendorDirectoryModule.cs

COPY src/backend/ProjectTime.Api/Modules/DefectTrackerModule.cs \
     src/backend/ProjectTime.Api/Modules/DefectTrackerModule.cs

COPY src/backend/ProjectTime.Api/Modules/CrmErpIntegrationModule.cs \
     src/backend/ProjectTime.Api/Modules/CrmErpIntegrationModule.cs

COPY src/backend/ProjectTime.Api/Modules/CrmErpAdministrationExperience.cs \
     src/backend/ProjectTime.Api/Modules/CrmErpAdministrationExperience.cs

COPY src/backend/ProjectTime.Api/Modules/ScopedRolePolicyAuthorizationBridge.cs \
     src/backend/ProjectTime.Api/Modules/ScopedRolePolicyAuthorizationBridge.cs

COPY src/backend/ProjectTime.Api/Modules/WorkRegisterAuthorization.cs \
     src/backend/ProjectTime.Api/Modules/WorkRegisterAuthorization.cs

COPY src/backend/ProjectTime.Api/Modules/WorkRegisterSellImportModule.cs \
     src/backend/ProjectTime.Api/Modules/WorkRegisterSellImportModule.cs

COPY src/backend/ProjectTime.Api/Modules/WorkRegisterPurchaseOrderModule.cs \
     src/backend/ProjectTime.Api/Modules/WorkRegisterPurchaseOrderModule.cs

COPY src/backend/ProjectTime.Api/Modules/WorkLifecycleModule.cs \
     src/backend/ProjectTime.Api/Modules/WorkLifecycleModule.cs

COPY src/backend/ProjectTime.Api/Modules/InvoiceBillingModule.cs \
     src/backend/ProjectTime.Api/Modules/InvoiceBillingModule.cs

COPY src/backend/ProjectTime.Api/Modules/CertiniaBillingModule.cs \
     src/backend/ProjectTime.Api/Modules/CertiniaBillingModule.cs

COPY src/backend/ProjectTime.Api/Modules/InvoiceArtifactBrandingRenderer.cs \
     src/backend/ProjectTime.Api/Modules/InvoiceArtifactBrandingRenderer.cs

COPY src/backend/ProjectTime.Api/Modules/InvoiceBrandingAssets.cs \
     src/backend/ProjectTime.Api/Modules/InvoiceBrandingAssets.cs

COPY src/backend/ProjectTime.Api/ProjectTime.Api.csproj \
     src/backend/ProjectTime.Api/ProjectTime.Api.csproj

COPY src/backend/ProjectTime.Api/Assets/Branding/USSNavyStacked.png \
     src/backend/ProjectTime.Api/Assets/Branding/USSNavyStacked.jpg \
     src/backend/ProjectTime.Api/Assets/Branding/

COPY src/backend/ProjectTime.Api/Modules/GovernedOperationsReadModule.cs \
     src/backend/ProjectTime.Api/Modules/GovernedOperationsReadModule.cs

COPY src/backend/ProjectTime.Api/Modules/IntegrationEventGatewayModule.cs \
     src/backend/ProjectTime.Api/Modules/IntegrationEventGatewayModule.cs

COPY src/backend/ProjectTime.Api/Modules/ReleaseDeploymentControlModule.cs \
     src/backend/ProjectTime.Api/Modules/ReleaseDeploymentControlModule.cs

COPY src/backend/ProjectTime.Api/Modules/ObservabilitySloHealthModule.cs \
     src/backend/ProjectTime.Api/Modules/ObservabilitySloHealthModule.cs

COPY src/backend/ProjectTime.Api/Modules/DataGovernanceRetentionModule.cs \
     src/backend/ProjectTime.Api/Modules/DataGovernanceRetentionModule.cs

COPY src/backend/ProjectTime.Api/Modules/CustomerDeliveryAcceptanceModule.cs \
     src/backend/ProjectTime.Api/Modules/CustomerDeliveryAcceptanceModule.cs

COPY src/backend/ProjectTime.Api/Modules/SystemDiagnosticRemediationModule.cs \
     src/backend/ProjectTime.Api/Modules/SystemDiagnosticRemediationModule.cs

COPY docs/modules/module-011-pulse-ai/ \
     docs/modules/module-011-pulse-ai/

COPY docs/modules/module-062-unified-identity-profile/README.md \
     docs/modules/module-062-unified-identity-profile/README.md

COPY docs/modules/module-064-ai-provider-configuration/ \
     docs/modules/module-064-ai-provider-configuration/

COPY docs/modules/module-065-entra-secret-administration/ \
     docs/modules/module-065-entra-secret-administration/

COPY docs/modules/module-066-project-flowhive/ \
     docs/modules/module-066-project-flowhive/

COPY docs/modules/module-067-global-mail/ \
     docs/modules/module-067-global-mail/

COPY docs/modules/module-068-system-architecture/ \
     docs/modules/module-068-system-architecture/

COPY docs/modules/module-069-qualifications-certifications/ \
     docs/modules/module-069-qualifications-certifications/

COPY docs/modules/module-070-capacity-pipeline-forecasting/ \
     docs/modules/module-070-capacity-pipeline-forecasting/

COPY docs/modules/module-071-oncall-scheduling/ \
     docs/modules/module-071-oncall-scheduling/

COPY docs/modules/module-072-oneassist-routing-directory/ \
     docs/modules/module-072-oneassist-routing-directory/

COPY docs/modules/module-073-sales-coverage-alignment/ \
     docs/modules/module-073-sales-coverage-alignment/

COPY docs/modules/module-074-oem-vendor-directory/ \
     docs/modules/module-074-oem-vendor-directory/

COPY docs/modules/module-075-integration-event-gateway/ \
     docs/modules/module-075-integration-event-gateway/

COPY docs/modules/module-076-defect-tracker/ \
     docs/modules/module-076-defect-tracker/

COPY docs/modules/module-026-crm-erp-integrations/ \
     docs/modules/module-026-crm-erp-integrations/

COPY docs/modules/module-055c-edit-work-register/ \
     docs/modules/module-055c-edit-work-register/

COPY docs/modules/module-055d-create-work-register/ \
     docs/modules/module-055d-create-work-register/

COPY database/migrations/031_modules_071_072_native_persistence.sql \
     database/migrations/031_modules_071_072_native_persistence.sql
COPY database/rollback/031_modules_071_072_native_persistence_rollback.sql \
     database/rollback/031_modules_071_072_native_persistence_rollback.sql
COPY database/migrations/032_projectpulse_native_administration_documents.sql \
     database/migrations/032_projectpulse_native_administration_documents.sql
COPY database/rollback/032_projectpulse_native_administration_documents_rollback.sql \
     database/rollback/032_projectpulse_native_administration_documents_rollback.sql
COPY database/migrations/033_security_diagnostics_native_operations.sql \
     database/migrations/033_security_diagnostics_native_operations.sql
COPY database/rollback/033_security_diagnostics_native_operations_rollback.sql \
     database/rollback/033_security_diagnostics_native_operations_rollback.sql
COPY database/migrations/034_module_026_crm_erp_integrations.sql \
     database/migrations/034_module_026_crm_erp_integrations.sql
COPY database/rollback/034_module_026_crm_erp_integrations_rollback.sql \
     database/rollback/034_module_026_crm_erp_integrations_rollback.sql
COPY database/migrations/035_work_register_055c_055d_split.sql \
     database/migrations/035_work_register_055c_055d_split.sql
COPY database/rollback/035_work_register_055c_055d_split_rollback.sql \
     database/rollback/035_work_register_055c_055d_split_rollback.sql
COPY database/migrations/036_work_register_role_scope_and_closeout_handoff.sql \
     database/migrations/036_work_register_role_scope_and_closeout_handoff.sql
COPY database/rollback/036_work_register_role_scope_and_closeout_handoff_rollback.sql \
     database/rollback/036_work_register_role_scope_and_closeout_handoff_rollback.sql
COPY database/migrations/037_work_register_dates_and_contract_types.sql \
     database/migrations/037_work_register_dates_and_contract_types.sql
COPY database/rollback/037_work_register_dates_and_contract_types_rollback.sql \
     database/rollback/037_work_register_dates_and_contract_types_rollback.sql
COPY database/migrations/038_work_to_cash_lifecycle_and_audit.sql \
     database/migrations/038_work_to_cash_lifecycle_and_audit.sql
COPY database/rollback/038_work_to_cash_lifecycle_and_audit_rollback.sql \
     database/rollback/038_work_to_cash_lifecycle_and_audit_rollback.sql
COPY database/migrations/039_work_to_cash_reactivation_lock_order.sql \
     database/migrations/039_work_to_cash_reactivation_lock_order.sql
COPY database/rollback/039_work_to_cash_reactivation_lock_order_rollback.sql \
     database/rollback/039_work_to_cash_reactivation_lock_order_rollback.sql

COPY database/migrations/052_document_intelligence_runtime.sql \
     database/migrations/052_document_intelligence_runtime.sql
COPY database/rollback/052_document_intelligence_runtime_rollback.sql \
     database/rollback/052_document_intelligence_runtime_rollback.sql
COPY database/migrations/053_intelligence_answer_orchestration.sql \
     database/migrations/053_intelligence_answer_orchestration.sql
COPY database/rollback/053_intelligence_answer_orchestration_rollback.sql \
     database/rollback/053_intelligence_answer_orchestration_rollback.sql
COPY database/migrations/054_pulse_ai_system_intelligence_conversations.sql \
     database/migrations/054_pulse_ai_system_intelligence_conversations.sql
COPY database/rollback/054_pulse_ai_system_intelligence_conversations_rollback.sql \
     database/rollback/054_pulse_ai_system_intelligence_conversations_rollback.sql
COPY database/migrations/055_analytics_center.sql \
     database/migrations/055_analytics_center.sql
COPY database/rollback/055_analytics_center_rollback.sql \
     database/rollback/055_analytics_center_rollback.sql
COPY database/migrations/057_module_001_multi_timer_document_grounded_ai.sql \
     database/migrations/057_module_001_multi_timer_document_grounded_ai.sql
COPY database/rollback/057_module_001_multi_timer_document_grounded_ai_rollback.sql \
     database/rollback/057_module_001_multi_timer_document_grounded_ai_rollback.sql
COPY database/migrations/060_analytics_center_enterprise_experience.sql \
     database/migrations/060_analytics_center_enterprise_experience.sql
COPY database/rollback/060_analytics_center_enterprise_experience_rollback.sql \
     database/rollback/060_analytics_center_enterprise_experience_rollback.sql
COPY database/migrations/061_celar_ai_capability_routing.sql \
     database/migrations/061_celar_ai_capability_routing.sql
COPY database/rollback/061_celar_ai_capability_routing_rollback.sql \
     database/rollback/061_celar_ai_capability_routing_rollback.sql
COPY database/migrations/062_super_administrator_permanent_full_control.sql \
     database/migrations/062_super_administrator_permanent_full_control.sql
COPY database/rollback/062_super_administrator_permanent_full_control_rollback.sql \
     database/rollback/062_super_administrator_permanent_full_control_rollback.sql
COPY database/migrations/063_project_management_billing_role_access_repair.sql \
     database/migrations/063_project_management_billing_role_access_repair.sql
COPY database/rollback/063_project_management_billing_role_access_repair_rollback.sql \
     database/rollback/063_project_management_billing_role_access_repair_rollback.sql
COPY database/migrations/064_module_065_enterprise_notification_orchestration.sql \
     database/migrations/064_module_065_enterprise_notification_orchestration.sql
COPY database/rollback/064_module_065_enterprise_notification_orchestration_rollback.sql \
     database/rollback/064_module_065_enterprise_notification_orchestration_rollback.sql

COPY tests/ \
     tests/

COPY scripts/module-066-validation/ \
     scripts/module-066-validation/

COPY docs/modules/module-077-release-deployment-control/ \
     docs/modules/module-077-release-deployment-control/
COPY docs/modules/module-078-observability-slo-health/ \
     docs/modules/module-078-observability-slo-health/
COPY docs/modules/module-079-data-governance-retention/ \
     docs/modules/module-079-data-governance-retention/
COPY docs/modules/module-080-customer-delivery-acceptance/ \
     docs/modules/module-080-customer-delivery-acceptance/
COPY docs/modules/module-997-security-operations-response/ \
     docs/modules/module-997-security-operations-response/
COPY docs/modules/module-998-system-diagnostic-remediation/ \
     docs/modules/module-998-system-diagnostic-remediation/
COPY docs/modules/modules-064-074-native-administration/ \
     docs/modules/modules-064-074-native-administration/
COPY docs/MODULE-CATALOG.md \
     docs/MODULE-CATALOG.md
COPY docs/MODULE-WORK-REGISTER.md \
     docs/MODULE-WORK-REGISTER.md
COPY docs/production-readiness/AUGUST_PRODUCTION_READINESS_TRACKER.md \
     docs/production-readiness/AUGUST_PRODUCTION_READINESS_TRACKER.md
COPY docs/releases/2026-08-02-consolidated-enterprise-validation.md \
     docs/releases/2026-08-02-consolidated-enterprise-validation.md

COPY deployment/containers/web/Dockerfile \
     deployment/containers/web/Dockerfile

# Supply every non-secret, non-ignored source artifact to this disposable
# validation/build stage. The explicit COPY records above remain as governed
# evidence for existing validators; the final runtime image still receives
# only the compiled frontend assets.
COPY . /workspace/

WORKDIR /workspace/src/frontend/project-time-web

RUN npm run build


FROM nginxinc/nginx-unprivileged:1.31.3-alpine AS runtime

ENV API_UPSTREAM=http://127.0.0.1:5080 \
    NGINX_ENVSUBST_FILTER="^(API_UPSTREAM|NGINX_RESOLVER)$"

USER root

COPY --from=build \
     /workspace/src/frontend/project-time-web/dist/ \
     /usr/share/nginx/html/

COPY deployment/containers/web/default.conf.template \
     /etc/nginx/templates/default.conf.template

COPY deployment/containers/web/projecttime-web-entrypoint.sh \
     /usr/local/bin/projecttime-web-entrypoint.sh

RUN chmod 0644 /etc/nginx/templates/default.conf.template \
    && chmod 0755 /usr/local/bin/projecttime-web-entrypoint.sh

USER 101

RUN test -r /etc/nginx/templates/default.conf.template \
    && test -x /usr/local/bin/projecttime-web-entrypoint.sh \
    && API_UPSTREAM=http://127.0.0.1:18080 \
       NGINX_RESOLVER=127.0.0.1 \
       /usr/local/bin/projecttime-web-entrypoint.sh nginx -t \
    && rm -f /etc/nginx/conf.d/default.conf

EXPOSE 8080

ENTRYPOINT ["/usr/local/bin/projecttime-web-entrypoint.sh"]

CMD ["nginx", "-g", "daemon off;"]
