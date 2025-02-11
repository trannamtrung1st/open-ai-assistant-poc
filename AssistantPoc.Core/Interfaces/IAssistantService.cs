using AssistantPoc.Core.Models;
using OpenAI.Assistants;

namespace AssistantPoc.Core.Interfaces;

public interface IAssistantService
{
    Task<string> CreateAssistant(CancellationToken cancellationToken = default);
    Task DeleteAssistant(string assistantId, CancellationToken cancellationToken = default);
    Task<(string Content, IEnumerable<CommandResult>? Results)> RunThreadOnce(
        AssistantThread thread,
        string? assistantId,
        Func<Action<MessageStatusUpdate>> onMessageCreated,
        Action<string>? onContent = null,
        CancellationToken cancellationToken = default);
    Task AddPrompt(AssistantThread thread, string message, CancellationToken cancellationToken = default);
    Task<AssistantThread> GetOrCreateThread(string? sessionId = null, CancellationToken cancellationToken = default);
    Task RemoveThread(string sessionId, CancellationToken cancellationToken = default);

    // [NOTE] Demo only
    Task RunConsoleThread(string? assistantId = null, CancellationToken cancellationToken = default);
    Task<int> GetTokenCount(string sessionId, CancellationToken cancellationToken = default);
}