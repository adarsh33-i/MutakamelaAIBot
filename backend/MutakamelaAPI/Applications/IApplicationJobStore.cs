using MutakamelaAPI.Models;

namespace MutakamelaAPI.Applications;

/// <summary>
/// Persistence for application jobs. Jobs span minutes to days (login waits,
/// payment, later claims), so they must survive a restart.
/// </summary>
public interface IApplicationJobStore
{
    Task<ApplicationJob?> GetAsync(string jobId);
    Task<ApplicationJob?> GetActiveForSessionAsync(string sessionId);
    Task<IReadOnlyList<ApplicationJob>> ListAsync(string? sessionId = null, int limit = 50);
    Task SaveAsync(ApplicationJob job);
    Task DeleteForSessionAsync(string sessionId);
}
