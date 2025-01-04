using AssistantPoc.Core.Extensions;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Assistant API", Version = "v1" });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

builder.Services.AddAssistantServices(options =>
{
    options.Endpoint = builder.Configuration["Assistant:Endpoint"]!;
    options.ApiKey = builder.Configuration["Assistant:ApiKey"]!;
    options.InstructionsPath = "../Documents/Instructions.md";
    options.KnowledgeBasePath = "../Documents/KnowledgeBase";
    options.TimeSeriesPath = "../Documents/Responses/GetTimeSeries.txt";
    options.AssistantId = builder.Configuration["Assistant:AssistantId"]!;
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

app.Run();
