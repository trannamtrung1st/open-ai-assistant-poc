using System.ClientModel;
using OpenAI.Files;

public interface IFileService
{
    Task<List<string>> UploadKnowledgeBaseFiles(string knowledgeBasePath);
    Task<ClientResult<BinaryData>> DownloadFile(string fileId);
    Task DeleteFile(string fileId);
    Task<ClientResult<OpenAIFile>> UploadFile(Stream fileStream, string fileName, FileUploadPurpose purpose);
}