using AssistantPoc.Core.Interfaces;
using OpenAI.Files;
using System.ClientModel;

namespace AssistantPoc.Core.Services;

public class AssistantFileService : IAssistantFileService
{
    private readonly OpenAIFileClient _fileClient;

    public AssistantFileService(OpenAIFileClient fileClient)
    {
        _fileClient = fileClient;
    }

    public async Task<List<string>> UploadKnowledgeBaseFiles(string knowledgeBasePath, CancellationToken cancellationToken = default)
    {
        var directory = new DirectoryInfo(knowledgeBasePath);
        var files = directory.GetFiles();
        var fileIds = new List<string>();

        foreach (var file in files)
        {
            var uploadResult = await UploadFile(
                file.OpenRead(),
                file.Name,
                FileUploadPurpose.Assistants,
                cancellationToken: cancellationToken);

            fileIds.Add(uploadResult.Value.Id);
        }

        return fileIds;
    }

    public Task<ClientResult<BinaryData>> DownloadFile(string fileId, CancellationToken cancellationToken = default)
        => _fileClient.DownloadFileAsync(fileId, cancellationToken: cancellationToken);

    public Task DeleteFile(string fileId, CancellationToken cancellationToken = default)
        => _fileClient.DeleteFileAsync(fileId, cancellationToken: cancellationToken);

    public Task<ClientResult<OpenAIFile>> UploadFile(Stream fileStream, string fileName, FileUploadPurpose purpose, CancellationToken cancellationToken = default)
        => _fileClient.UploadFileAsync(fileStream, fileName, purpose, cancellationToken: cancellationToken);
}