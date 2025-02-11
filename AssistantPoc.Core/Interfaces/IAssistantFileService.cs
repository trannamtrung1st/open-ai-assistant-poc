using System.ClientModel;
using OpenAI.Files;

namespace AssistantPoc.Core.Interfaces;

public interface IAssistantFileService
{
    Task<List<string>> UploadKnowledgeBaseFiles(string knowledgeBasePath, CancellationToken cancellationToken = default);
    Task<ClientResult<BinaryData>> DownloadFile(string fileId, CancellationToken cancellationToken = default);
    Task DeleteFile(string fileId, CancellationToken cancellationToken = default);
    Task<ClientResult<OpenAIFile>> UploadFile(Stream fileStream, string fileName, FileUploadPurpose purpose, CancellationToken cancellationToken = default);
}