using System.Collections.Concurrent;
using System.Text;

namespace MutakamelaAPI.Services;

public interface ISessionManager
{
    UserSession GetOrCreateSession(string sessionId);
    void ClearSession(string sessionId);
    int GetActiveSessionCount();
}

public class SessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<string, UserSession> _sessions = new();
    private readonly ILogger<SessionManager> _logger;

    public SessionManager(ILogger<SessionManager> logger)
    {
        _logger = logger;
    }

    public UserSession GetOrCreateSession(string sessionId)
    {
        return _sessions.GetOrAdd(sessionId, id =>
        {
            _logger.LogInformation("📱 New session created: {SessionId}", id);
            return new UserSession(id);
        });
    }

    public void ClearSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out _))
        {
            _logger.LogInformation("🗑️ Session removed: {SessionId}", sessionId);
        }
    }

    public int GetActiveSessionCount() => _sessions.Count;
}

public class UserSession
{
    public string SessionId { get; }
    public DateTime CreatedAt { get; }
    public DateTime LastActivity { get; private set; }
    public List<ConversationMessage> Messages { get; } = new();
    public string? SelectedProductId { get; set; }

    public UserSession(string sessionId)
    {
        SessionId = sessionId;
        CreatedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;
    }

    public void AddMessage(string role, string content)
    {
        Messages.Add(new ConversationMessage
        {
            Role = role,
            Content = content,
            Timestamp = DateTime.UtcNow
        });
        LastActivity = DateTime.UtcNow;

        // Keep only last 10 messages
        if (Messages.Count > 10)
        {
            Messages.RemoveAt(0);
        }
    }

    public string GetConversationText()
    {
        var sb = new StringBuilder();
        foreach (var msg in Messages.TakeLast(6))
        {
            var role = msg.Role == "user" ? "Customer" : "AI";
            sb.AppendLine($"{role}: {msg.Content}");
        }
        return sb.ToString();
    }
}

public class ConversationMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
