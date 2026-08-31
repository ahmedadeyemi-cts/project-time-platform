# Generates compiler copies that integrate universal answer reliability and
# Ask Celar AI operational intelligence into the existing composition root and
# authoritative v2 chat platform.
# Usage:
#   awk -v mode=services -f this-file canonical-services.cs
#   awk -v mode=production -f this-file canonical-production-module.cs

BEGIN {
  inserted_service = 0
  inserted_current_fact_service = 0
  inserted_operations_service = 0
  inserted_map = 0
  inserted_intent_map = 0
  changed_delegate = 0
  inserted_parameter = 0
  inserted_plan = 0
  inserted_product_knowledge_plan = 0
  inserted_product_knowledge_branch = 0
  inserted_product_knowledge_helper = 0
  inserted_public_fact_precheck = 0
  inserted_public_fact_branch = 0
  inserted_public_fact_helper = 0
  inserted_current_fact_gate = 0
  inserted_gate = 0
  inserted_evidence = 0
  inserted_flowhive_canonical_work_package_instruction = 0
  inserted_flowhive_freshness_precheck = 0
  inserted_flowhive_freshness_postcheck = 0
  inserted_flowhive_citation_gate = 0
  replaced_flowhive_builder = 0
  waiting_for_map_brace = 0
  after_quality_gate = 0
}

mode == "services" {
  print
  if ($0 ~ /services\.AddSingleton<CelarAiInternalDataService>\(\);/) {
    print "        services.AddHttpClient(CelarAiAuthoritativePublicFactService.ClientName);"
    print "        services.AddSingleton<CelarAiAuthoritativePublicFactService>();"
    print "        services.AddSingleton<CelarAiUniversalAnswerReliabilityService>();"
    print "        services.AddSingleton<CelarAiDefectOrchestrationService>();"
    print "        services.AddSingleton<CelarAiDefectQueryService>();"
    print "        services.AddSingleton<CelarAiMonitorLeadershipService>();"
    print "        services.AddHostedService<CelarAiAvailabilityMonitorService>();"
    inserted_service++
    inserted_current_fact_service++
    inserted_operations_service++
  }
  next
}

mode == "production" {
  line = $0
  if (line ~ /public static IEndpointRouteBuilder MapCelarAiProductionPlatformEndpoints\(this IEndpointRouteBuilder endpoints\)/) waiting_for_map_brace = 1

  if (line ~ /CelarAiPeopleAndGuidanceService, CelarAiCapabilityRoutingStore, CancellationToken/) {
    gsub(/CelarAiPeopleAndGuidanceService, CelarAiCapabilityRoutingStore, CancellationToken/,
      "CelarAiPeopleAndGuidanceService, PulseAiQuestionPlanner, CelarAiAuthoritativePublicFactService, CelarAiUniversalAnswerReliabilityService, CelarAiCapabilityRoutingStore, CancellationToken", line)
    changed_delegate++
  }

  if (line ~ /var generated = BuildPlan\(request\.Plan, composition\.FlowHivePlan\);/) {
    gsub(/BuildPlan\(request\.Plan, composition\.FlowHivePlan\)/,
      "ProjectFlowHiveDetailedPlanBuilder.Build(request.Plan, composition.FlowHivePlan)", line)
    replaced_flowhive_builder++
  }

  if (line ~ /var outcome = BuildPlanningOutcome\(request\);/) {
    print "        var sowFreshness = await VerifyFlowHiveSowFreshnessAsync("
    print "            request.Plan,"
    print "            context,"
    print "            identity.Value.Effective,"
    print "            cancellationToken);"
    print "        if (sowFreshness.Failure is not null) return sowFreshness.Failure;"
    inserted_flowhive_freshness_precheck++
  }

  if (line ~ /var sowCitations = composition\.Citations\.Where\(citation =>/) {
    print "        sowFreshness = await VerifyFlowHiveSowFreshnessAsync("
    print "            request.Plan,"
    print "            context,"
    print "            identity.Value.Effective,"
    print "            cancellationToken);"
    print "        if (sowFreshness.Failure is not null) return sowFreshness.Failure;"
    inserted_flowhive_freshness_postcheck++
  }

  if (line ~ /^[[:space:]]*if \(attachmentIds\.Length == 0 && intent\.Code == \"current_date_time\"\)[[:space:]]*$/) {
    print "        if (directProductKnowledge is not null)"
    print "        {"
    print "            result = await DirectResultAsync("
    print "                request,"
    print "                identity.Value,"
    print "                access,"
    print "                repository,"
    print "                context,"
    print "                intent,"
    print "                ProductKnowledgeAnswer(directProductKnowledge),"
    print "                \"celar_ai_governed_product_knowledge\","
    print "                cancellationToken);"
    print "        }"
    print "        else if (authoritativePublicFactPreverified)"
    print "        {"
    print "            result = await PersistAuthoritativePublicFactAsync("
    print "                request,"
    print "                identity.Value,"
    print "                access,"
    print "                repository,"
    print "                context,"
    print "                intent,"
    print "                authoritativePublicFactCandidate!,"
    print "                cancellationToken);"
    print "        }"
    sub(/if \(/, "else if (", line)
    inserted_product_knowledge_branch++
    inserted_public_fact_branch++
  }

  if (line ~ /^[[:space:]]*private static async Task<PulseAiSystemQuestionResult> DirectResultAsync\([[:space:]]*$/) {
    print "    private static bool IsStableProductKnowledgeQuestion(string question)"
    print "    {"
    print "        var normalized = Whitespace().Replace(question.Trim().ToLowerInvariant(), \" \");"
    print "        if (!normalized.Contains(\"flowhive\", StringComparison.Ordinal)) return false;"
    print "        var asksPurpose = normalized.Contains(\"purpose\", StringComparison.Ordinal)"
    print "            || normalized.StartsWith(\"what is flowhive\", StringComparison.Ordinal)"
    print "            || normalized.StartsWith(\"what is project flowhive\", StringComparison.Ordinal)"
    print "            || normalized.StartsWith(\"what does flowhive\", StringComparison.Ordinal)"
    print "            || normalized.StartsWith(\"what does project flowhive\", StringComparison.Ordinal);"
    print "        var asksLiveState = normalized.Contains(\"current status\", StringComparison.Ordinal)"
    print "            || normalized.Contains(\"right now\", StringComparison.Ordinal)"
    print "            || normalized.Contains(\"for project\", StringComparison.Ordinal)"
    print "            || normalized.Contains(\"schedule\", StringComparison.Ordinal)"
    print "            || normalized.Contains(\"timeline\", StringComparison.Ordinal)"
    print "            || normalized.Contains(\"milestone\", StringComparison.Ordinal)"
    print "            || normalized.Contains(\"dependency\", StringComparison.Ordinal);"
    print "        return asksPurpose && !asksLiveState;"
    print "    }"
    print ""
    print "    private static PulseAiSystemDetailedAnswer ProductKnowledgeAnswer(PulseAiKnowledgeAnswer knowledge)"
    print "    {"
    print "        var now = DateTimeOffset.UtcNow;"
    print "        return Answer("
    print "            knowledge.Summary,"
    print "            knowledge.Title,"
    print "            knowledge.DetailedSteps,"
    print "            knowledge.ImportantRules,"
    print "            .99m,"
    print "            \"Governed source-controlled Pulse product knowledge. No provider or model execution was required.\","
    print "            now);"
    print "    }"
    print ""
    inserted_product_knowledge_helper++
    print "    private static async Task<PulseAiSystemQuestionResult> PersistAuthoritativePublicFactAsync("
    print "        CelarAiProductionChatRequest request,"
    print "        (Guid Actual, Guid Effective) identity,"
    print "        PulseAiSystemAccess access,"
    print "        PulseAiSystemIntelligenceRepository repository,"
    print "        HttpContext context,"
    print "        Intent intent,"
    print "        PulseAiSystemQuestionResult verified,"
    print "        CancellationToken cancellationToken)"
    print "    {"
    print "        const string provider = \"celar_ai_authoritative_public_fact\";"
    print "        var correlationId = CorrelationId(context);"
    print "        var detail = DetailLevel(request.DetailLevel);"
    print "        var mayPersist = identity.Actual == identity.Effective && access.CanViewConversations;"
    print "        var conversation = mayPersist ? await repository.EnsureConversationAsync(request.ConversationId, identity.Actual, identity.Effective, intent.Code, cancellationToken) : null;"
    print "        var persisted = conversation is not null;"
    print "        var conversationId = conversation?.ConversationId ?? request.ConversationId ?? Guid.NewGuid();"
    print "        var user = persisted"
    print "            ? await repository.AppendMessageAsync(conversationId, identity.Effective, \"user\", \"completed\", request.Question ?? string.Empty, new { intent = intent.Code, request.ClientTimeZone }, null, null, correlationId, string.Empty, string.Empty, [], new { source = provider }, verified.Answer.DataAsOf, cancellationToken)"
    print "            : (MessageId: Guid.NewGuid(), SequenceNumber: 1);"
    print "        var run = persisted ? await repository.CreateInquiryRunAsync(conversationId, user.MessageId, identity.Actual, identity.Effective, intent.Code, detail, Sha256(request.Question ?? string.Empty), correlationId, cancellationToken) : Guid.NewGuid();"
    print "        var provisional = verified with"
    print "        {"
    print "            ConversationId = conversationId,"
    print "            UserMessageId = user.MessageId,"
    print "            AssistantMessageId = Guid.Empty,"
    print "            InquiryRunId = run,"
    print "            IntentCode = intent.Code,"
    print "            DetailLevel = detail,"
    print "            ModelProvider = provider,"
    print "            ModelName = CelarAiAuthoritativePublicFactService.ContractVersion,"
    print "            CorrelationId = correlationId,"
    print "            Persisted = persisted"
    print "        };"
    print "        var assistant = persisted"
    print "            ? await repository.AppendMessageAsync(conversationId, identity.Effective, \"assistant\", provisional.Status, provisional.Answer.DirectConclusion, provisional.ToPublicResponse(), run, null, correlationId, provisional.ModelProvider, provisional.ModelName, [], new { authoritativePublicFact = true, sourceCount = provisional.Sources.Count, previousConversationMessagesInjected = false }, provisional.Answer.DataAsOf, cancellationToken)"
    print "            : (MessageId: Guid.NewGuid(), SequenceNumber: 2);"
    print "        if (persisted) await repository.CompleteInquiryRunAsync(run, assistant.MessageId, provisional.Status, [], [], provisional.Sources.Count, provisional.Answer.Confidence, string.Empty, cancellationToken);"
    print "        return provisional with { AssistantMessageId = assistant.MessageId, Persisted = persisted && assistant.MessageId != Guid.Empty };"
    print "    }"
    print ""
    inserted_public_fact_helper++
  }

  print line

  if (line ~ /citation\.DocumentCategory\.Equals\("statement_of_work".*ToArray\(\);/) {
    print "        var staleSowCitations = sowCitations"
    print "            .Where(citation => !sowFreshness.CurrentSowDocumentIds.Contains(citation.DocumentId))"
    print "            .ToArray();"
    print "        if (staleSowCitations.Length > 0)"
    print "            return FlowHiveStaleSowCitationFailure(staleSowCitations.Length);"
    inserted_flowhive_citation_gate++
  }

  if (line ~ /Treat the approved SOW Scope of Services section as the primary delivery authority/) {
    print "        builder.AppendLine(\"Return each distinct source-backed SOW work package exactly once as one canonical work-package record. Do not pre-expand or duplicate the same scope outcome across Plan, Design, Implement, Validate, and Release; the deterministic FlowHive builder creates those five phase tasks. Use the phase field only as schema compatibility and preserve complete source citations, used-item details, prerequisites, roles, inputs, outputs, validation, acceptance, risks, and open questions on the single canonical work package.\");"
    inserted_flowhive_canonical_work_package_instruction++
  }

  if (waiting_for_map_brace == 1 && line ~ /^[[:space:]]*\{[[:space:]]*$/) {
    print "        endpoints.MapCelarAiUniversalAnswerReliabilityEndpoints();"
    print "        endpoints.MapCelarAiOperationsEndpoints();"
    print "        endpoints.MapCelarAiOperationsIntentEndpoints();"
    print "        endpoints.MapCelarAiDefectQueryEndpoints();"
    inserted_map++
    inserted_intent_map++
    waiting_for_map_brace = 0
  }

  if (line ~ /^[[:space:]]*CelarAiPeopleAndGuidanceService peopleAndGuidance,[[:space:]]*$/) {
    print "        PulseAiQuestionPlanner questionPlanner,"
    print "        CelarAiAuthoritativePublicFactService authoritativePublicFacts,"
    print "        CelarAiUniversalAnswerReliabilityService universalReliability,"
    inserted_parameter++
  }

  if (line ~ /context\.Items\[PulseAiSystemIntelligencePolicy\.ResolvedIntentContextItem\] = intentPlan;/) {
    print "        var reliabilityPlan = universalReliability.Plan("
    print "            question,"
    print "            intent.Code,"
    print "            request.ProjectCode,"
    print "            request.ProjectName,"
    print "            request.ModuleCode,"
    print "            request.IncludeRepositoryContext,"
    print "            attachmentIds.Length);"
    inserted_plan++
  }

  if (line ~ /^[[:space:]]*PulseAiSystemQuestionResult result;[[:space:]]*$/) {
    print "        var directProductKnowledge = attachmentIds.Length == 0 && IsStableProductKnowledgeQuestion(question)"
    print "            ? questionPlanner.PlanHelpSearch(question).DirectKnowledgeAnswer"
    print "            : null;"
    inserted_product_knowledge_plan++
    print "        PulseAiSystemQuestionResult? authoritativePublicFactCandidate = null;"
    print "        var authoritativePublicFactPreverified = false;"
    print "        if (attachmentIds.Length == 0 && intent.Code == \"general_knowledge\")"
    print "        {"
    print "            var publicFactSeedAt = DateTimeOffset.UtcNow;"
    print "            var publicFactSeedAnswer = Answer("
    print "                \"Celar AI is verifying this current public fact from an official source.\","
    print "                \"No provider or model answer is accepted before retrieval-time official-source verification.\","
    print "                [],"
    print "                [],"
    print "                0m,"
    print "                \"Pending authoritative public retrieval.\","
    print "                publicFactSeedAt);"
    print "            var publicFactSeed = new PulseAiSystemQuestionResult("
    print "                request.ConversationId ?? Guid.NewGuid(),"
    print "                Guid.NewGuid(),"
    print "                Guid.Empty,"
    print "                Guid.NewGuid(),"
    print "                \"partial\","
    print "                intent.Code,"
    print "                DetailLevel(request.DetailLevel),"
    print "                publicFactSeedAnswer,"
    print "                [],"
    print "                [],"
    print "                [],"
    print "                \"celar_ai_authoritative_public_fact\","
    print "                CelarAiAuthoritativePublicFactService.ContractVersion,"
    print "                CorrelationId(context),"
    print "                [],"
    print "                false);"
    print "            var publicFactVerified = await authoritativePublicFacts.VerifyAsync("
    print "                publicFactSeed,"
    print "                reliabilityPlan,"
    print "                question,"
    print "                cancellationToken);"
    print "            if (!ReferenceEquals(publicFactVerified, publicFactSeed))"
    print "            {"
    print "                authoritativePublicFactCandidate = publicFactVerified;"
    print "                authoritativePublicFactPreverified = true;"
    print "            }"
    print "        }"
    inserted_public_fact_precheck++
  }

  if (line ~ /^[[:space:]]*result = EnforceAnswer\(result, intent, question\);[[:space:]]*$/) {
    print "        if (!authoritativePublicFactPreverified)"
    print "        {"
    print "            result = await authoritativePublicFacts.VerifyAsync("
    print "                result,"
    print "                reliabilityPlan,"
    print "                question,"
    print "                cancellationToken);"
    print "        }"
    print "        var reliabilityEnforcement = universalReliability.Enforce("
    print "            result,"
    print "            reliabilityPlan,"
    print "            request.IncludeSourceCitations,"
    print "            request.IncludeAssumptions);"
    print "        result = reliabilityEnforcement.Result;"
    inserted_current_fact_gate++
    inserted_gate++
    after_quality_gate = 1
  }

  if (after_quality_gate == 1 && line ~ /^[[:space:]]*trust,[[:space:]]*$/) {
    print "            reliability = universalReliability.ToPublicEvidence("
    print "                reliabilityPlan,"
    print "                reliabilityEnforcement.Assessment),"
    inserted_evidence++
    after_quality_gate = 0
  }
  next
}

END {
  if (mode == "services") {
    if (inserted_service != 1 || inserted_current_fact_service != 1 || inserted_operations_service != 1) exit 42
  } else if (mode == "production") {
    if (inserted_map != 1 || inserted_intent_map != 1 || changed_delegate != 1 || inserted_parameter != 1 || inserted_plan != 1 || inserted_product_knowledge_plan != 1 || inserted_product_knowledge_branch != 1 || inserted_product_knowledge_helper != 1 || inserted_public_fact_precheck != 1 || inserted_public_fact_branch != 1 || inserted_public_fact_helper != 1 || inserted_current_fact_gate != 1 || inserted_gate != 1 || inserted_evidence != 1 || inserted_flowhive_canonical_work_package_instruction != 1 || inserted_flowhive_freshness_precheck != 1 || inserted_flowhive_freshness_postcheck != 1 || inserted_flowhive_citation_gate != 1 || replaced_flowhive_builder != 1) exit 42
  } else exit 42
}
