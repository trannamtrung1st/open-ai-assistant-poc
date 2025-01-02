using System.ClientModel;
using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.AI.OpenAI;
using OpenAI;
using OpenAI.Assistants;
using OpenAI.Files;
using OpenAI.VectorStores;

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

    public void SetKnowledgeBasePathEnv(string path)
    {
        Environment.SetEnvironmentVariable("KNOWLEDGE_BASE_PATH", path);
    }

    public void SetInstructionsPathEnv(string path)
    {
        Environment.SetEnvironmentVariable("INSTRUCTIONS_PATH", path);
    }

    public async Task CreateAhiAssistant()
    {
        ArgumentNullException.ThrowIfNull(Client);

        OpenAIFileClient fileClient = Client.GetOpenAIFileClient();
        AssistantClient assistantClient = Client.GetAssistantClient();

        var knowledgeBasePath = Environment.GetEnvironmentVariable("KNOWLEDGE_BASE_PATH")
            ?? throw new ArgumentNullException("KNOWLEDGE_BASE_PATH");

        Console.WriteLine("Uploading knowledge base files...");
        var fileIds = await UploadKnowledgeBaseFiles(fileClient, knowledgeBasePath);
        Console.WriteLine("Uploaded knowledge base files");

        var instructionsPath = Environment.GetEnvironmentVariable("INSTRUCTIONS_PATH")
            ?? throw new ArgumentNullException("INSTRUCTIONS_PATH");
        var instructionsContent = File.ReadAllText(instructionsPath);
        Console.WriteLine("Read instructions");

        // VectorStoreClient vectorStoreClient = Client.GetVectorStoreClient();
        // CreateVectorStoreOperation vectorStoreResult = await vectorStoreClient.CreateVectorStoreAsync(
        //     waitUntilCompleted: true,
        //     vectorStore: new VectorStoreCreationOptions
        //     {
        //         ChunkingStrategy = FileChunkingStrategy.Auto,
        //         ExpirationPolicy = new VectorStoreExpirationPolicy(VectorStoreExpirationAnchor.LastActiveAt, days: int.MaxValue),
        //         Name = "AHI AI Assistant - Knowledge Base",
        //     });

        // var vectorStoreId = vectorStoreResult.Value?.Id ?? throw new ArgumentNullException("Vector store ID");
        // foreach (var fileId in fileIds)
        // {
        //     await vectorStoreClient.AddFileToVectorStoreAsync(vectorStoreId, fileId, waitUntilCompleted: true);
        // }

        // Now, we'll create a client intended to help with that data
        AssistantCreationOptions assistantOptions = new()
        {
            Name = "AHI AI Assistant",
            Instructions = instructionsContent,
            Tools = { },
            ToolResources = new()
            {
                FileSearch = new()
                {
                    NewVectorStores =
                    {
                        new VectorStoreCreationHelper(fileIds),
                    }
                }
            },
            Temperature = 0.1f,
            // ResponseFormat = AssistantResponseFormat.JsonObject
            ResponseFormat = AssistantResponseFormat.Auto
        };
        assistantOptions.Tools.Add(new FileSearchToolDefinition());

        assistantOptions.Tools.Add(new FunctionToolDefinition("NavigateToAsset")
        {
            Description = "Navigate to an asset",
            Parameters = BinaryData.FromString(
"""
{
    "type": "object",
    "properties": {
        "assetId": { "type": "string", "description": "The ID of the asset to navigate to" },
        "assetName": { "type": "string", "description": "The name of the asset to navigate to" }
    },
    "required": []
}
"""),
            StrictParameterSchemaEnabled = false
        });

        assistantOptions.Tools.Add(new FunctionToolDefinition("GetTimeSeries")
        {
            Description = "Get time series data of given asset",
            Parameters = BinaryData.FromString(
"""
{
    "type": "object",
    "properties": {
      "assetId": { "type": "string", "description": "The ID of the asset to navigate to" },
      "assetName": { "type": "string", "description": "The name of asset" },
      "from": { "type": "string", "description": "From time (yyyy-MM-dd HH:mm:ss)" },
      "to": { "type": "string", "description": "To time (yyyy-MM-dd HH:mm:ss)" }
    },
    "required": []
}
"""),
            StrictParameterSchemaEnabled = false
        });

        Console.WriteLine("Creating assistant...");
        ClientResult<Assistant> assistantResult = await assistantClient.CreateAssistantAsync(model: "gpt-4o", assistantOptions);
        Console.WriteLine("Created assistant");

        if (assistantResult.Value is null)
            throw new Exception("Failed to create assistant");
    }

    public async Task<List<string>> UploadKnowledgeBaseFiles(OpenAIFileClient fileClient, string knowledgeBasePath)
    {
        // Get all files from the KnowledgeBase directory
        var directory = new DirectoryInfo(knowledgeBasePath);
        var files = directory.GetFiles();
        var fileIds = new List<string>();

        foreach (var file in files)
        {
            // Upload each file
            var uploadResult = await fileClient.UploadFileAsync(
                file: file.OpenRead(),
                filename: file.Name,
                purpose: FileUploadPurpose.Assistants);

            // Get the file ID
            fileIds.Add(uploadResult.Value.Id);
        }

        return fileIds;
    }
}
