# syntax=docker/dockerfile:1

FROM node:20-alpine AS build

# Preserve the repository-relative path used by the frontend validation
# scripts while copying only the repository files they inspect.
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

COPY src/backend/ProjectTime.Api/Modules/AiProviderConfigurationModule.cs \
     src/backend/ProjectTime.Api/Modules/AiProviderConfigurationModule.cs

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
     src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs

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

COPY database/migrations/034_module_026_crm_erp_integrations.sql \
     database/migrations/034_module_026_crm_erp_integrations.sql

COPY database/migrations/035_work_register_055c_055d_split.sql \
     database/migrations/035_work_register_055c_055d_split.sql

COPY database/migrations/036_work_register_role_scope_and_closeout_handoff.sql \
     database/migrations/036_work_register_role_scope_and_closeout_handoff.sql

COPY database/migrations/037_work_register_dates_and_contract_types.sql \
     database/migrations/037_work_register_dates_and_contract_types.sql

COPY database/migrations/038_work_to_cash_lifecycle_and_audit.sql \
     database/migrations/038_work_to_cash_lifecycle_and_audit.sql

COPY database/rollback/034_module_026_crm_erp_integrations_rollback.sql \
     database/rollback/034_module_026_crm_erp_integrations_rollback.sql

COPY database/rollback/035_work_register_055c_055d_split_rollback.sql \
     database/rollback/035_work_register_055c_055d_split_rollback.sql

COPY database/rollback/036_work_register_role_scope_and_closeout_handoff_rollback.sql \
     database/rollback/036_work_register_role_scope_and_closeout_handoff_rollback.sql

COPY database/rollback/037_work_register_dates_and_contract_types_rollback.sql \
     database/rollback/037_work_register_dates_and_contract_types_rollback.sql

COPY database/rollback/038_work_to_cash_lifecycle_and_audit_rollback.sql \
     database/rollback/038_work_to_cash_lifecycle_and_audit_rollback.sql

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

COPY scripts/module-066-validation/ \
     scripts/module-066-validation/

COPY deployment/containers/web/Dockerfile \
     deployment/containers/web/Dockerfile

WORKDIR /workspace/src/frontend/project-time-web

RUN npm run build


FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime

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
