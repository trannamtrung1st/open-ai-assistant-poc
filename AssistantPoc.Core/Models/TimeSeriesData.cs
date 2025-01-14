namespace AssistantPoc.Core.Models;

public class TimeSeriesData
{
    public DateTime Timestamp { get; set; }
    public Dictionary<string, double> Values { get; set; } = new();
}