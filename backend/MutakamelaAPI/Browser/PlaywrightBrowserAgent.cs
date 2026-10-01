using MutakamelaAPI.Models;

namespace MutakamelaAPI.Browser;

/// <summary>
/// Placeholder for the real portal driver. It is registered when
/// <c>Agent:Browser = "playwright"</c> but refuses to run until two things exist:
///   1. the Microsoft.Playwright package and browsers are installed, and
///   2. the flow spec has been verified against the live portal (<see cref="FlowSpec.Verified"/>).
/// Until then every call fails safely and the job is marked Failed with a clear reason,
/// so an unverified selector can never be guessed against production.
///
/// Implementation sketch once unblocked (kept here so the contract is visible):
///   - one persistent BrowserContext per job, stored under Agent:ProfileDir/{jobId}
///   - locate fields by trying each FieldSpec.Selectors entry in order (role/label first)
///   - after every screen: read ScreenSpec.ErrorSelector, raise "portal-error" events, screenshot to Agent:EvidenceDir
///   - on OtpScreen: pause with PausedFor="otp"; on PaymentHandoff: capture page.Url and pause with PausedFor="payment"
///   - SubmitAsync: click NextSelector of the IsSubmit screen exactly once, keyed by job.IdempotencyKey
/// </summary>
public class PlaywrightBrowserAgent : IBrowserAgent
{
    private readonly ILogger<PlaywrightBrowserAgent> _logger;

    public PlaywrightBrowserAgent(ILogger<PlaywrightBrowserAgent> logger)
    {
        _logger = logger;
    }

    public string Name => "playwright";

    public Task<FlowRunResult> RunAsync(FlowSpec flow, ApplicationJob job, Func<FlowStepEvent, Task> onEvent, CancellationToken ct) =>
        Refuse(flow, onEvent);

    public Task<FlowRunResult> SubmitAsync(FlowSpec flow, ApplicationJob job, Func<FlowStepEvent, Task> onEvent, CancellationToken ct) =>
        Refuse(flow, onEvent);

    public Task CloseAsync(string jobId) => Task.CompletedTask;

    private async Task<FlowRunResult> Refuse(FlowSpec flow, Func<FlowStepEvent, Task> onEvent)
    {
        var reason = !flow.Verified
            ? $"Flow '{flow.Id}' has not been verified against the live portal. Capture the pages, update the selectors and set \"verified\": true."
            : "The Playwright browser agent is not installed in this build. Add the Microsoft.Playwright package and run `playwright install chromium`.";
        _logger.LogWarning("Browser agent refused to run: {Reason}", reason);
        await onEvent(new FlowStepEvent("portal-error", reason));
        return new FlowRunResult
        {
            Succeeded = false,
            Error = reason,
            ErrorAr = "لا يمكن تشغيل وكيل المتصفح الحقيقي بعد. يرجى التواصل مع الفريق التقني."
        };
    }
}
