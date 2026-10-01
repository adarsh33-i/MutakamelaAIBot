using System.Collections.Concurrent;
using System.Text.Json;
using MutakamelaAPI.Models;

namespace MutakamelaAPI.Applications;

/// <summary>
/// Durable job store that writes one JSON document per job under
/// <c>Agent:DataDir</c> (default App_Data/jobs). Chosen over SQLite so the
/// build has no new package dependency; the interface is the seam for swapping
/// in SQLite/Postgres later. Sensitive fields (otp codes) are stripped before
/// writing. Writes are serialised per job and the whole store is loaded into
/// memory at startup.
/// </summary>
public class FileApplicationJobStore : IApplicationJobStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly ConcurrentDictionary<string, ApplicationJob> _jobs = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly string _dir;
    private readonly ILogger<FileApplicationJobStore> _logger;

    public FileApplicationJobStore(IConfiguration config, ILogger<FileApplicationJobStore> logger)
    {
        _logger = logger;
        _dir = config["Agent:DataDir"] ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "jobs");
        Directory.CreateDirectory(_dir);
        foreach (var file in Directory.GetFiles(_dir, "*.json"))
        {
            try
            {
                var job = JsonSerializer.Deserialize<ApplicationJob>(File.ReadAllText(file));
                if (job != null && !string.IsNullOrEmpty(job.Id)) _jobs[job.Id] = job;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping unreadable job file {File}", file);
            }
        }
        _logger.LogInformation("Loaded {Count} application jobs from {Dir}", _jobs.Count, _dir);
    }

    public Task<ApplicationJob?> GetAsync(string jobId) =>
        Task.FromResult(_jobs.TryGetValue(jobId, out var job) ? job : null);

    public Task<ApplicationJob?> GetActiveForSessionAsync(string sessionId) =>
        Task.FromResult(_jobs.Values
            .Where(j => j.SessionId == sessionId && !j.IsTerminal)
            .OrderByDescending(j => j.UpdatedAt)
            .FirstOrDefault());

    public Task<IReadOnlyList<ApplicationJob>> ListAsync(string? sessionId = null, int limit = 50)
    {
        IEnumerable<ApplicationJob> query = _jobs.Values;
        if (!string.IsNullOrEmpty(sessionId)) query = query.Where(j => j.SessionId == sessionId);
        return Task.FromResult<IReadOnlyList<ApplicationJob>>(query.OrderByDescending(j => j.UpdatedAt).Take(limit).ToList());
    }

    public async Task SaveAsync(ApplicationJob job)
    {
        job.UpdatedAt = DateTime.UtcNow;
        _jobs[job.Id] = job;
        await _writeLock.WaitAsync();
        try
        {
            var snapshot = Sanitize(job);
            var path = Path.Combine(_dir, job.Id + ".json");
            var tmp = path + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(snapshot, JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DeleteForSessionAsync(string sessionId)
    {
        foreach (var job in _jobs.Values.Where(j => j.SessionId == sessionId).ToList())
        {
            _jobs.TryRemove(job.Id, out _);
            await _writeLock.WaitAsync();
            try
            {
                var path = Path.Combine(_dir, job.Id + ".json");
                if (File.Exists(path)) File.Delete(path);
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }

    private static ApplicationJob Sanitize(ApplicationJob job)
    {
        var copy = JsonSerializer.Deserialize<ApplicationJob>(JsonSerializer.Serialize(job))!;
        copy.Data.Remove("otp_code");
        return copy;
    }
}
