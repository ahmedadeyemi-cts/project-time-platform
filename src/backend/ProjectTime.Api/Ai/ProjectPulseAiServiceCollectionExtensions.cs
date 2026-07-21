namespace ProjectTime.Api.Ai;

public static class ProjectPulseAiServiceCollectionExtensions
{
    public static IServiceCollection AddProjectPulseAi(this IServiceCollection services)
    {
        services.AddHttpClient("ProjectPulseAi");
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
        services.AddSingleton<ProjectPulseAiTimeEntrySuggestionService>();
        services.AddHostedService<ProjectPulseAiHealthMonitor>();
        return services;
    }
}
