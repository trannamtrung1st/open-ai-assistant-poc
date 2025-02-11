namespace AssistantPoc.Core.Models;

public class SearchSubscriptionResponse : BaseResponse
{
    public IEnumerable<SubscriptionEntity> Data { get; set; } = [];
}