using Microsoft.AspNetCore.Mvc;
using MutakamelaAPI.Models;
using MutakamelaAPI.Services;

namespace MutakamelaAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IAIPolicyService _aiService;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IAIPolicyService aiService,
        ISessionManager sessionManager,
        ILogger<ChatController> logger)
    {
        _aiService = aiService;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Direct chat endpoint (for testing without WhatsApp)
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ChatResponse>> Chat([FromBody] ChatRequest request)
    {
        if (string.IsNullOrEmpty(request.Message))
        {
            return BadRequest(new { error = "Message is required" });
        }

        var language = request.Language.Trim().ToLowerInvariant();
        if (language is not ("en" or "ar"))
        {
            return BadRequest(new { error = "Language must be 'en' or 'ar'" });
        }

        // Generate session ID if not provided
        var sessionId = string.IsNullOrEmpty(request.SessionId)
            ? Guid.NewGuid().ToString("N")[..8]
            : request.SessionId;

        _logger.LogInformation("Chat request received with {MessageLength} characters.",
            request.Message.Length);

        try
        {
            var aiResponse = await _aiService.ProcessMessageAsync(sessionId, request.Message, language);
            var isArabic = language == "ar";
            var primaryResponse = isArabic && !string.IsNullOrWhiteSpace(aiResponse.ResponseAr)
                ? aiResponse.ResponseAr
                : aiResponse.Response;
            var secondaryResponse = isArabic ? aiResponse.Response : aiResponse.ResponseAr;

            return Ok(new ChatResponse
            {
                Success = true,
                SessionId = sessionId,
                Response = primaryResponse,
                ResponseAr = secondaryResponse,
                Stage = aiResponse.Stage,
                DetectedLob = aiResponse.DetectedLob,
                SelectedProduct = aiResponse.SelectedProduct,
                ProductOptions = aiResponse.ProductOptions,
                ProductDetails = aiResponse.ProductDetails
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Chat processing error");
            return StatusCode(500, new ChatResponse
            {
                Success = false,
                SessionId = sessionId,
                Response = language == "ar" ? "حدث خطأ. يرجى المحاولة مرة أخرى." : "An error occurred. Please try again.",
                ResponseAr = language == "ar" ? "An error occurred. Please try again." : "حدث خطأ. يرجى المحاولة مرة أخرى."
            });
        }
    }

    /// <summary>
    /// Clear a chat session
    /// </summary>
    [HttpDelete("{sessionId}")]
    public IActionResult ClearSession(string sessionId)
    {
        _aiService.ClearSession(sessionId);
        return Ok(new { message = "Session cleared", sessionId });
    }
}
