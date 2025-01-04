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
using System.Text;
using System.Threading;

namespace AssistantPoc.Core.Services;

public class AssistantService : IAssistantService
{
    private readonly AzureOpenAIClient _client;
    private readonly AssistantClient _assistantClient;
    private readonly VectorStoreClient _vectorStoreClient;
    private readonly OpenAIFileClient _fileClient;
    private readonly IFileService _fileService;
    private readonly AssistantConfiguration _config;
    private readonly Dictionary<string, AssistantThread> _threads = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private NavigateToAssetResponse? _lastNavigationCommand;
    private const string ImagePlaceholderFormat = "{{image:{0}}}";

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

    public async Task RunConsoleThread(string? assistantId = null)
    {
        assistantId ??= _config.AssistantId;
        var thread = await _assistantClient.CreateThreadAsync(new ThreadCreationOptions());
        bool hasPrompt = true;

        while (hasPrompt)
        {
            var response = await RunThreadOnce(thread.Value, assistantId, () =>
            {
                Console.Write("[Assistant] ");
                return (content) => Console.Write(content);
            });

            hasPrompt = await GetConsolePrompt(thread.Value);
        }

        await _assistantClient.DeleteThreadAsync(thread.Value.Id);
    }

    public async Task<(string Content, NavigateToAssetResponse? NavigateCommand)> RunThreadOnce(
        AssistantThread thread, 
        string? assistantId,
        Func<Action<MessageStatusUpdate>> onMessageCreated)
    {
        _lastNavigationCommand = null;
        assistantId ??= _config.AssistantId;
        var responseBuilder = new StringBuilder();

        var streamingResult = _assistantClient.CreateRunStreamingAsync(
            thread.Id,
            assistantId,
            new RunCreationOptions());

        do
        {
            streamingResult = await ReadStreamingResult(thread, streamingResult, onMessageCreated,
                content => responseBuilder.Append(content));
        } while (streamingResult is not null);

        return (responseBuilder.ToString(), _lastNavigationCommand);
    }

    private async Task<bool> GetConsolePrompt(AssistantThread thread)
    {
        Console.WriteLine();
        Console.Write("[User] ");
        string? prompt = Console.ReadLine();
        if (string.IsNullOrEmpty(prompt))
            return false;

        await AddPrompt(thread, prompt);
        return true;
    }

    public async Task AddPrompt(AssistantThread thread, string message)
    {
        var prompt = AppendPromptMetadata(message);
        await _assistantClient.CreateMessageAsync(thread.Id, MessageRole.User,
            content: [MessageContent.FromText(prompt)]);
    }

    private async Task<AsyncCollectionResult<StreamingUpdate>?> ReadStreamingResult(
        AssistantThread thread,
        AsyncCollectionResult<StreamingUpdate> streamingResult,
        Func<Action<MessageStatusUpdate>> onMessageCreated,
        Action<string> onContent)
    {
        await foreach (StreamingUpdate update in streamingResult)
        {
            switch (update)
            {
                case MessageStatusUpdate messageUpdate when
                    update.UpdateKind == StreamingUpdateReason.MessageCreated:
                    if (messageUpdate.Value.Role == MessageRole.Assistant)
                    {
                        var content = messageUpdate.Value.Content
                            .Select(c => c.ImageFileId is null ? 
                                c.Text : 
                                string.Format(ImagePlaceholderFormat, $"/api/file/{c.ImageFileId}"))
                            .Where(c => !string.IsNullOrEmpty(c));
                        onMessageCreated()?.Invoke(messageUpdate);
                    }
                    break;

                case MessageContentUpdate contentUpdate:
                    var text = contentUpdate.ImageFileId is null ? 
                        contentUpdate.Text : 
                        string.Format(ImagePlaceholderFormat, $"/api/file/{contentUpdate.ImageFileId}");
                    onContent(text);
                    break;

                case RunUpdate runUpdate when update.UpdateKind == StreamingUpdateReason.RunFailed:
                    var errorMessage = $"[Error] {runUpdate.Value.LastError?.Message ?? "Unknown error"}";
                    onContent(errorMessage);
                    return null;

                case RequiredActionUpdate actionUpdate:
                    return await HandleActionUpdate(thread, actionUpdate);
            }
        }
        return null;
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
            assetEntity = DataStore.Assets.FirstOrDefault(a => 
                string.Equals(a.Name, command.AssetName, StringComparison.OrdinalIgnoreCase));
        }
        var response = new NavigateToAssetResponse
        {
            AssetId = assetEntity?.Id,
            Found = assetEntity is not null
        };
        _lastNavigationCommand = response;
        return response;
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

    private string AppendPromptMetadata(string prompt) =>
        $"{prompt}\n---\nCurrent time: {DateTime.UtcNow:o}";

    public async Task<AssistantThread> GetOrCreateThread(string? sessionId = null)
    {
        await _lock.WaitAsync();
        try
        {
            sessionId ??= Guid.NewGuid().ToString();
            
            if (!_threads.TryGetValue(sessionId, out var thread))
            {
                var result = await _assistantClient.CreateThreadAsync(new ThreadCreationOptions());
                thread = result.Value;
                _threads[sessionId] = thread;
            }

            return thread;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void RemoveThread(string sessionId)
    {
        _threads.Remove(sessionId);
    }

    // Implementation of other interface methods...
}