namespace AssistantPoc.Core.Models;

public class NavigateToPageCommand
{
    public string? SubscriptionId { get; set; }
    public string? ProjectId { get; set; }
    public string? Application { get; set; }
    public string? Page { get; set; }
    public string? Params { get; set; }
}
