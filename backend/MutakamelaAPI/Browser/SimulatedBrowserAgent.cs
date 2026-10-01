using MutakamelaAPI.Models;

namespace MutakamelaAPI.Browser;

/// <summary>
/// Walks a flow spec screen by screen without touching the network. It exercises
/// the full job lifecycle (login pause, screen-by-screen filling, OTP, approval,
/// payment handoff, submit) so the chat, API and tests work end to end before
/// a Playwright agent and verified selectors exist. Outputs are clearly marked
/// as simulated so nothing can be mistaken for a real portal response.
/// </summary>
public class SimulatedBrowserAgent : IBrowserAgent
{
    private readonly ILogger<SimulatedBrowserAgent> _logger;
    private readonly string _evidenceRoot;

    public SimulatedBrowserAgent(ILogger<SimulatedBrowserAgent> logger, IConfiguration config)
    {
        _logger = logger;
        _evidenceRoot = config["Agent:EvidenceDir"] ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "evidence");
    }

    public string Name => "simulated";

    public async Task<FlowRunResult> RunAsync(FlowSpec flow, ApplicationJob job, Func<FlowStepEvent, Task> onEvent, CancellationToken ct)
    {
        if (flow.RequiresLogin && job.LoginCompletedAt == null)
        {
            await onEvent(new FlowStepEvent("login-required", $"Portal login required before {flow.Id}. Waiting for customer to approve login.", flow.Screens.FirstOrDefault()?.Id));
            return new FlowRunResult { Succeeded = true, PausedFor = "login" };
        }

        var result = new FlowRunResult { Succeeded = true };
        foreach (var screen in flow.Screens)
        {
            ct.ThrowIfCancellationRequested();

            if (screen.PaymentHandoff)
            {
                if (job.Events.Any(e => e.Message.Contains("reported payment complete")))
                {
                    // Second visit after the customer paid: a real agent would read the
                    // confirmation page; the simulator surfaces a clearly-marked value.
                    result.Outputs["policy_number"] = SimulatedOutput(flow.Id, "policy_number", job);
                    await onEvent(new FlowStepEvent("output", "[simulated] Read policy number from confirmation page.", screen.Id, Outputs: new() { ["policy_number"] = result.Outputs["policy_number"] }));
                    return result;
                }
                var url = flow.Url + "#payment";
                result.Outputs["payment_url"] = url;
                await onEvent(new FlowStepEvent("payment-handoff", "Reached the payment screen. Handing the payment link to the customer; card fields are never touched.", screen.Id, Outputs: new() { ["payment_url"] = url }));
                result.PausedFor = "payment";
                result.FinalScreenshotPath = await Snapshot(job, screen.Id, onEvent);
                return result;
            }

            await onEvent(new FlowStepEvent("screen", $"[simulated] Opened screen '{screen.Title}'", screen.Id));

            if (screen.OtpScreen && !job.Data.ContainsKey("otp_code"))
            {
                await onEvent(new FlowStepEvent("otp-required", "The portal sent a one-time code. Waiting for the customer to provide it.", screen.Id, "otp_code"));
                result.PausedFor = "otp";
                return result;
            }

            foreach (var field in screen.Fields)
            {
                if (field.Type == "file" || field.Source == "portal") continue;
                if (!job.Data.TryGetValue(field.Id, out var value) || string.IsNullOrWhiteSpace(value)) continue;
                var shown = field.Sensitive ? "••••" : value;
                await onEvent(new FlowStepEvent("field", $"[simulated] Filled '{field.Label}' = {shown}", screen.Id, field.Id));
            }

            if (screen.OtpScreen)
            {
                job.Data.Remove("otp_code"); // never persisted after use
                await onEvent(new FlowStepEvent("info", "[simulated] One-time code accepted and discarded.", screen.Id));
            }

            var shot = await Snapshot(job, screen.Id, onEvent);
            result.FinalScreenshotPath = shot;

            if (screen.Id == "quotes")
            {
                var quotes = "Third Party — SAR 650 (simulated); Comprehensive — SAR 2,400 (simulated)";
                result.Outputs["quote_options"] = quotes;
                await onEvent(new FlowStepEvent("output", "[simulated] Read quote options from the portal.", screen.Id, Outputs: new() { ["quote_options"] = quotes }));
            }

            if (screen.IsSubmit)
            {
                // Stop before the final action; the orchestrator must obtain approval first.
                await onEvent(new FlowStepEvent("info", $"[simulated] Reached final screen '{screen.Title}'. Awaiting customer approval before submit.", screen.Id));
                result.PausedFor = flow.RequiresApproval ? "approval" : null;
                if (result.PausedFor != null) return result;
            }
        }

        // Read-only flows finish here.
        foreach (var output in flow.Outputs)
            result.Outputs[output.Id] = SimulatedOutput(flow.Id, output.Id, job);
        await onEvent(new FlowStepEvent("done", "[simulated] Flow completed.", Outputs: result.Outputs));
        return result;
    }

    public async Task<FlowRunResult> SubmitAsync(FlowSpec flow, ApplicationJob job, Func<FlowStepEvent, Task> onEvent, CancellationToken ct)
    {
        var submitScreen = flow.Screens.LastOrDefault(s => s.IsSubmit);
        await onEvent(new FlowStepEvent("info", $"[simulated] Clicking submit on '{submitScreen?.Title ?? "final"}' with idempotency key {job.IdempotencyKey}", submitScreen?.Id));
        var result = new FlowRunResult { Succeeded = true };
        foreach (var output in flow.Outputs)
            result.Outputs[output.Id] = SimulatedOutput(flow.Id, output.Id, job);
        result.FinalScreenshotPath = await Snapshot(job, "confirmation", onEvent);
        await onEvent(new FlowStepEvent("done", "[simulated] Submitted. Confirmation captured.", "confirmation", Outputs: result.Outputs));
        return result;
    }

    public Task CloseAsync(string jobId) => Task.CompletedTask;

    private static string SimulatedOutput(string flowId, string outputId, ApplicationJob job) => outputId switch
    {
        "claim_status" => "Under review (simulated)",
        "last_updated" => DateTime.UtcNow.ToString("yyyy-MM-dd") + " (simulated)",
        "next_step" => "Surveyor assessment (simulated)",
        "claim_number" => $"SIM-CLM-{job.Id[..6].ToUpperInvariant()}",
        "policy_number" => $"SIM-POL-{job.Id[..6].ToUpperInvariant()}",
        _ => $"(simulated {flowId}/{outputId})"
    };

    /// <summary>Writes a text placeholder where a real agent would save a PNG, so evidence paths are exercised.</summary>
    private async Task<string?> Snapshot(ApplicationJob job, string screenId, Func<FlowStepEvent, Task> onEvent)
    {
        try
        {
            var dir = Path.Combine(_evidenceRoot, job.Id);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{screenId}.txt");
            await File.WriteAllTextAsync(path, $"simulated screenshot of {screenId} for job {job.Id} at {DateTime.UtcNow:O}");
            await onEvent(new FlowStepEvent("screenshot", $"Captured evidence for '{screenId}'", screenId, ScreenshotPath: path));
            return path;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write evidence for job {Job}", job.Id);
            return null;
        }
    }
}
