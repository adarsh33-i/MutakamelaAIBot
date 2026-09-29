using System.Text;
using MutakamelaAPI.Models;
using Newtonsoft.Json;

namespace MutakamelaAPI.Services;

public class WhatsAppService : IWhatsAppService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<WhatsAppService> _logger;
    private readonly string _apiUrl;

    public WhatsAppService(HttpClient httpClient, IConfiguration config, ILogger<WhatsAppService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;

        var phoneId = _config["WhatsApp:PhoneNumberId"];
        var apiVersion = _config["WhatsApp:ApiVersion"] ?? "v18.0";
        _apiUrl = $"https://graph.facebook.com/{apiVersion}/{phoneId}/messages";

        // Set authorization header
        var token = _config["WhatsApp:AccessToken"];
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    public bool ValidateWebhook(string mode, string token, string challenge, out string response)
    {
        var verifyToken = _config["WhatsApp:VerifyToken"];

        if (mode == "subscribe" && token == verifyToken)
        {
            _logger.LogInformation("✅ Webhook verified successfully");
            response = challenge;
            return true;
        }

        _logger.LogWarning("❌ Webhook verification failed");
        response = "Forbidden";
        return false;
    }

    public async Task<bool> SendTextMessageAsync(string to, string message)
    {
        try
        {
            var request = new SendMessageRequest
            {
                To = to,
                Type = "text",
                Text = new SendMessageText { Body = message }
            };

            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_apiUrl, content);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("✅ Message sent to {Phone}", to);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to send message to {Phone}", to);
            return false;
        }
    }

    public async Task<bool> SendInteractiveButtonsAsync(string to, string body, List<string> buttons)
    {
        try
        {
            var buttonList = buttons.Take(3).Select((btn, i) => new InteractiveButton
            {
                Type = "reply",
                Reply = new ButtonReply
                {
                    Id = $"btn_{i}",
                    Title = btn.Length > 20 ? btn.Substring(0, 20) : btn
                }
            }).ToList();

            var request = new SendMessageRequest
            {
                To = to,
                Type = "interactive",
                Interactive = new SendMessageInteractive
                {
                    Type = "button",
                    Body = new InteractiveBody { Text = body },
                    Action = new InteractiveAction { Buttons = buttonList }
                }
            };

            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_apiUrl, content);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("✅ Interactive message sent to {Phone}", to);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to send interactive message, falling back to text");
            return await SendTextMessageAsync(to, body);
        }
    }
}
