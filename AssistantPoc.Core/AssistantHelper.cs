using System.ClientModel;
using System.Diagnostics.CodeAnalysis;
using AssistantPoc.Core.Models;
using Azure;
using Azure.AI.OpenAI;
using Newtonsoft.Json;
using OpenAI.Assistants;
using OpenAI.Files;

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

    public void SetAssistantIdEnv(string assistantId)
    {
        Environment.SetEnvironmentVariable("ASSISTANT_ID", assistantId);
    }

    public void SetInstructionsPathEnv(string path)
    {
        Environment.SetEnvironmentVariable("INSTRUCTIONS_PATH", path);
    }

    public void SetTimeSeriesPathEnv(string path)
    {
        Environment.SetEnvironmentVariable("TIMESERIES_PATH", path);
    }

    public async Task RunThread(AssistantClient assistantClient, string? assistantId = null)
    {
        assistantId ??= Environment.GetEnvironmentVariable("ASSISTANT_ID")
            ?? throw new ArgumentNullException("ASSISTANT_ID");

        // Create and run a thread with a user query about the data already associated with the assistant
        ThreadCreationOptions threadOptions = new() { };
        ClientResult<AssistantThread> threadResult = await assistantClient.CreateThreadAsync(threadOptions);
        bool hasPrompt = true;

        while (hasPrompt)
        {
            AsyncCollectionResult<StreamingUpdate>? streamingResult = assistantClient.CreateRunStreamingAsync(threadResult.Value.Id,
                assistantId, options: new RunCreationOptions
                {
                    // AdditionalInstructions = "",
                    // AdditionalMessages = { }
                });

            do
            {
                streamingResult = await ReadStreamingResult(assistantClient, threadResult.Value.Id, streamingResult);
            } while (streamingResult is not null);

            hasPrompt = await Prompt(threadResult.Value, assistantClient);
        }

        await assistantClient.DeleteThreadAsync(threadResult.Value.Id);
    }

    public async Task<AsyncCollectionResult<StreamingUpdate>?> ReadStreamingResult(
        AssistantClient assistantClient,
        string threadId, AsyncCollectionResult<StreamingUpdate> streamingResult)
    {
        ArgumentNullException.ThrowIfNull(Client);
        await foreach (StreamingUpdate streamingUpdate in streamingResult)
        {
            if (streamingUpdate.UpdateKind == StreamingUpdateReason.MessageCreated
                && streamingUpdate is MessageStatusUpdate messageStatusUpdate)
            {
                if (messageStatusUpdate.Value.Role == MessageRole.Assistant)
                {
                    Console.Write("[Assistant] ");
                }
            }
            else if (streamingUpdate is MessageContentUpdate contentUpdate)
            {
                if (contentUpdate.ImageFileId is not null)
                {
                    ClientResult<BinaryData> imageData = await Client.GetOpenAIFileClient().DownloadFileAsync(contentUpdate.ImageFileId);
                    // Save image to a file or display it
                    string fileName = $"image_{DateTime.Now:yyyyMMddHHmmss}.png";
                    await File.WriteAllBytesAsync(Path.Combine("/Users/trungtran/MyPlace/Yokogawa/Projects/ahi-apps/Projects/open-ai-assistant/Local", fileName), imageData.Value);
                }
                else
                {
                    Console.Write(contentUpdate.Text);
                }
            }
            else if (streamingUpdate.UpdateKind == StreamingUpdateReason.RunFailed && streamingUpdate is RunUpdate runUpdate)
            {
                if (runUpdate.Value.LastError is not null)
                {
                    Console.WriteLine($"[Assistant] [{runUpdate.Value.LastError.Code}] {runUpdate.Value.LastError.Message}");
                }
            }
            else if (streamingUpdate is RequiredActionUpdate actionUpdate)
            {
                string responseJson = "{}";
                switch (actionUpdate.FunctionName)
                {
                    case nameof(NavigateToAsset):
                        {
                            var command = JsonConvert.DeserializeObject<NavigateToAssetCommand>(actionUpdate.FunctionArguments);
                            var response = await NavigateToAsset(command ?? throw new ArgumentNullException(nameof(command)));
                            responseJson = JsonConvert.SerializeObject(response);
                            break;
                        }
                    case nameof(GetTimeSeries):
                        {
                            var command = JsonConvert.DeserializeObject<GetTimeSeriesCommand>(actionUpdate.FunctionArguments);
                            var response = await GetTimeSeries(command ?? throw new ArgumentNullException(nameof(command)));
                            responseJson = JsonConvert.SerializeObject(response);
                            break;
                        }
                }

                var submitResult = assistantClient.SubmitToolOutputsToRunStreamingAsync(
                    threadId: threadId,
                    runId: actionUpdate.Value.Id,
                    toolOutputs: [new ToolOutput(actionUpdate.ToolCallId, responseJson)]);

                return submitResult;
            }
            else
            {
                // Console.WriteLine(streamingUpdate.UpdateKind);
            }
        }

        return null;
    }

    public async Task<NavigateToAssetResponse> NavigateToAsset(NavigateToAssetCommand command)
    {
        await Task.CompletedTask;
        AssetEntity? assetEntity = null;
        if (command.AssetId is not null && Guid.TryParse(command.AssetId, out var assetId))
        {
            assetEntity = DataStore.Assets.FirstOrDefault(a => a.Id == assetId);
        }
        else if (!string.IsNullOrWhiteSpace(command.AssetName))
        {
            assetEntity = DataStore.Assets.FirstOrDefault(a => string.Equals(a.Name, command.AssetName, StringComparison.OrdinalIgnoreCase));
        }
        return new NavigateToAssetResponse
        {
            AssetId = assetEntity?.Id,
            Found = assetEntity is not null
        };
    }

    public async Task<GetTimeSeriesResponse> GetTimeSeries(GetTimeSeriesCommand command)
    {
        await Task.CompletedTask;
        AssetEntity? assetEntity = null;
        if (command.AssetId is not null && Guid.TryParse(command.AssetId, out var assetId))
        {
            assetEntity = DataStore.Assets.FirstOrDefault(a => a.Id == assetId);
        }
        else if (!string.IsNullOrWhiteSpace(command.AssetName))
        {
            assetEntity = DataStore.Assets.FirstOrDefault(a => string.Equals(a.Name, command.AssetName, StringComparison.OrdinalIgnoreCase));
        }

        var response = new GetTimeSeriesResponse
        {
            AssetId = assetEntity?.Id,
            Found = assetEntity is not null
        };

        if (response.Found)
        {
            var sampleResponseJson = File.ReadAllText(Environment.GetEnvironmentVariable("TIMESERIES_PATH") ?? throw new ArgumentNullException());
            var sampleResponse = JsonConvert.DeserializeObject<GetTimeSeriesResponse>(sampleResponseJson);
            response.Series = sampleResponse?.Series;
        }

        return response;
    }

    public async Task<bool> Prompt(AssistantThread thread, AssistantClient assistantClient)
    {
        Console.WriteLine();
        Console.Write("[User] ");
        string? prompt = Console.ReadLine();
        if (string.IsNullOrEmpty(prompt))
            return false;

        prompt = AppendPromptMetadata(prompt);

        await assistantClient.CreateMessageAsync(thread.Id, MessageRole.User,
            content: [MessageContent.FromText(prompt)], options: new MessageCreationOptions
            {
                Attachments = []
            });
        return true;
    }

    public string AppendPromptMetadata(string prompt)
    {
        return $"""
        {prompt}
        ---
        Current time: {DateTime.UtcNow:o}
        """;
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
        assistantOptions.Tools.Add(new CodeInterpreterToolDefinition());

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
