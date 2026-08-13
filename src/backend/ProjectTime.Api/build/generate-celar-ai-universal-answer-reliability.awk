# Generates compiler copies that integrate universal answer reliability and
# Ask Celar AI operational intelligence into the existing composition root and
# authoritative v2 chat platform.
# Usage:
#   awk -v mode=services   -f this-file canonical-services.cs
#   awk -v mode=production -f this-file canonical-production-module.cs

BEGIN {
  inserted_service = 0
  inserted_operations_service = 0
  inserted_map = 0
  changed_delegate = 0
  inserted_parameter = 0
  inserted_plan = 0
  inserted_gate = 0
  inserted_evidence = 0
  waiting_for_map_brace = 0
  after_quality_gate = 0
}

mode == "services" {
  print
  if ($0 ~ /services\.AddSingleton<CelarAiInternalDataService>\(\);/) {
    print "        services.AddSingleton<CelarAiUniversalAnswerReliabilityService>();"
    print "        services.AddSingleton<CelarAiDefectOrchestrationService>();"
    print "        services.AddSingleton<CelarAiDefectQueryService>();"
    print "        services.AddSingleton<CelarAiMonitorLeadershipService>();"
    print "        services.AddHostedService<CelarAiAvailabilityMonitorService>();"
    inserted_service++
    inserted_operations_service++
  }
  next
}

mode == "production" {
  line = $0

  if (line ~ /public static IEndpointRouteBuilder MapCelarAiProductionPlatformEndpoints\(this IEndpointRouteBuilder endpoints\)/) {
    waiting_for_map_brace = 1
  }

  if (line ~ /CelarAiPeopleAndGuidanceService, CelarAiCapabilityRoutingStore, CancellationToken/) {
    gsub(/CelarAiPeopleAndGuidanceService, CelarAiCapabilityRoutingStore, CancellationToken/,
      "CelarAiPeopleAndGuidanceService, CelarAiUniversalAnswerReliabilityService, CelarAiCapabilityRoutingStore, CancellationToken",
      line)
    changed_delegate++
  }

  print line

  if (waiting_for_map_brace == 1 && line ~ /^[[:space:]]*\{[[:space:]]*$/) {
    print "        endpoints.MapCelarAiUniversalAnswerReliabilityEndpoints();"
    print "        endpoints.MapCelarAiOperationsEndpoints();"
    print "        endpoints.MapCelarAiOperationsIntentEndpoints();"
    print "        endpoints.MapCelarAiDefectQueryEndpoints();"
    inserted_map++
    waiting_for_map_brace = 0
  }

  if (line ~ /^[[:space:]]*CelarAiPeopleAndGuidanceService peopleAndGuidance,[[:space:]]*$/) {
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

  if (line ~ /^[[:space:]]*result = EnforceAnswer\(result, intent, question\);[[:space:]]*$/) {
    print "        var reliabilityEnforcement = universalReliability.Enforce("
    print "            result,"
    print "            reliabilityPlan,"
    print "            request.IncludeSourceCitations,"
    print "            request.IncludeAssumptions);"
    print "        result = reliabilityEnforcement.Result;"
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
    if (inserted_service != 1 || inserted_operations_service != 1) {
      print "CELAR_UAR_GENERATOR_ERROR=service_registration_count:" inserted_service ",operations:" inserted_operations_service > "/dev/stderr"
      exit 42
    }
  } else if (mode == "production") {
    if (inserted_map != 1 || changed_delegate != 1 || inserted_parameter != 1 ||
        inserted_plan != 1 || inserted_gate != 1 || inserted_evidence != 1) {
      print "CELAR_UAR_GENERATOR_ERROR=map:" inserted_map ",delegate:" changed_delegate ",parameter:" inserted_parameter ",plan:" inserted_plan ",gate:" inserted_gate ",evidence:" inserted_evidence > "/dev/stderr"
      exit 42
    }
  } else {
    print "CELAR_UAR_GENERATOR_ERROR=unknown_mode:" mode > "/dev/stderr"
    exit 42
  }
}
