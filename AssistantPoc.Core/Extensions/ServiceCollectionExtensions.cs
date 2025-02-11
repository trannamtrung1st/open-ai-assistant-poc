using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Azure.AI.OpenAI;
using AssistantPoc.Core.Interfaces;
using AssistantPoc.Core.Services;
using AssistantPoc.Core.Configuration;
using Azure;

namespace AssistantPoc.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAssistantServices(
        this IServiceCollection services,
        Action<AssistantConfiguration> configureOptions)
    {
        services.Configure(configureOptions);

        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IOptions<AssistantConfiguration>>().Value;
            return new AzureOpenAIClient(
                new Uri(config.Endpoint),
                new AzureKeyCredential(config.ApiKey));
        });

        services.AddSingleton(sp =>
        {
            var client = sp.GetRequiredService<AzureOpenAIClient>();
            return client.GetAssistantClient();
        });

        services.AddSingleton(sp =>
        {
            var client = sp.GetRequiredService<AzureOpenAIClient>();
            return client.GetVectorStoreClient();
        });

        services.AddSingleton(sp =>
        {
            var client = sp.GetRequiredService<AzureOpenAIClient>();
            return client.GetOpenAIFileClient();
        });

        services.AddSingleton<IAssistantFileService, AssistantFileService>();
        services.AddSingleton<IAssistantService, AssistantService>();

        return services;
    }
}