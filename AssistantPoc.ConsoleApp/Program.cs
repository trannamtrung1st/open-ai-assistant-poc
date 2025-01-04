using AssistantPoc.Core.Extensions;
using AssistantPoc.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddAssistantServices(options =>
{
    options.Endpoint = "https://assistant-poc.openai.azure.com/";
    options.ApiKey = File.ReadAllText("Local/key.txt");
    options.InstructionsPath = "Documents/Instructions.md";
    options.KnowledgeBasePath = "Documents/KnowledgeBase";
    options.TimeSeriesPath = "Documents/Responses/GetTimeSeries.txt";
    options.AssistantId = "asst_U8cliLiQlx1w1ol9nWtLfexp";
});

var host = builder.Build();

var assistantService = host.Services.GetRequiredService<IAssistantService>();
// await assistantService.CreateAssistant();
await assistantService.RunThread();
