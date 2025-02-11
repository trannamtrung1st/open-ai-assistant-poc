namespace AssistantPoc.Core.Configuration;

public class AssistantConfiguration
{
    public string AssistantName { get; set; } = "AHI AI Assistant";
    public float Temperature { get; set; } = 0.1f;
    public string Version { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string InstructionsPath { get; set; } = string.Empty;
    public TimeSpan CacheExpiry { get; set; } = TimeSpan.FromMinutes(30);

    // [NOTE] Demo only
    public string AssistantId { get; set; } = string.Empty;
    public string KnowledgeBasePath { get; set; } = string.Empty;
    public string TimeSeriesPath { get; set; } = string.Empty;
    public string? ImageOutputPath { get; set; }
}