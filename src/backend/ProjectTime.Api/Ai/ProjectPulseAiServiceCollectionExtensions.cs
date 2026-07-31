namespace ProjectTime.Api.Ai;

public static class ProjectPulseAiServiceCollectionExtensions
{
    public static IServiceCollection AddProjectPulseAi(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddHttpClient("ProjectPulseAi");
        services.AddHttpClient("PulseAiPrivateOcr", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddHttpClient("PulseAiPrivateEmbedding", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(3);
        });
        services.AddHttpClient("PulseAiPrivateInference", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddHttpClient("PulseAiSystemTools", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(75);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        });
        services.AddSingleton<ProjectPulseAiConfiguration>();
        services.AddSingleton<ProjectPulseAiSecretStore>();
        services.AddHostedService<ProjectPulseAiSecretLoader>();
        services.AddHostedService<ProjectPulseAiConfigurationSynchronizer>();
        services.AddSingleton<ProjectPulseAiHealthRegistry>();
        services.AddSingleton<ProjectPulseClaudeProvider>();
        services.AddSingleton<ProjectPulseOpenAiProvider>();
        services.AddSingleton<IProjectPulseAiProvider>(provider => provider.GetRequiredService<ProjectPulseClaudeProvider>());
        services.AddSingleton<IProjectPulseAiProvider>(provider => provider.GetRequiredService<ProjectPulseOpenAiProvider>());
        services.AddSingleton<ProjectPulseAiRouter>();
        services.AddSingleton<ProjectPulseAiHealthCoordinator>();
        services.AddSingleton<PulseAiDocumentGroundingService>();
        services.AddSingleton<PulseAiQuestionPlanner>();
        services.AddSingleton<PulseAiEscalationSanitizer>();
        services.AddSingleton<PulseAiPrivateDocumentExtractionService>();
        services.AddSingleton<PulseAiPrivateDocumentPipelineService>();
        services.AddSingleton<PulseAiPrivateRuntimeSourceResolver>();
        services.AddSingleton<PulseAiPrivateMalwareScanner>();
        services.AddSingleton<PulseAiPrivateOcrClient>();
        services.AddSingleton<PulseAiPrivateEmbeddingClient>();
        services.AddSingleton<PulseAiPrivateDocumentRuntimeRepository>();
        services.AddSingleton<PulseAiPrivateDocumentRuntimeService>();
        services.AddHostedService<PulseAiPrivateDocumentRuntimeWorker>();
        services.AddSingleton<PulseAiPrivateRagRepository>();
        services.AddSingleton<PulseAiPrivateRetrievalAuthorizationService>();
        services.AddSingleton<PulseAiPrivateRetrievalService>();
        services.AddSingleton<PulseAiPrivateModelClient>();
        services.AddSingleton<PulseAiPrivateRagService>();
        services.AddSingleton<PulseAiSystemApiCatalogService>();
        services.AddSingleton<PulseAiSystemToolExecutor>();
        services.AddSingleton<PulseAiSystemIntelligenceRepository>();
        services.AddSingleton<PulseAiSystemIntelligenceService>();
        services.AddSingleton<ProjectPulseAiTimeEntrySuggestionService>();
        services.AddHostedService<ProjectPulseAiHealthMonitor>();
        return services;
    }
}
