using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using AssistantPoc.Core.Interfaces;
using AssistantPoc.Core.Configuration;
using OpenAI.Assistants;
using Newtonsoft.Json;
using System.ClientModel;
using AssistantPoc.Core.Models;
using OpenAI.Files;
using OpenAI.VectorStores;
using Microsoft.Extensions.Options;

namespace AssistantPoc.Core.Services;

public class AssistantService : IAssistantService
{
    private readonly AzureOpenAIClient _client;
    private readonly AssistantClient _assistantClient;
    private readonly VectorStoreClient _vectorStoreClient;
    private readonly OpenAIFileClient _fileClient;
    private readonly IFileService _fileService;
    private readonly AssistantConfiguration _config;

    public AssistantService(
        AzureOpenAIClient client,
        AssistantClient assistantClient,
        VectorStoreClient vectorStoreClient,
        OpenAIFileClient fileClient,
        IFileService fileService,
        IOptions<AssistantConfiguration> options)
    {
        _client = client;
        _assistantClient = assistantClient;
        _vectorStoreClient = vectorStoreClient;
        _fileClient = fileClient;
        _fileService = fileService;
        _config = options.Value;
    }

    public async Task CreateAssistant()
    {
        var fileIds = await _fileService.UploadKnowledgeBaseFiles(_config.KnowledgeBasePath);
        var instructionsContent = await File.ReadAllTextAsync(_config.InstructionsPath);

        var assistantOptions = CreateAssistantOptions(instructionsContent, fileIds);
        var assistantResult = await _assistantClient.CreateAssistantAsync("gpt-4o", assistantOptions);

        if (assistantResult.Value is null)
            throw new Exception("Failed to create assistant");
    }

    private AssistantCreationOptions CreateAssistantOptions(string instructions, List<string> fileIds)
    {
        var options = new AssistantCreationOptions
        {
            Name = "AHI AI Assistant",
            Instructions = instructions,
            Tools = { },
            ToolResources = new()
            {
                FileSearch = new()
                {
                    NewVectorStores = { new VectorStoreCreationHelper(fileIds) }
                }
            },
            Temperature = 0.1f,
            ResponseFormat = AssistantResponseFormat.Auto
        };

        AddTools(options);
        return options;
    }

    private void AddTools(AssistantCreationOptions options)
    {
        options.Tools.Add(new FileSearchToolDefinition());
        options.Tools.Add(new CodeInterpreterToolDefinition());
        options.Tools.Add(CreateNavigateToAssetTool());
        options.Tools.Add(CreateGetTimeSeriesTool());
    }

    private FunctionToolDefinition CreateNavigateToAssetTool()
    {
        return new FunctionToolDefinition("NavigateToAsset")
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
        };
    }

    private FunctionToolDefinition CreateGetTimeSeriesTool()
    {
        return new FunctionToolDefinition("GetTimeSeries")
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
        };
    }

    public async Task RunThread(string? assistantId = null)
    {
        assistantId ??= _config.AssistantId;

        var threadResult = await _assistantClient.CreateThreadAsync(new ThreadCreationOptions());
        bool hasPrompt = true;

        while (hasPrompt)
        {
            var streamingResult = _assistantClient.CreateRunStreamingAsync(
                threadResult.Value.Id,
                assistantId,
                new RunCreationOptions());

            do
            {
                streamingResult = await ReadStreamingResult(threadResult.Value, streamingResult);
            } while (streamingResult is not null);

            hasPrompt = await Prompt(threadResult.Value);
        }

        await _assistantClient.DeleteThreadAsync(threadResult.Value.Id);
    }

    public async Task<bool> Prompt(AssistantThread thread)
    {
        Console.WriteLine();
        Console.Write("[User] ");
        string? prompt = Console.ReadLine();
        if (string.IsNullOrEmpty(prompt))
            return false;

        prompt = AppendPromptMetadata(prompt);
        await _assistantClient.CreateMessageAsync(thread.Id, MessageRole.User,
            content: [MessageContent.FromText(prompt)]);
        return true;
    }

    public string AppendPromptMetadata(string prompt) =>
        $"{prompt}\n---\nCurrent time: {DateTime.UtcNow:o}";

    private async Task<AsyncCollectionResult<StreamingUpdate>?> ReadStreamingResult(
        AssistantThread thread,
        AsyncCollectionResult<StreamingUpdate> streamingResult)
    {
        ArgumentNullException.ThrowIfNull(_client);

        await foreach (StreamingUpdate update in streamingResult)
        {
            switch (update)
            {
                case MessageStatusUpdate messageUpdate when update.UpdateKind == StreamingUpdateReason.MessageCreated:
                    if (messageUpdate.Value.Role == MessageRole.Assistant)
                    {
                        Console.Write("[Assistant] ");
                    }
                    break;

                case MessageContentUpdate contentUpdate:
                    await HandleContentUpdate(contentUpdate);
                    break;

                case RunUpdate runUpdate when update.UpdateKind == StreamingUpdateReason.RunFailed:
                    HandleRunError(runUpdate);
                    break;

                case RequiredActionUpdate actionUpdate:
                    return await HandleActionUpdate(thread, actionUpdate);
            }
        }
        return null;
    }

    private async Task HandleContentUpdate(MessageContentUpdate contentUpdate)
    {
        if (contentUpdate.ImageFileId is not null)
        {
            await HandleImageContent(contentUpdate.ImageFileId);
        }
        else
        {
            Console.Write(contentUpdate.Text);
        }
    }

    private async Task HandleImageContent(string imageFileId)
    {
        var imageData = await _fileClient.DownloadFileAsync(imageFileId);
        string fileName = $"image_{DateTime.Now:yyyyMMddHHmmss}.png";
        string path = Path.Combine(_config.ImageOutputPath ?? "Local", fileName);
        await File.WriteAllBytesAsync(path, imageData.Value);
    }

    private void HandleRunError(RunUpdate runUpdate)
    {
        if (runUpdate.Value.LastError is not null)
        {
            Console.WriteLine($"[Assistant] [{runUpdate.Value.LastError.Code}] {runUpdate.Value.LastError.Message}");
        }
    }

    private async Task<AsyncCollectionResult<StreamingUpdate>> HandleActionUpdate(
        AssistantThread thread,
        RequiredActionUpdate actionUpdate)
    {
        string responseJson = await GetActionResponse(thread, actionUpdate);

        return _assistantClient.SubmitToolOutputsToRunStreamingAsync(
            threadId: thread.Id,
            runId: actionUpdate.Value.Id,
            toolOutputs: [new ToolOutput(actionUpdate.ToolCallId, responseJson)]);
    }

    private async Task<string> GetActionResponse(AssistantThread thread, RequiredActionUpdate actionUpdate)
    {
        return actionUpdate.FunctionName switch
        {
            nameof(NavigateToAsset) => await HandleNavigateToAsset(actionUpdate),
            nameof(GetTimeSeries) => await HandleGetTimeSeries(thread, actionUpdate),
            _ => "{}"
        };
    }

    private async Task<string> HandleNavigateToAsset(RequiredActionUpdate actionUpdate)
    {
        var command = JsonConvert.DeserializeObject<NavigateToAssetCommand>(actionUpdate.FunctionArguments);
        var response = await NavigateToAsset(command ?? throw new ArgumentNullException(nameof(command)));
        return JsonConvert.SerializeObject(response);
    }

    private async Task<string> HandleGetTimeSeries(AssistantThread thread, RequiredActionUpdate actionUpdate)
    {
        var command = JsonConvert.DeserializeObject<GetTimeSeriesCommand>(actionUpdate.FunctionArguments);
        var response = await GetTimeSeries(thread, command ?? throw new ArgumentNullException(nameof(command)));
        return JsonConvert.SerializeObject(response);
    }

    private async Task<NavigateToAssetResponse> NavigateToAsset(NavigateToAssetCommand command)
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

    private async Task<GetTimeSeriesResponse> GetTimeSeries(AssistantThread thread, GetTimeSeriesCommand command)
    {
        var response = new GetTimeSeriesResponse
        {
            AssetId = FindAsset(command)?.Id,
            Found = false
        };

        response.Found = response.AssetId.HasValue;
        if (!response.Found) return response;

        var timeSeriesPath = _config.TimeSeriesPath ?? throw new ArgumentNullException(nameof(_config.TimeSeriesPath));

        if (Environment.GetEnvironmentVariable("USE_THREAD_FILES") != "1")
        {
            response.Content = await File.ReadAllTextAsync(timeSeriesPath);
            return response;
        }

        return await HandleThreadFiles(thread, timeSeriesPath, response);
    }

    private AssetEntity? FindAsset(GetTimeSeriesCommand command)
    {
        if (command.AssetId is not null && Guid.TryParse(command.AssetId, out var assetId))
        {
            return DataStore.Assets.FirstOrDefault(a => a.Id == assetId);
        }

        if (!string.IsNullOrWhiteSpace(command.AssetName))
        {
            return DataStore.Assets.FirstOrDefault(a =>
                string.Equals(a.Name, command.AssetName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private async Task<GetTimeSeriesResponse> HandleThreadFiles(
        AssistantThread thread,
        string timeSeriesPath,
        GetTimeSeriesResponse response)
    {
        const string KeyVectorStoreId = nameof(KeyVectorStoreId);
        const string KeyTimeSeriesFileId = nameof(KeyTimeSeriesFileId);

        var fileName = Path.GetFileName(timeSeriesPath);
        var uploadResult = await _fileService.UploadFile(
            File.OpenRead(timeSeriesPath),
            fileName,
            FileUploadPurpose.Assistants);

        if (!thread.Metadata.TryGetValue(KeyVectorStoreId, out var vectorStoreId))
        {
            await CreateNewVectorStore(thread, uploadResult.Value.Id, KeyTimeSeriesFileId, KeyVectorStoreId);
        }
        else
        {
            await UpdateExistingVectorStore(vectorStoreId, KeyTimeSeriesFileId, uploadResult.Value.Id);
        }

        response.FileName = fileName;
        return response;
    }

    private async Task CreateNewVectorStore(
        AssistantThread thread,
        string fileId,
        string keyTimeSeriesFileId,
        string keyVectorStoreId)
    {
        var vectorOptions = new VectorStoreCreationOptions
        {
            Metadata = { [keyTimeSeriesFileId] = fileId },
            FileIds = { fileId }
        };

        var result = await _vectorStoreClient.CreateVectorStoreAsync(
            waitUntilCompleted: true,
            vectorStore: vectorOptions);

        await _assistantClient.ModifyThreadAsync(thread.Id, new ThreadModificationOptions
        {
            Metadata = { [keyVectorStoreId] = result.VectorStoreId },
            ToolResources = new ToolResources
            {
                FileSearch = new FileSearchToolResources
                {
                    VectorStoreIds = { result.VectorStoreId }
                }
            }
        });
    }

    private async Task UpdateExistingVectorStore(
        string vectorStoreId,
        string keyTimeSeriesFileId,
        string newFileId)
    {
        var vectorStore = _vectorStoreClient.GetVectorStore(vectorStoreId).Value;
        if (vectorStore.Metadata.TryGetValue(keyTimeSeriesFileId, out var oldFileId))
        {
            await _fileService.DeleteFile(oldFileId);
            await _vectorStoreClient.RemoveFileFromStoreAsync(vectorStoreId, oldFileId);
        }

        await _vectorStoreClient.AddFileToVectorStoreAsync(
            vectorStoreId,
            fileId: newFileId,
            waitUntilCompleted: true);
    }

    // Implementation of other interface methods...
}