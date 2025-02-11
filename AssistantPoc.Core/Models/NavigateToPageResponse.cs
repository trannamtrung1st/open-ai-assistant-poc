namespace AssistantPoc.Core.Models;

public class NavigateToPageResponse : BaseResponse
{
    public Guid? SubscriptionId { get; set; }
    public Guid? ProjectId { get; set; }
    public string? Application { get; set; }
    public string? Page { get; set; }
    public Dictionary<string, string>? Params { get; set; }
}
