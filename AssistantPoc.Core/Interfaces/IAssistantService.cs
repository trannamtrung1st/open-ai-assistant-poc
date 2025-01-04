using AssistantPoc.Core.Models;
using OpenAI.Assistants;

namespace AssistantPoc.Core.Interfaces;

public interface IAssistantService
{
    Task CreateAssistant();
    Task RunConsoleThread(string? assistantId = null);
    Task<(string Content, NavigateToAssetResponse? NavigateCommand)> RunThreadOnce(
        AssistantThread thread, 
        string? assistantId,
        Func<Action<MessageStatusUpdate>> onMessageCreated);
    Task AddPrompt(AssistantThread thread, string message);
    Task<AssistantThread> GetOrCreateThread(string? sessionId = null);
    void RemoveThread(string sessionId);
}