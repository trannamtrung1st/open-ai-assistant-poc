using AssistantPoc.Core.Configuration;
using Azure.AI.OpenAI;

namespace AssistantPoc.Core.Services;

public abstract class BaseService
{
    protected readonly AzureOpenAIClient Client;
    protected readonly AssistantConfiguration Configuration;

    protected BaseService(AzureOpenAIClient client, AssistantConfiguration configuration)
    {
        Client = client;
        Configuration = configuration;
    }

    protected void ValidateNotNull<T>(T value, string paramName) where T : class
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
    }
}