using AssistantPoc.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AssistantPoc.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FileController : ControllerBase
{
    private readonly IAssistantFileService _fileService;
    private readonly ILogger<FileController> _logger;

    public FileController(
        IAssistantFileService fileService,
        ILogger<FileController> logger)
    {
        _fileService = fileService;
        _logger = logger;
    }

    [HttpGet("{fileId}")]
    public async Task<IActionResult> GetFile(string fileId)
    {
        try
        {
            var result = await _fileService.DownloadFile(fileId);
            if (result.Value == null)
            {
                return NotFound();
            }

            return File(result.Value.ToStream(), "image/png"); // Adjust content type if needed
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file {FileId}", fileId);
            return StatusCode(500, new { error = "Failed to download file" });
        }
    }
} 