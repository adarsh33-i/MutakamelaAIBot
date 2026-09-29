using Microsoft.AspNetCore.Mvc;
using MutakamelaAPI.Models;
using MutakamelaAPI.Services;
using Newtonsoft.Json;

namespace MutakamelaAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WebhookController : ControllerBase
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly IAIPolicyService _aiService;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IWhatsAppService whatsAppService,
        IAIPolicyService aiService,
        ILogger<WebhookController> logger)
    {
        _whatsAppService = whatsAppService;
        _aiService = aiService;
        _logger = logger;
    }

    /// <summary>
    /// Webhook verification endpoint for Meta
    /// </summary>
    [HttpGet]
    public IActionResult VerifyWebhook(
        [FromQuery(Name = "hub.mode")] string mode,
        [FromQuery(Name = "hub.verify_token")] string token,
        [FromQuery(Name = "hub.challenge")] string challenge)
    {
        _logger.LogInformation("🔐 Webhook verification attempt");

        if (_whatsAppService.ValidateWebhook(mode, token, challenge, out var response))
        {
            return Ok(response);
        }

        return Forbid();
    }

    /// <summary>
    /// Receive incoming WhatsApp messages
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ReceiveMessage([FromBody] WhatsAppWebhook webhook)
    {
        try
        {
            _logger.LogInformation("📩 Webhook received: {Type}", webhook.Object);

            // Extract message
            var entry = webhook.Entry.FirstOrDefault();
            var change = entry?.Changes.FirstOrDefault();
            var value = change?.Value;
            var message = value?.Messages.FirstOrDefault();

            if (message == null)
            {
                // Could be a status update, not a message
                _logger.LogInformation("ℹ️ Non-message webhook (status update)");
                return Ok(new { status = "no_message" });
            }

            var phoneNumber = message.From;
            var messageText = ExtractMessageText(message);

            if (string.IsNullOrEmpty(messageText))
            {
                _logger.LogWarning("⚠️ Empty or unsupported message type: {Type}", message.Type);
                return Ok(new { status = "unsupported_type" });
            }

            _logger.LogInformation("💬 Message from {Phone}: {Text}", phoneNumber, messageText);

            // Check for reset command
            if (IsResetCommand(messageText))
            {
                _aiService.ClearSession(phoneNumber);
                await _whatsAppService.SendTextMessageAsync(phoneNumber,
                    "🔄 Session cleared! How can I help you with insurance today?\n\n🇸🇦 تم مسح المحادثة! كيف يمكنني مساعدتك في التأمين اليوم؟");
                return Ok(new { status = "session_cleared" });
            }

            // Process through AI
            var aiResponse = await _aiService.ProcessMessageAsync(phoneNumber, messageText);

            // Build response message
            var responseMessage = BuildResponseMessage(aiResponse);

            // Send response
            await _whatsAppService.SendTextMessageAsync(phoneNumber, responseMessage);

            return Ok(new { status = "success" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Webhook processing error");
            return Ok(new { status = "error", message = ex.Message });
        }
    }

    private string ExtractMessageText(WebhookMessage message)
    {
        return message.Type switch
        {
            "text" => message.Text?.Body ?? "",
            "interactive" => message.Interactive?.ButtonReply?.Title
                          ?? message.Interactive?.ListReply?.Title ?? "",
            _ => ""
        };
    }

    private bool IsResetCommand(string text)
    {
        var commands = new[] { "reset", "start over", "clear", "new", "restart" };
        return commands.Contains(text.ToLower().Trim());
    }

    private string BuildResponseMessage(AIPolicyResponse response)
    {
        var parts = new List<string>();

        // Main response
        if (!string.IsNullOrEmpty(response.Response))
        {
            parts.Add(response.Response);
        }

        // Product details
        if (response.ProductDetails != null)
        {
            if (response.ProductDetails.Coverage.Any())
            {
                parts.Add($"\n📦 Coverage: {string.Join(", ", response.ProductDetails.Coverage.Take(3))}");
            }
            if (response.ProductDetails.KeyBenefits.Any())
            {
                parts.Add($"⭐ Benefits: {string.Join(", ", response.ProductDetails.KeyBenefits.Take(3))}");
            }
        }

        // Arabic response
        if (!string.IsNullOrEmpty(response.ResponseAr))
        {
            parts.Add($"\n🇸🇦 {response.ResponseAr}");
        }

        return string.Join("\n", parts);
    }
}
