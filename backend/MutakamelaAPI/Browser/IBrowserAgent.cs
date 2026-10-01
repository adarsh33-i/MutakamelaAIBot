using MutakamelaAPI.Models;

namespace MutakamelaAPI.Browser;

/// <summary>Progress event raised by the browser agent while a flow runs.</summary>
public record FlowStepEvent(
    string Type,            // screen | field | validation | portal-error | screenshot | login-required | otp-required | payment-handoff | output | done
    string Message,
    string? Screen = null,
    string? Field = null,
    string? ScreenshotPath = null,
    Dictionary<string, string>? Outputs = null);

/// <summary>Outcome of a run segment (the agent may pause for login, OTP or approval).</summary>
public class FlowRunResult
{
    public bool Succeeded { get; set; }
    /// <summary>login | otp | approval | payment | null when finished or failed outright.</summary>
    public string? PausedFor { get; set; }
    public string? Error { get; set; }
    public string? ErrorAr { get; set; }
    public Dictionary<string, string> Outputs { get; set; } = new();
    public string? FinalScreenshotPath { get; set; }
}

/// <summary>
/// Drives the portal for one job. Implementations: <see cref="SimulatedBrowserAgent"/>
/// (default, no network) and a Playwright-backed agent once the dependency and
/// verified flow specs are available. The orchestrator never submits anything
/// through this interface unless the job is already in Submitting.
/// </summary>
public interface IBrowserAgent
{
    string Name { get; }

    /// <summary>Walk the flow's screens up to the first pause point (login, OTP, approval, payment) or completion.</summary>
    Task<FlowRunResult> RunAsync(FlowSpec flow, ApplicationJob job, Func<FlowStepEvent, Task> onEvent, CancellationToken ct);

    /// <summary>Perform the final submit for a job already approved by the customer.</summary>
    Task<FlowRunResult> SubmitAsync(FlowSpec flow, ApplicationJob job, Func<FlowStepEvent, Task> onEvent, CancellationToken ct);

    /// <summary>Release any browser context held for the job.</summary>
    Task CloseAsync(string jobId);
}
