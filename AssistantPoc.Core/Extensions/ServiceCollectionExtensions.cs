using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Azure.Core;
using Azure.AI.OpenAI;
using AssistantPoc.Core.Interfaces;
using AssistantPoc.Core.Services;
using AssistantPoc.Core.Configuration;
using Azure;
using OpenAI.Assistants;
using OpenAI.VectorStores;
using OpenAI.Files;

namespace AssistantPoc.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAssistantServices(
        this IServiceCollection services,
        Action<AssistantConfiguration> configureOptions)
    {
        services.Configure(configureOptions);

        services.AddSingleton<AzureOpenAIClient>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<AssistantConfiguration>>().Value;
            return new AzureOpenAIClient(
                new Uri(config.Endpoint),
                new AzureKeyCredential(config.ApiKey));
        });

        services.AddSingleton<AssistantClient>(sp =>
        {
            var client = sp.GetRequiredService<AzureOpenAIClient>();
            return client.GetAssistantClient();
        });

        services.AddSingleton<VectorStoreClient>(sp =>
        {
            var client = sp.GetRequiredService<AzureOpenAIClient>();
            return client.GetVectorStoreClient();
        });

        services.AddSingleton<OpenAIFileClient>(sp =>
        {
            var client = sp.GetRequiredService<AzureOpenAIClient>();
            return client.GetOpenAIFileClient();
        });

        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<IAssistantService, AssistantService>();

        return services;
    }
}