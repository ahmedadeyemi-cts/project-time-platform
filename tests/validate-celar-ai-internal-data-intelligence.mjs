import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const read = (path) => readFileSync(resolve(root, path), 'utf8')
const requireText = (content, value, evidence) => {
  if (!content.includes(value)) throw new Error(`Missing ${evidence}: ${value}`)
}
const rejectText = (content, value, evidence) => {
  if (content.includes(value)) throw new Error(`Unexpected ${evidence}: ${value}`)
}

const resolver = read('src/backend/ProjectTime.Api/Ai/CelarAiInternalDataService.cs')
for (const source of [
  'project_assignments',
  'work_register_task_assignment_history',
  'engineering_resource_request_assignments',
]) {
  requireText(resolver, source, `assignment authority ${source}`)
}
requireText(resolver, 'SELECT DISTINCT ON (project_id, task_id, user_id)', 'task deduplication')
requireText(resolver, 'JOIN authorized_people allowed ON allowed.user_id = person.user_id', 'pre-match identity scope')
requireText(resolver, 'External providers called: none.', 'internal answer boundary evidence')
requireText(resolver, 'total = reader.GetInt64(10);', 'task count reader ordinal')
requireText(resolver, 'database_schema_not_ready_', 'explicit source-readiness failure')
rejectText(resolver, 'PersonResolutionOutcome.NotAuthorized', 'global-directory existence disclosure')

const contextGenerator = read('src/backend/ProjectTime.Api/build/generate-celar-ai-internal-data-context-resilience.py')
requireText(contextGenerator, 'ParseExplicitQuestionContext', 'explicit current-question context parser generator')
requireText(contextGenerator, 'MatchContextualQuestion', 'context-selected deterministic resolver generator')
requireText(contextGenerator, 'ProjectFactsScopeCte', 'project-fact isolated authorization scope generator')
requireText(contextGenerator, 'ProjectFactsReadinessSql', 'project-fact isolated readiness generator')
requireText(contextGenerator, 'ExactProjectSql = ProjectFactsScopeCte', 'project resolver isolated from workload scope')
requireText(contextGenerator, 'AuthorizedProjectsSql = ProjectFactsScopeCte', 'project suggestions isolated from workload scope')
requireText(contextGenerator, 'ValidateSourceReadinessAsync(connection, query.Kind, cancellationToken)', 'query-specific readiness dispatch')
requireText(contextGenerator, 'COALESCE(project.project_manager_user_id = @person_user_id, FALSE)', 'nullable PM workload role flag is total boolean')
requireText(contextGenerator, 'COALESCE(project.account_executive_user_id = @person_user_id, FALSE)', 'nullable AE workload role flag is total boolean')
requireText(contextGenerator, 'COALESCE(project.solution_architect_user_id = @person_user_id, FALSE)', 'nullable SA workload role flag is total boolean')
requireText(contextGenerator, 'is_work_lifecycle_assigned_project_manager', 'project resolver carries lifecycle PM authority')
requireText(contextGenerator, 'CanViewProjectHistory(access, projectResolution.Project.IsWorkLifecycleAssignedProjectManager)', 'history authorization runs before audit read')
requireText(contextGenerator, 'WorkLifecycleHistoryReadAllRoles', 'Work Lifecycle administrator/PTC read roles')
requireText(contextGenerator, 'WorkLifecycleHistoryBillingRoles', 'Work Lifecycle billing read roles')
requireText(contextGenerator, 'WorkLifecycleHistoryBroadReadRoles', 'Work Lifecycle broad read roles')
requireText(contextGenerator, 'BuildProjectHistoryAccessDeniedAnswer', 'history access-denied result without audit retrieval')
requireText(contextGenerator, 'Ordinary project resource assignment alone does not grant lifecycle audit history.', 'resource assignment cannot widen lifecycle audit authority')
requireText(contextGenerator, '@can_view_lifecycle_portfolio = TRUE', 'Work Lifecycle portfolio readers enter project-history resolver scope')
requireText(contextGenerator, 'CanResolveProjectHistoryPortfolio', 'portfolio history role classifier')
requireText(contextGenerator, 'AddProjectFactsScopeParameters', 'project resolver receives lifecycle-specific scope parameter')
requireText(contextGenerator, 'query.ProjectReference,\\n                    query.Kind,', 'project resolver receives query kind without widening stakeholder lookups')

const buildProps = read('src/backend/ProjectTime.Api/Directory.Build.props')
requireText(buildProps, 'GenerateCelarAiInternalDataContextResilience', 'internal-data compiler resilience target')
requireText(buildProps, 'generate-celar-ai-internal-data-context-resilience.py', 'internal-data compiler generator invocation')
requireText(buildProps, '<Compile Remove="Ai/CelarAiInternalDataService.cs" />', 'canonical internal-data compiler replacement')
requireText(buildProps, '<Compile Include="$(CelarAiInternalDataResilienceGenerated)" />', 'generated internal-data compiler copy')

const intelligence = read('src/backend/ProjectTime.Api/Ai/PulseAiSystemIntelligenceService.cs')
requireText(intelligence, 'plan.IntentCode, out var externalCapsulePurpose', 'intent-owned external purpose')
requireText(intelligence, 'CelarAiExternalCapsuleCatalog.GeneralKnowledge', 'public external purpose')
requireText(intelligence, 'const string externalProblemStatement = "";', 'no internal help capsule')
requireText(intelligence, '_internalData.TryAnswerAsync(', 'all-entry-point deterministic interception')
requireText(intelligence, 'LooksLikeExternalProviderNonAnswer', 'provider non-answer detection')
requireText(intelligence, 'CelarAiExternalAnswerQuality.LooksLikeNonAnswer(value)', 'shared provider answer-quality gate')
const routing = read('src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs')
requireText(routing, 'execution.PublicGeneralQuestion', 'public-only semantic fallback boundary')
requireText(routing, 'CelarAiExternalAnswerQuality.TryRejectPublicAnswer(', 'provider semantic and confidence answer-quality fallback')
requireText(routing, 'public_general_question_semantic_non_answer', 'semantic non-answer route decision')
requireText(routing, 'public_general_question_low_confidence_answer', 'low-confidence answer route decision')
requireText(routing, 'ProjectPulseAiOutcomes.Unavailable', 'semantic non-answer assurance outcome')
requireText(routing, 'DirectNonAnswerPreambles.Any(preamble => opening.StartsWith', 'direct non-answer preamble gate')
requireText(routing, 'AccessLimitationPreambles.Any(preamble => opening.StartsWith', 'access-limitation opening gate')
requireText(routing, 'ExternalInformationScopeCues.Any(cue => opening.Contains', 'external-information scope gate')
requireText(routing, '_health.RecordSuccess(', 'semantic non-answer preserves provider availability')
rejectText(routing, '_health.RecordFailure(target, nonAnswerCode', 'semantic non-answer circuit failure')
const behavioralTests = read('tests/CelarAiInternalDataTests/Program.cs')
requireText(behavioralTests, 'ExternalLooksLikeNonAnswer(', 'semantic non-answer behavioral tests')
requireText(behavioralTests, 'first-person substantive access-control explanation remains an answer', 'substantive access-control negative case')
requireText(behavioralTests, 'Explicit current-question context:', 'context-selected internal question regressions')
requireText(behavioralTests, 'ProjectFactsReadinessSql', 'query-specific project readiness regression')
requireText(behavioralTests, 'DROP TABLE engineering_resource_request_assignments;', 'unrelated supporting-source degradation regression')
requireText(behavioralTests, 'Account Executive remains queryable after unrelated source degradation', 'project fact survives unrelated source outage')
const intelligenceContracts = read('src/backend/ProjectTime.Api/Ai/PulseAiSystemIntelligenceContracts.cs')
requireText(intelligenceContracts, 'celar-ai-system-intelligence-v3-20260807', 'system-intelligence contract version')
for (const formerInternalCapsule of [
  'CelarAiExternalCapsuleCatalog.HelpTroubleshooting',
  'CelarAiExternalCapsuleCatalog.HelpProjectDelivery',
  'CelarAiExternalCapsuleCatalog.HelpProduct',
]) {
  rejectText(intelligence, formerInternalCapsule, 'internal Help Assistant external purpose')
}

const catalog = read('src/backend/ProjectTime.Api/Ai/PulseAiSystemKnowledgeCatalog.cs')
requireText(catalog, 'celar-ai-system-knowledge-v5-20260808', 'system-knowledge contract version')
requireText(catalog, 'CelarAiInternalDataService.IsSupportedQuestion(question)', 'deterministic internal-first classification')
requireText(catalog, 'LooksLikeNamedInternalSubject(raw)', 'named-subject privacy guard')
requireText(catalog, 'LooksLikeClearlyPublicOfficeholderQuestion(normalized)', 'public officeholder classification before acronym privacy guard')
rejectText(catalog, "(?:president|prime\\s+minister|head\\s+of\\s+state|monarch|king|queen)\\s+of\\s+[a-z]", 'unrestricted named-organization officeholder route')
requireText(catalog, 'return true;', 'privacy-preserving internal default')

const production = read('src/backend/ProjectTime.Api/Modules/CelarAiProductionPlatformModule.cs')
requireText(production, 'celar-ai-production-platform-v2-20260807', 'production contract version')
requireText(production, 'intent.Code == CelarAiInternalDataService.IntentCode', 'production internal-data branch')
requireText(production, 'string.Equals(result.Status, "completed"', 'completed-only trust gate')
requireText(production, '"internal_data" when answered && successful > 0', 'verified current-fact classification')

const migration = read('database/migrations/080_celar_ai_internal_data_intelligence.sql')
requireText(migration, 'migration_080_known_directory_correction', 'known verified identity correction')
requireText(migration, 'WHERE candidate_count = 1', 'unambiguous identity seed guard')
const rollback = read('database/rollback/080_celar_ai_internal_data_intelligence_rollback.sql')
requireText(rollback, 'Rollback refused:', 'governed alias rollback guard')

console.log('CELAR_AI_INTERNAL_DATA_STATIC_CONTRACT=PASS')
