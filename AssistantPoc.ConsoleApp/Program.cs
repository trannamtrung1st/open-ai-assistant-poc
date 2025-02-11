using AssistantPoc.Core.Extensions;
using AssistantPoc.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMemoryCache();
builder.Services.AddAssistantServices(options =>
{
    options.Endpoint = "";
    options.ApiKey = File.ReadAllText("Local/key.txt");
    options.InstructionsPath = "Documents/Instructions.md";
    options.KnowledgeBasePath = "Documents/KnowledgeBase";
    options.TimeSeriesPath = "Documents/Responses/GetTimeSeries.txt";
    options.AssistantId = "";
});

var host = builder.Build();

var assistantService = host.Services.GetRequiredService<IAssistantService>();
await assistantService.CreateAssistant();
// await assistantService.RunConsoleThread();
