using AssistantPoc.Core.Interfaces;
using AssistantPoc.WebApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace AssistantPoc.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AssistantsController : ControllerBase
{
    private readonly IAssistantService _assistantService;
    private readonly ILogger<AssistantsController> _logger;

    public AssistantsController(
        IAssistantService assistantService,
        ILogger<AssistantsController> logger)
    {
        _assistantService = assistantService;
        _logger = logger;
    }

    [HttpPost("messages")]
    public async Task<ActionResult<ChatResponse>> SendMessage([FromBody] ChatRequest request)
    {
        try
        {
            var thread = await _assistantService.GetOrCreateThread(request.SessionId);
            await _assistantService.AddPrompt(thread, request.Message);

            var (content, commandResults) = await _assistantService.RunThreadOnce(thread, assistantId: null,
                () => _ => { });

            var response = new ChatResponse
            {
                Content = content,
                SessionId = request.SessionId ?? thread.Id,
                CommandResults = commandResults
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat message");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateAssistant()
    {
        var assistantId = await _assistantService.CreateAssistant();
        return Ok(new { assistantId });
    }

    [HttpDelete("{assistantId}")]
    public async Task<IActionResult> DeleteAssistant(string assistantId)
    {
        await _assistantService.DeleteAssistant(assistantId);
        return Ok();
    }

    [HttpGet("token-count")]
    public async Task<IActionResult> GetTokenCount(string sessionId)
    {
        var tokenCount = await _assistantService.GetTokenCount(sessionId);
        return Ok(new { tokenCount });
    }
}