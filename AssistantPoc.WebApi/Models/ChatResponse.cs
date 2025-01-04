using AssistantPoc.Core.Models;

namespace AssistantPoc.WebApi.Models;

public class ChatResponse
{
    public required string Content { get; set; }
    public required string SessionId { get; set; }
    public NavigateToAssetResponse? NavigateToAsset { get; set; }
} 