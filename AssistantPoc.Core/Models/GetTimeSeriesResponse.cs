namespace AssistantPoc.Core.Models;

public class GetTimeSeriesResponse
{
    public string Command => "GetTimeSeries";
    public Guid? AssetId { get; set; }
    public bool Found { get; set; }
    public string? FileName { get; set; }
    public string? Content { get; set; }
}