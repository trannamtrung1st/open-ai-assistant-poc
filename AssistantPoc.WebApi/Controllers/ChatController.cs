using AssistantPoc.Core.Interfaces;
using AssistantPoc.WebApi.Models;
using Microsoft.AspNetCore.Mvc;

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
            request.SessionId ??= Guid.NewGuid().ToString();
            var thread = await _assistantService.GetOrCreateThread(request.SessionId);
            await _assistantService.AddPrompt(thread, request.Message);

            var (content, navigateCommand) = await _assistantService.RunThreadOnce(thread, assistantId: null,
                () => _ => { });

            var response = new ChatResponse
            {
                Content = content,
                SessionId = request.SessionId ?? thread.Id
            };

            if (navigateCommand != null)
            {
                response.CommandResult = new CommandResult
                {
                    Command = navigateCommand.Command,
                    Data = navigateCommand
                };
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat message");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}