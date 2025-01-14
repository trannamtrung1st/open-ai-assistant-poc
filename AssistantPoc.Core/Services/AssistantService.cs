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
    private readonly List<TimeSeriesData> _timeSeriesData = new();
    private readonly object _timeSeriesLock = new();

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
            Console.Write("[Assistant] ");

            var response = await RunThreadOnce(thread.Value, assistantId,
                onContent: Console.Write);

            hasPrompt = await GetConsolePrompt(thread.Value);
        }

        await _assistantClient.DeleteThreadAsync(thread.Value.Id);
    }

    public async Task<(string Content, NavigateToAssetResponse? NavigateCommand)> RunThreadOnce(
        AssistantThread thread,
        string? assistantId,
        Func<Action<MessageStatusUpdate>>? onMessageCreated = null,
        Action<string>? onContent = null)
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
                content =>
                {
                    responseBuilder.Append(content);
                    onContent?.Invoke(content);
                });
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
        Func<Action<MessageStatusUpdate>>? onMessageCreated,
        Action<string>? onContent)
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
        _lastNavigationCommand = response;
        return response;
    }

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
            return response;
        }

        return await HandleThreadFiles(thread, response);
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
}

public class SeriesConfig
{
    public required string Name { get; set; }
    public double BaseValue { get; set; }
    public double NormalVariation { get; set; }
    public double AnomalyVariation { get; set; }
    public double AnomalyChance { get; set; }
}