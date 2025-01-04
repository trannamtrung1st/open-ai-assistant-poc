using AssistantPoc.Core.Models;

namespace AssistantPoc.WebApi.Models;

public class ChatResponse
{
    public required string Content { get; set; }
    public required string SessionId { get; set; }
    public CommandResult? CommandResult { get; set; }
}

public class CommandResult
{
    public required string Command { get; set; }
    public required object Data { get; set; }
} 