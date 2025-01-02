namespace AssistantPoc.Core.Models;

public class GetTimeSeriesCommand
{
    public string? AssetName { get; set; }
    public string? AssetId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
