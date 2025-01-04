namespace AssistantPoc.Core.Configuration;

public class AssistantConfiguration
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string KnowledgeBasePath { get; set; } = string.Empty;
    public string InstructionsPath { get; set; } = string.Empty;
    public string TimeSeriesPath { get; set; } = string.Empty;
    public string AssistantId { get; set; } = string.Empty;
    public string? ImageOutputPath { get; set; }
}