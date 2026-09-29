namespace MutakamelaAPI.Services;

public interface IWhatsAppService
{
    Task<bool> SendTextMessageAsync(string to, string message);
    Task<bool> SendInteractiveButtonsAsync(string to, string body, List<string> buttons);
    bool ValidateWebhook(string mode, string token, string challenge, out string response);
}
