using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.AI.OpenAI;
using OpenAI;

namespace AssistantPoc.Core;

public class AssistantHelper
{
    public AzureOpenAIClient? Client { get; private set; }

    [MemberNotNull(nameof(Client))]
    public void InitClient(string? endpoint = null, string? key = null)
    {
        endpoint ??= Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
            ?? throw new ArgumentNullException("AZURE_OPENAI_ENDPOINT");
        key ??= Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
            ?? throw new ArgumentNullException("AZURE_OPENAI_API_KEY");

        Client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));
    }

    public void SetEndpointEnv(string endpoint)
    {
        Environment.SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", endpoint);
    }

    public void SetApiKeyEnv(string apiKey)
    {
        Environment.SetEnvironmentVariable("AZURE_OPENAI_API_KEY", apiKey);
    }
}