using AssistantPoc.Core.Interfaces;
using AssistantPoc.WebApi.Models;
using Microsoft.AspNetCore.Mvc;
using OpenAI.Assistants;
using System.Text;

namespace AssistantPoc.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IAssistantService _assistantService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IAssistantService assistantService,
        ILogger<ChatController> logger)
    {
        _assistantService = assistantService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<ChatResponse>> SendMessage([FromBody] ChatRequest request)
    {
        try
        {
            var thread = await _assistantService.GetOrCreateThread(request.SessionId);
            await _assistantService.AddPrompt(thread, request.Message);

            var (content, navigateCommand) = await _assistantService.RunThreadOnce(thread, null, 
                () => _ => {});

            return Ok(new ChatResponse 
            { 
                Content = content,
                SessionId = request.SessionId ?? thread.Id,
                NavigateToAsset = navigateCommand
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat message");
            return StatusCode(500, new { error = ex.Message });
        }
    }
} 