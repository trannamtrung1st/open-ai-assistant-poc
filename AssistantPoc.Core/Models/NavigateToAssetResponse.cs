namespace AssistantPoc.Core.Models;

public class NavigateToAssetResponse
{
    public Guid? AssetId { get; set; }
    public bool Found { get; set; }
}