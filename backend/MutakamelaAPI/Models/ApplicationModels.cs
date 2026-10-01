using System.Text.Json.Serialization;

namespace MutakamelaAPI.Models;

// ============ Agentic application job models ============

/// <summary>
/// Lifecycle of a portal automation job. Transitions are enforced by
/// <see cref="ApplicationJob.TryTransition"/>; the LLM never moves a job
/// forward on its own and only an explicit customer approval reaches Submitting.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JobStatus
{
    Collecting,
    Validating,
    AwaitingLogin,
    Filling,
    AwaitingApproval,
    Submitting,
    Done,
    Failed,
    Cancelled
}

/// <summary>Portal journeys, one per flow spec under Browser/Flows.</summary>
public static class FlowIds
{
    public const string BuyInsurance = "buy-insurance";
    public const string BuyMotorInsurance = "buy-motor-insurance";
    public const string PersonalInfo = "personal-info";
    public const string MakeAClaim = "make-a-claim";
    public const string TrackAClaim = "track-a-claim";

    public static readonly string[] All =
    {
        BuyInsurance, BuyMotorInsurance, PersonalInfo, MakeAClaim, TrackAClaim
    };
}

public class ApplicationJob
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string FlowId { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public JobStatus Status { get; set; } = JobStatus.Collecting;

    /// <summary>Field values collected in chat, keyed by flow field id.</summary>
    public Dictionary<string, string> Data { get; set; } = new();

    /// <summary>Values the browser agent read back from the portal (quotes, status, policy number...).</summary>
    public Dictionary<string, string> Outputs { get; set; } = new();

    /// <summary>Set once a submit has been attempted so a retry can never submit twice.</summary>
    public string? IdempotencyKey { get; set; }
    public bool SubmitAttempted { get; set; }

    /// <summary>Set when the customer asked for another journey mid-job; cleared on any other reply.</summary>
    public string? PendingSwitchTo { get; set; }

    public string? FailureReason { get; set; }
    public string? FailureReasonAr { get; set; }
    public string? ReferenceNumber { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public DateTime? LoginCompletedAt { get; set; }

    public List<JobEvent> Events { get; set; } = new();

    private static readonly Dictionary<JobStatus, JobStatus[]> Allowed = new()
    {
        [JobStatus.Collecting] = new[] { JobStatus.Validating, JobStatus.Cancelled, JobStatus.Failed },
        [JobStatus.Validating] = new[] { JobStatus.AwaitingLogin, JobStatus.Filling, JobStatus.Collecting, JobStatus.Cancelled, JobStatus.Failed },
        [JobStatus.AwaitingLogin] = new[] { JobStatus.Filling, JobStatus.Cancelled, JobStatus.Failed },
        [JobStatus.Filling] = new[] { JobStatus.AwaitingApproval, JobStatus.Done, JobStatus.Collecting, JobStatus.Cancelled, JobStatus.Failed },
        [JobStatus.AwaitingApproval] = new[] { JobStatus.Submitting, JobStatus.Collecting, JobStatus.Cancelled, JobStatus.Failed },
        [JobStatus.Submitting] = new[] { JobStatus.Done, JobStatus.Failed },
        [JobStatus.Done] = Array.Empty<JobStatus>(),
        [JobStatus.Failed] = Array.Empty<JobStatus>(),
        [JobStatus.Cancelled] = Array.Empty<JobStatus>()
    };

    public bool CanTransition(JobStatus next) =>
        Allowed.TryGetValue(Status, out var targets) && targets.Contains(next);

    public bool TryTransition(JobStatus next, string? note = null)
    {
        if (!CanTransition(next)) return false;
        var previous = Status;
        Status = next;
        UpdatedAt = DateTime.UtcNow;
        AddEvent("status", $"{previous} → {next}" + (note is null ? string.Empty : $": {note}"));
        return true;
    }

    public void AddEvent(string type, string message, string? screen = null, string? screenshotPath = null)
    {
        Events.Add(new JobEvent
        {
            Type = type,
            Message = message,
            Screen = screen,
            ScreenshotPath = screenshotPath,
            Timestamp = DateTime.UtcNow
        });
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsTerminal => Status is JobStatus.Done or JobStatus.Failed or JobStatus.Cancelled;
}

public class JobEvent
{
    public DateTime Timestamp { get; set; }
    /// <summary>status | screen | field | validation | portal-error | screenshot | info</summary>
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Screen { get; set; }
    public string? ScreenshotPath { get; set; }
}

// ============ API contracts ============

public class CreateApplicationRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string FlowId { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public Dictionary<string, string> Data { get; set; } = new();
}

public class UpdateApplicationDataRequest
{
    public Dictionary<string, string> Data { get; set; } = new();
}

public class ApplicationJobResponse
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string FlowId { get; set; } = string.Empty;
    public JobStatus Status { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public string StatusMessageAr { get; set; } = string.Empty;
    public List<string> MissingFields { get; set; } = new();
    public List<FieldProblem> ValidationErrors { get; set; } = new();
    public Dictionary<string, string> Data { get; set; } = new();
    public Dictionary<string, string> Outputs { get; set; } = new();
    public string? ReferenceNumber { get; set; }
    public string? FailureReason { get; set; }
    public string? FailureReasonAr { get; set; }
    public ReviewSummary? Review { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<JobEvent> Events { get; set; } = new();
}

public class FieldProblem
{
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string MessageAr { get; set; } = string.Empty;
}

/// <summary>What the customer sees before approving a submission.</summary>
public class ReviewSummary
{
    public List<ReviewLine> Lines { get; set; } = new();
    public string? FinalScreenshotPath { get; set; }
    public string Disclaimer { get; set; } = string.Empty;
    public string DisclaimerAr { get; set; } = string.Empty;
}

public class ReviewLine
{
    public string Field { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string LabelAr { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    /// <summary>customer | document | profile | portal</summary>
    public string Source { get; set; } = "customer";
}
