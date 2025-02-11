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
using System.Globalization;
using AssistantPoc.Core.Constants;
using Microsoft.Extensions.Caching.Memory;

namespace AssistantPoc.Core.Services;

public class AssistantService : IAssistantService
{
    private readonly AzureOpenAIClient _client;
    private readonly AssistantClient _assistantClient;
    private readonly VectorStoreClient _vectorStoreClient;
    private readonly OpenAIFileClient _fileClient;
    private readonly IAssistantFileService _fileService;
    private readonly IMemoryCache _memoryCache;
    private readonly AssistantConfiguration _config;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private const string ImagePlaceholderFormat = "{{image:{0}}}";
    private readonly List<TimeSeriesData> _timeSeriesData = new();
    private readonly object _timeSeriesLock = new();

    public AssistantService(
        AzureOpenAIClient client,
        AssistantClient assistantClient,
        VectorStoreClient vectorStoreClient,
        OpenAIFileClient fileClient,
        IAssistantFileService fileService,
        IMemoryCache memoryCache,
        IOptions<AssistantConfiguration> options)
    {
        _client = client;
        _assistantClient = assistantClient;
        _vectorStoreClient = vectorStoreClient;
        _fileClient = fileClient;
        _fileService = fileService;
        _memoryCache = memoryCache;
        _config = options.Value;
    }

    public async Task<string> CreateAssistant(CancellationToken cancellationToken = default)
    {
        var instructionsContent = await File.ReadAllTextAsync(_config.InstructionsPath);
        var assistantOptions = CreateAssistantOptions(instructionsContent);
        var assistantResult = await _assistantClient.CreateAssistantAsync("gpt-4o", assistantOptions, cancellationToken);
        if (assistantResult.Value is null)
            throw new Exception("Failed to create assistant");
        var assistantId = assistantResult.Value.Id;
        return assistantId;
    }

    public async Task DeleteAssistant(string assistantId, CancellationToken cancellationToken = default)
    {
        await _assistantClient.DeleteAssistantAsync(assistantId, cancellationToken);
    }

    public async Task RunConsoleThread(string? assistantId = null, CancellationToken cancellationToken = default)
    {
        assistantId ??= _config.AssistantId;
        var thread = await _assistantClient.CreateThreadAsync(new ThreadCreationOptions(), cancellationToken: cancellationToken);
        bool hasPrompt = true;

        while (hasPrompt)
        {
            Console.Write("[Assistant] ");

            var response = await RunThreadOnce(thread.Value, assistantId,
                onContent: Console.Write,
                cancellationToken: cancellationToken);

            hasPrompt = await GetConsolePrompt(thread.Value, cancellationToken: cancellationToken);
        }

        await _assistantClient.DeleteThreadAsync(thread.Value.Id, cancellationToken: cancellationToken);
    }

    public async Task<(string Content, IEnumerable<CommandResult>? Results)> RunThreadOnce(
        AssistantThread thread,
        string? assistantId,
        Func<Action<MessageStatusUpdate>>? onMessageCreated = null,
        Action<string>? onContent = null,
        CancellationToken cancellationToken = default)
    {
        assistantId ??= _config.AssistantId;
        var responseBuilder = new StringBuilder();
        IEnumerable<CommandResult>? commandResults = null;
        var streamingResult = _assistantClient.CreateRunStreamingAsync(
            thread.Id,
            assistantId,
            new RunCreationOptions(),
            cancellationToken: cancellationToken);

        do
        {
            streamingResult = await ReadStreamingResult(thread, streamingResult, onMessageCreated,
                onCommandResult: (result) => commandResults = result,
                onContent: content =>
                {
                    responseBuilder.Append(content);
                    onContent?.Invoke(content);
                },
                cancellationToken: cancellationToken);
        } while (streamingResult is not null);

        return (responseBuilder.ToString(), commandResults);
    }

    public async Task AddPrompt(AssistantThread thread, string message, CancellationToken cancellationToken = default)
    {
        var prompt = AppendPromptMetadata(message);
        await _assistantClient.CreateMessageAsync(thread.Id, MessageRole.User,
            content: [MessageContent.FromText(prompt)],
            cancellationToken: cancellationToken);
    }

    public async Task<AssistantThread> GetOrCreateThread(string? sessionId = null, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync();
        try
        {
            AssistantThread thread;

            if (sessionId is null)
            {
                var result = await _assistantClient.CreateThreadAsync(new ThreadCreationOptions(), cancellationToken);
                thread = result.Value;
            }
            else
            {
                thread = await _memoryCache.GetOrCreateAsync(sessionId, async (entry) =>
                {
                    entry.AbsoluteExpirationRelativeToNow = _config.CacheExpiry;
                    var result = await _assistantClient.GetThreadAsync(sessionId, cancellationToken);
                    return result.Value;
                }) ?? throw new Exception("Invalid thread");
            }

            return thread;
        }
        finally
        {
            _lock.Release();
        }
    }

    private AssistantCreationOptions CreateAssistantOptions(string instructions)
    {
        var options = new AssistantCreationOptions
        {
            Name = _config.AssistantName,
            Description = _config.Version,
            Instructions = instructions,
            Tools = { },
            ToolResources = new(),
            Temperature = _config.Temperature,
            ResponseFormat = AssistantResponseFormat.Auto
        };

        AddTools(options.Tools);
        return options;
    }

    private void AddTools(IList<ToolDefinition> tools)
    {
        tools.Add(CreateNavigateToPageTool());
        tools.Add(CreateSwitchSubscriptionTool());
        tools.Add(CreateSwitchProjectTool());
        tools.Add(CreateSwitchApplicationTool());
        tools.Add(CreateSearchAssetTool());
        tools.Add(CreateSearchDeviceTool());
        tools.Add(CreateSearchSubscriptionTool());
        tools.Add(CreateSearchProjectTool());
    }

    private FunctionToolDefinition CreateNavigateToPageTool()
    {
        return new FunctionToolDefinition("NavigateToPage")
        {
            Description = "Navigate to a specific page in the AHI platform",
            Parameters = BinaryData.FromString(
"""
{
    "type": "object",
    "properties": {
        "subscriptionId": {
            "type": "string",
            "description": "Id (GUID) of the subscription to navigate to"
        },
        "projectId": {
            "type": "string",
            "description": "Id (GUID) of the project to navigate to"
        },
        "params": {
            "type": "string",
            "description": "the parameters required for the page, if backend returns exact ids, use the ids as values instead of user's input. The params is a json string with this format:\r\n    ```json\r\n    {\r\n      \"param1\": \"value1\",\r\n      \"param2\": \"value2\"\r\n    }\r\n    ```\r\n"
        },
        "application": {
            "type": "string",
            "description": "the application to navigate to",
            "enum": [
                "DataManagement",
                "DataInsight"
            ]
        },
        "page": {
            "type": "string",
            "description": "the page to navigate to",
            "enum": [
                "ASSET_LIST_TREE",
                "EDIT_ASSET",
                "ASSET_TEMPLATE_LIST",
                "ADD_ASSET_TEMPLATE",
                "EDIT_ASSET_TEMPLATE",
                "IMPORT_ASSET_TEMPLATE",
                "TABLE_LIST",
                "MEDIA_LIST",
                "BLOCK_TEMPLATE_LIST",
                "ADD_BLOCK_TEMPLATE",
                "EDIT_BLOCK_TEMPLATE",
                "BLOCK_EXECUTION_LIST",
                "ADD_BLOCK_EXECUTION",
                "EDIT_BLOCK_EXECUTION",
                "DEVICE_LIST",
                "ADD_DEVICE",
                "EDIT_DEVICE",
                "IMPORT_DEVICE",
                "DEVICE_TEMPLATE_LIST",
                "ADD_DEVICE_TEMPLATE",
                "EDIT_DEVICE_TEMPLATE",
                "IMPORT_DEVICE_TEMPLATE",
                "ALARM_LIST",
                "ALARM_HISTORY_LIST",
                "TIMELINE_VIEW",
                "RULE_LIST",
                "ADD_ALARM_RULE",
                "EDIT_ALARM_RULE",
                "IMPORT_ALARM_RULE",
                "ACTION_LIST",
                "ADD_ACTION",
                "EDIT_ACTION",
                "IMPORT_ACTION",
                "BROKER_LIST",
                "ADD_BROKER",
                "EDIT_BROKER",
                "IMPORT_BROKER",
                "INTEGRATION_LIST",
                "ADD_INTEGRATION",
                "EDIT_INTEGRATION",
                "UOM_LIST",
                "ADD_UOM",
                "EDIT_UOM",
                "IMPORT_UOM",
                "EVENT_FORWARDING_LIST",
                "ADD_EVENT_FORWARDING",
                "EDIT_EVENT_FORWARDING",
                "USER_LIST",
                "ADD_USER",
                "EDIT_USER",
                "GROUP_LIST",
                "ADD_GROUP",
                "EDIT_GROUP",
                "ROLE_LIST",
                "EDIT_ROLE",
                "API_CLIENT_LIST",
                "ADD_API_CLIENT",
                "EDIT_API_CLIENT",
                "ACTIVITY_LOG_LIST",
                "ACTIVITY_LOG_DETAIL",
                "PROJECT_LIST",
                "DASHBOARD_LIST",
                "EDIT_DASHBOARD",
                "DASHBOARD_TEMPLATE_LIST",
                "ADD_DASHBOARD_TEMPLATE",
                "EDIT_DASHBOARD_TEMPLATE",
                "IMPORT_DASHBOARD_TEMPLATE",
                "DASHBOARD_MEDIA_LIST",
                "EDIT_MEDIA",
                "REPORT_TEMPLATE",
                "REPORT_SCHEDULE",
                "REPORT_DETAIL",
                "REPORT_TEMPLATE_LIST",
                "ADD_REPORT_TEMPLATE",
                "EDIT_REPORT_TEMPLATE",
                "PREVIEW_REPORT_TEMPLATE",
                "REPORT_SCHEDULE_LIST",
                "ADD_REPORT_SCHEDULE",
                "EDIT_REPORT_SCHEDULE"
            ]
        }
    },
    "required": [
        "page",
        "application"
    ]
}
"""),
            StrictParameterSchemaEnabled = false
        };
    }

    private FunctionToolDefinition CreateSwitchSubscriptionTool()
    {
        return new FunctionToolDefinition("SwitchSubscription")
        {
            Description = "Switch to a different subscription",
            Parameters = BinaryData.FromString(
"""
{
    "type": "object",
    "properties": {
        "subscriptionName": {
            "type": "string",
            "description": "Name of the subscription to switch to"
        },
        "subscriptionId": {
            "type": "string",
            "description": "Id (GUID) of the subscription to switch to"
        }
    },
    "required": []
}
"""),
            StrictParameterSchemaEnabled = false
        };
    }

    private FunctionToolDefinition CreateSwitchProjectTool()
    {
        return new FunctionToolDefinition("SwitchProject")
        {
            Description = "Switch to a different project",
            Parameters = BinaryData.FromString(
"""
{
    "type": "object",
    "properties": {
        "projectName": {
            "type": "string",
            "description": "Name of the project to switch to"
        },
        "projectId": {
            "type": "string",
            "description": "Id (GUID) of the project to switch to"
        }
    },
    "required": []
}
"""),
            StrictParameterSchemaEnabled = false
        };
    }

    private FunctionToolDefinition CreateSwitchApplicationTool()
    {
        return new FunctionToolDefinition("SwitchApplication")
        {
            Description = "Switch to a different application",
            Parameters = BinaryData.FromString(
"""
{
    "type": "object",
    "properties": {
        "application": {
            "type": "string",
            "description": "The application to navigate to"
        }
    },
    "required": []
}
"""),
            StrictParameterSchemaEnabled = false
        };
    }

    private FunctionToolDefinition CreateSearchAssetTool()
    {
        return new FunctionToolDefinition("SearchAsset")
        {
            Description = "Search for assets in the AHI platform",
            Parameters = BinaryData.FromString(
"""
{
    "type": "object",
    "properties": {
        "projectId": {
            "type": "string",
            "description": "Id (GUID) of the project"
        },
        "term": {
            "type": "string",
            "description": "The term to search for"
        }
    },
    "required": []
}
"""),
            StrictParameterSchemaEnabled = false
        };
    }

    private FunctionToolDefinition CreateSearchDeviceTool()
    {
        return new FunctionToolDefinition("SearchDevice")
        {
            Description = "Search for devices in the AHI platform",
            Parameters = BinaryData.FromString(
"""
{
    "type": "object",
    "properties": {
        "projectId": {
            "type": "string",
            "description": "Id (GUID) of the project"
        },
        "term": {
            "type": "string",
            "description": "The term to search for"
        },
        "status": {
            "type": "string",
            "description": "The status of devices",
            "enum": [
                "connected",
                "disconnected",
                "unknown"
            ]
        }
    },
    "required": []
}
"""),
            StrictParameterSchemaEnabled = false
        };
    }

    private FunctionToolDefinition CreateSearchSubscriptionTool()
    {
        return new FunctionToolDefinition("SearchSubscription")
        {
            Description = "Search for subscriptions in the AHI platform",
            Parameters = BinaryData.FromString(
"""
{
    "type": "object",
    "properties": {
        "term": {
            "type": "string",
            "description": "The term to search for"
        }
    },
    "required": []
}
"""),
            StrictParameterSchemaEnabled = false
        };
    }

    private FunctionToolDefinition CreateSearchProjectTool()
    {
        return new FunctionToolDefinition("SearchProject")
        {
            Description = "Search for projects in the AHI platform",
            Parameters = BinaryData.FromString(
"""
{
    "type": "object",
    "properties": {
        "term": {
            "type": "string",
            "description": "The term to search for"
        }
    },
    "required": []
}
"""),
            StrictParameterSchemaEnabled = false
        };
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

    private async Task<bool> GetConsolePrompt(AssistantThread thread, CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.Write("[User] ");
        string? prompt = Console.ReadLine();
        if (string.IsNullOrEmpty(prompt))
            return false;

        await AddPrompt(thread, prompt, cancellationToken: cancellationToken);
        return true;
    }

    private async Task<AsyncCollectionResult<StreamingUpdate>?> ReadStreamingResult(
        AssistantThread thread,
        AsyncCollectionResult<StreamingUpdate> streamingResult,
        Func<Action<MessageStatusUpdate>>? onMessageCreated,
        Action<IEnumerable<CommandResult>>? onCommandResult,
        Action<string>? onContent,
        CancellationToken cancellationToken = default)
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
                        onMessageCreated?.Invoke()?.Invoke(messageUpdate);
                    }
                    break;

                case MessageContentUpdate contentUpdate:
                    var text = contentUpdate.ImageFileId is null ?
                        contentUpdate.Text :
                        string.Format(ImagePlaceholderFormat, $"/api/file/{contentUpdate.ImageFileId}");
                    onContent?.Invoke(text);
                    break;

                case RunUpdate runUpdate when update.UpdateKind == StreamingUpdateReason.RunFailed:
                    var errorMessage = $"[Error] {runUpdate.Value.LastError?.Message ?? "Unknown error"}";
                    onContent?.Invoke(errorMessage);
                    return null;

                case RequiredActionUpdate actionUpdate:
                    return await HandleActionUpdate(thread, actionUpdate, onCommandResult, cancellationToken);
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
        RequiredActionUpdate actionUpdate,
        Action<IEnumerable<CommandResult>>? onCommandResult,
        CancellationToken cancellationToken = default)
    {
        var requiredActions = actionUpdate.Value.RequiredActions;
        var toolOutputs = new List<ToolOutput>();
        var commandResults = new List<CommandResult>();
        var messageContext = new MessageContext();

        foreach (var action in requiredActions)
        {
            (string responseJson, CommandResult? commandResult) = await GetActionResponse(thread, action, messageContext, cancellationToken: cancellationToken);
            if (commandResult is not null)
                commandResults.Add(commandResult);
            toolOutputs.Add(new ToolOutput(action.ToolCallId, responseJson));
        }

        onCommandResult?.Invoke(commandResults);
        return _assistantClient.SubmitToolOutputsToRunStreamingAsync(
            threadId: thread.Id,
            runId: actionUpdate.Value.Id,
            toolOutputs: toolOutputs,
            cancellationToken: cancellationToken);
    }

    private async Task<(string Json, CommandResult? Result)> GetActionResponse(AssistantThread thread, RequiredAction action, MessageContext messageContext, CancellationToken cancellationToken = default)
    {
        object? response = action.FunctionName switch
        {
            nameof(NavigateToPage) => await HandleNavigateToPage(action, messageContext),
            nameof(SearchDevice) => await HandleSearchDevice(action, messageContext),
            nameof(SearchProject) => await HandleSearchProject(action, messageContext),
            nameof(SearchSubscription) => await HandleSearchSubscription(action, messageContext),

            // [NOTE] Obsolete
#pragma warning disable CS0612 // Type or member is obsolete
            nameof(NavigateToAsset) => await HandleNavigateToAsset(action),
            nameof(GetTimeSeries) => await HandleGetTimeSeries(thread, action),
#pragma warning restore CS0612 // Type or member is obsolete
            _ => null
        };

        return (JsonConvert.SerializeObject(response), response != null ? new CommandResult
        {
            Command = action.FunctionName,
            Data = response
        } : null);
    }

    private async Task<NavigateToPageResponse> HandleNavigateToPage(RequiredAction actionUpdate, MessageContext messageContext)
    {
        var command = JsonConvert.DeserializeObject<NavigateToPageCommand>(actionUpdate.FunctionArguments);
        var response = await NavigateToPage(command ?? throw new ArgumentNullException(nameof(command)), messageContext);
        return response;
    }

    private async Task<SearchDeviceResponse> HandleSearchDevice(RequiredAction actionUpdate, MessageContext messageContext)
    {
        var command = JsonConvert.DeserializeObject<SearchDeviceCommand>(actionUpdate.FunctionArguments);
        var response = await SearchDevice(command ?? throw new ArgumentNullException(nameof(command)), messageContext);
        return response;
    }

    private async Task<SearchProjectResponse> HandleSearchProject(RequiredAction actionUpdate, MessageContext messageContext)
    {
        var command = JsonConvert.DeserializeObject<SearchProjectCommand>(actionUpdate.FunctionArguments);
        var response = await SearchProject(command ?? throw new ArgumentNullException(nameof(command)), messageContext);
        return response;
    }

    private async Task<SearchSubscriptionResponse> HandleSearchSubscription(RequiredAction actionUpdate, MessageContext messageContext)
    {
        var command = JsonConvert.DeserializeObject<SearchSubscriptionCommand>(actionUpdate.FunctionArguments);
        var response = await SearchSubscription(command ?? throw new ArgumentNullException(nameof(command)), messageContext);
        return response;
    }

    [Obsolete]
    private async Task<NavigateToAssetResponse> HandleNavigateToAsset(RequiredAction actionUpdate)
    {
        var command = JsonConvert.DeserializeObject<NavigateToAssetCommand>(actionUpdate.FunctionArguments);
        var response = await NavigateToAsset(command ?? throw new ArgumentNullException(nameof(command)));
        return response;
    }

    [Obsolete]
    private async Task<GetTimeSeriesResponse> HandleGetTimeSeries(AssistantThread thread, RequiredAction actionUpdate)
    {
        var command = JsonConvert.DeserializeObject<GetTimeSeriesCommand>(actionUpdate.FunctionArguments);
        var response = await GetTimeSeries(thread, command ?? throw new ArgumentNullException(nameof(command)));
        return response;
    }

    private async Task<NavigateToPageResponse> NavigateToPage(NavigateToPageCommand command, MessageContext messageContext)
    {
        await Task.CompletedTask;
        var missingParams = new List<string>();
        var subscriptionId = Guid.TryParse(command.SubscriptionId, out var subId)
            ? subId : messageContext.SubscriptionId;
        var projectId = Guid.TryParse(command.ProjectId, out var projId)
            ? projId : messageContext.ProjectId;

        if (subscriptionId is null) missingParams.Add(nameof(command.SubscriptionId));
        if (projectId is null) missingParams.Add(nameof(command.ProjectId));

        if (missingParams.Count != 0)
        {
            return new NavigateToPageResponse
            {
                Status = AssistantResponseStatus.NEED_MORE_INFO,
                ForParams = missingParams
            };
        }

        return new NavigateToPageResponse()
        {
            Status = AssistantResponseStatus.SUCCESS,
            SubscriptionId = subscriptionId,
            ProjectId = projectId,
            Application = command.Application,
            Page = command.Page,
            Params = JsonConvert.DeserializeObject<Dictionary<string, string>>(command.Params ?? "{}")
        };
    }

    private async Task<SearchProjectResponse> SearchProject(SearchProjectCommand command, MessageContext messageContext)
    {
        await Task.CompletedTask;

        var projectEntities = DataStore.Projects;

        if (!string.IsNullOrWhiteSpace(command.Term))
        {
            projectEntities = projectEntities
                .Where(p => p.Name.Contains(command.Term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (projectEntities.Count == 1)
            messageContext.ProjectId = projectEntities[0].Id;

        return new SearchProjectResponse
        {
            Data = projectEntities,
            Status = AssistantResponseStatus.SUCCESS
        };
    }

    private async Task<SearchSubscriptionResponse> SearchSubscription(SearchSubscriptionCommand command, MessageContext messageContext)
    {
        await Task.CompletedTask;

        var subscriptionEntities = DataStore.Subscriptions;

        if (!string.IsNullOrWhiteSpace(command.Term))
        {
            subscriptionEntities = subscriptionEntities
                .Where(s => s.Name.Contains(command.Term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (subscriptionEntities.Count == 1)
            messageContext.SubscriptionId = subscriptionEntities[0].Id;

        return new SearchSubscriptionResponse
        {
            Data = subscriptionEntities,
            Status = AssistantResponseStatus.SUCCESS
        };
    }

    private async Task<SearchDeviceResponse> SearchDevice(SearchDeviceCommand command, MessageContext messageContext)
    {
        await Task.CompletedTask;
        var projectId = Guid.TryParse(command.ProjectId, out var projId)
            ? projId : messageContext.ProjectId;
        List<DeviceEntity> deviceEntities = DataStore.Devices;
        var missingParams = new List<string>();

        if (projectId is null) missingParams.Add(nameof(command.ProjectId));

        if (missingParams.Count != 0)
        {
            return new SearchDeviceResponse
            {
                Status = AssistantResponseStatus.NEED_MORE_INFO,
                ForParams = missingParams
            };
        }

        if (!string.IsNullOrWhiteSpace(command.Term))
        {
            deviceEntities = deviceEntities
                .Where(d => d.Name.Contains(command.Term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var response = new SearchDeviceResponse
        {
            Data = deviceEntities,
            Status = AssistantResponseStatus.SUCCESS
        };
        return response;
    }

    [Obsolete]
    private async Task<NavigateToAssetResponse> NavigateToAsset(NavigateToAssetCommand command)
    {
        await Task.CompletedTask;
        AssetEntity? assetEntity = null;

        void GetAssetByName(string name)
        {
            assetEntity = DataStore.Assets.FirstOrDefault(a =>
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        if (command.AssetId is not null)
        {
            if (Guid.TryParse(command.AssetId, out var assetId))
                assetEntity = DataStore.Assets.FirstOrDefault(a => a.Id == assetId);
            else GetAssetByName(command.AssetId);
        }
        else if (!string.IsNullOrWhiteSpace(command.AssetName))
            GetAssetByName(command.AssetName);

        var response = new NavigateToAssetResponse
        {
            AssetId = assetEntity?.Id,
            Found = assetEntity is not null
        };
        return response;
    }

    [Obsolete]
    private async Task<GetTimeSeriesResponse> GetTimeSeries(AssistantThread thread, GetTimeSeriesCommand command)
    {
        await Task.CompletedTask;
        var response = new GetTimeSeriesResponse
        {
            AssetId = FindAsset(command)?.Id,
            Found = false
        };

        response.Found = response.AssetId.HasValue;
        if (!response.Found) return response;

        var now = DateTime.UtcNow;
        var from = command.From ?? now.AddDays(-7);
        var to = command.To ?? now;

        if (Environment.GetEnvironmentVariable("USE_THREAD_FILES") != "1")
        {
            response.Content = GenerateTimeSeriesData(from, to);
            File.WriteAllText("../Local/timeseries.txt", response.Content);
            return response;
        }

        return await HandleThreadFiles(thread, response);
    }

    private async Task CreateAssistantDemo(CancellationToken cancellationToken = default)
    {
        var fileIds = await _fileService.UploadKnowledgeBaseFiles(_config.KnowledgeBasePath, cancellationToken);
        var instructionsContent = await File.ReadAllTextAsync(_config.InstructionsPath);

        var assistantOptions = CreateAssistantOptionsDemo(instructionsContent, fileIds);
        var assistantResult = await _assistantClient.CreateAssistantAsync("gpt-4o", assistantOptions, cancellationToken);

        if (assistantResult.Value is null)
            throw new Exception("Failed to create assistant");

        Console.WriteLine(assistantResult.Value.Id);
    }

    private AssistantCreationOptions CreateAssistantOptionsDemo(string instructions, List<string> fileIds)
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

        AddToolsDemo(options);
        return options;
    }

    private void AddToolsDemo(AssistantCreationOptions options)
    {
        options.Tools.Add(new FileSearchToolDefinition());
        options.Tools.Add(new CodeInterpreterToolDefinition());
        options.Tools.Add(CreateNavigateToAssetTool());
        options.Tools.Add(CreateGetTimeSeriesTool());
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
        GetTimeSeriesResponse response)
    {
        const string KeyVectorStoreId = nameof(KeyVectorStoreId);
        const string KeyTimeSeriesFileId = nameof(KeyTimeSeriesFileId);

        var timeSeriesPath = _config.TimeSeriesPath ?? throw new ArgumentNullException(nameof(_config.TimeSeriesPath));
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

    public async Task RemoveThread(string sessionId, CancellationToken cancellationToken)
    {
        await _assistantClient.DeleteThreadAsync(sessionId, cancellationToken: cancellationToken);
    }

    private void EnsureTimeSeriesData(int records, int intervalSeconds = 60)
    {
        lock (_timeSeriesLock)
        {
            if (_timeSeriesData.Any()) return;

            var random = new Random();
            var series = new List<SeriesConfig>
            {
                new() {
                    Name = "Temperature",
                    BaseValue = 23.0,
                    NormalVariation = 0.5,
                    AnomalyVariation = 5.0,
                    AnomalyChance = 0.05
                },
                new() {
                    Name = "Humidity",
                    BaseValue = 45.0,
                    NormalVariation = 1.0,
                    AnomalyVariation = 20.0,
                    AnomalyChance = 0.05
                },
                new() {
                    Name = "Pressure",
                    BaseValue = 1013.25,
                    NormalVariation = 2.0,
                    AnomalyVariation = 15.0,
                    AnomalyChance = 0.03
                },
                new() {
                    Name = "Velocity",
                    BaseValue = 5.0,
                    NormalVariation = 0.3,
                    AnomalyVariation = 3.0,
                    AnomalyChance = 0.08
                }
            };

            var baseTime = DateTime.UtcNow.AddHours(1);
            for (int i = 0; i < records; i++)
            {
                var data = new TimeSeriesData
                {
                    Timestamp = baseTime.AddSeconds(-i * intervalSeconds)
                };

                foreach (var s in series)
                {
                    data.Values[s.Name.ToLower()] = GenerateValue(random, s);
                }

                _timeSeriesData.Add(data);
            }

            _timeSeriesData.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        }
    }

    private string GenerateTimeSeriesData(DateTime from, DateTime to)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        EnsureTimeSeriesData(records: 50000, intervalSeconds: 60);

        var filteredData = _timeSeriesData
            .Where(d => d.Timestamp >= from && d.Timestamp <= to)
            .OrderBy(d => d.Timestamp)
            .ToList();

        if (!filteredData.Any())
            return string.Empty;

        var sb = new StringBuilder();

        // Header using the column names from the first record
        var columns = new[] { "timestamp" }.Concat(filteredData[0].Values.Keys).ToList();
        sb.AppendLine(string.Join(",", columns));

        // Data rows
        foreach (var data in filteredData)
        {
            var values = new[] { data.Timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ") }
                .Concat(columns.Skip(1).Select(c => data.Values[c].ToString("F2")));
            sb.AppendLine(string.Join(",", values));
        }

        return sb.ToString();
    }

    private double GenerateValue(Random random, SeriesConfig config)
    {
        var isAnomaly = random.NextDouble() < config.AnomalyChance;
        var variation = isAnomaly ? config.AnomalyVariation : config.NormalVariation;
        var delta = (random.NextDouble() * 2 - 1) * variation;
        var value = config.BaseValue + delta;

        return Math.Round(value, 2);
    }

    public async Task<int> GetTokenCount(string sessionId, CancellationToken cancellationToken = default)
    {
        var thread = await GetOrCreateThread(sessionId, cancellationToken);
        var tokenCount = _assistantClient.GetRuns(thread.Id).Select(r => r.Usage?.TotalTokenCount ?? 0).Sum();
        return tokenCount;
    }
}

public class SeriesConfig
{
    public required string Name { get; set; }
    public double BaseValue { get; set; }
    public double NormalVariation { get; set; }
    public double AnomalyVariation { get; set; }
    public double AnomalyChance { get; set; }
}