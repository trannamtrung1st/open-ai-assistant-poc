namespace AssistantPoc.Core.Models;

public class SearchProjectResponse : BaseResponse
{
    public IEnumerable<ProjectEntity>? Data { get; set; }
}