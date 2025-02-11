namespace AssistantPoc.Core.Models;

public class ProjectEntity
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required Guid SubscriptionId { get; set; }
}

