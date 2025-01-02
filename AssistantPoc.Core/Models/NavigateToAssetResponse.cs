namespace AssistantPoc.Core.Models;

public class NavigateToAssetResponse
{
    public string Command => "NavigateToAsset";
    public Guid? AssetId { get; set; }
    public bool Found { get; set; }
}