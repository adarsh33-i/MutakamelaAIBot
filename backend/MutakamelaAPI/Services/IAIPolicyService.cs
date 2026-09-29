using MutakamelaAPI.Models;

namespace MutakamelaAPI.Services;

public interface IAIPolicyService
{
    Task<AIPolicyResponse> ProcessMessageAsync(string sessionId, string message, string lang = "en");
    void ClearSession(string sessionId);
}
