using Azure.AI.OpenAI;
using AssistantPoc.Core.Interfaces;
using OpenAI.Files;
using System.ClientModel;

namespace AssistantPoc.Core.Services;

public class FileService : IFileService
{
    private readonly OpenAIFileClient _fileClient;

    public FileService(OpenAIFileClient fileClient)
    {
        _fileClient = fileClient;
    }

    public async Task<List<string>> UploadKnowledgeBaseFiles(string knowledgeBasePath)
    {
        var directory = new DirectoryInfo(knowledgeBasePath);
        var files = directory.GetFiles();
        var fileIds = new List<string>();

        foreach (var file in files)
        {
            var uploadResult = await UploadFile(
                file.OpenRead(),
                file.Name,
                FileUploadPurpose.Assistants);

            fileIds.Add(uploadResult.Value.Id);
        }

        return fileIds;
    }

    public Task<ClientResult<BinaryData>> DownloadFile(string fileId)
        => _fileClient.DownloadFileAsync(fileId);

    public Task DeleteFile(string fileId)
        => _fileClient.DeleteFileAsync(fileId);

    public Task<ClientResult<OpenAIFile>> UploadFile(Stream fileStream, string fileName, FileUploadPurpose purpose)
        => _fileClient.UploadFileAsync(fileStream, fileName, purpose);
} 