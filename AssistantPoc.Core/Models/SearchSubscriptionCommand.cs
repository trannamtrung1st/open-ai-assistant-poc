namespace AssistantPoc.Core.Models;

public class SearchSubscriptionCommand
{
    public string Command => "SearchSubscription";
    public string? Term { get; set; }
}
