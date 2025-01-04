using OpenAI.Assistants;

namespace AssistantPoc.Core.Interfaces;

public interface IAssistantService
{
    Task CreateAssistant();
    Task RunThread(string? assistantId = null);
    Task<bool> Prompt(AssistantThread thread);
    string AppendPromptMetadata(string prompt);
}