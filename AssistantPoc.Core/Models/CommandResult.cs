namespace AssistantPoc.Core.Models;

public class CommandResult
{
    public required string Command { get; set; }
    public object? Data { get; set; }
}