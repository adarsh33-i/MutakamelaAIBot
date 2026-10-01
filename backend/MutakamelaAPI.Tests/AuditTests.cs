using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MutakamelaAPI.Applications;
using MutakamelaAPI.Browser;
using MutakamelaAPI.Models;
using MutakamelaAPI.Services;
using Xunit;

namespace MutakamelaAPI.Tests;

/// <summary>
/// One test per finding from the Phase 2 flow audit. Each test encodes the
/// behaviour the system SHOULD have, so a failing test here is a confirmed gap.
/// </summary>
public class AuditTests : IDisposable
{
    private readonly AgentFixture _fixture = new();
    private readonly ServiceProvider _sp;
    private readonly IAIPolicyService _ai;
    private readonly IApplicationOrchestrator _orchestrator;

    public AuditTests()
    {
        _sp = _fixture.BuildProvider();
        _ai = _sp.GetRequiredService<IAIPolicyService>();
        _orchestrator = _sp.GetRequiredService<IApplicationOrchestrator>();
    }

    public void Dispose() { _sp.Dispose(); _fixture.Dispose(); }

    private async Task<AIPolicyResponse> DriveClaimToReview(string s)
    {
        await _ai.Say(s, "I want to file a claim");
        foreach (var a in new[] { "POL-998877", "2098765432", "NJM-556677", "2026-09-20", "Riyadh", "ABC 1234", "yes", "Rear bumper damaged" })
            await _ai.Say(s, a);
        var r = await _ai.Say(s, "logged in");
        Assert.Equal(JobStatus.AwaitingApproval, r.Application!.Status);
        return r;
    }

    private async Task<AIPolicyResponse> DriveMotorToPayment(string s)
    {
        await _ai.Say(s, "I want to buy car insurance");
        foreach (var a in new[] { "1098765432", "1990-01-15", "istimara", "123456789", "2022", "third party", DateTime.UtcNow.AddDays(5).ToString("yyyy-MM-dd"), "no" })
            await _ai.Say(s, a);
        var r = await _ai.Say(s, "logged in");
        Assert.Equal("APP_PAYMENT", r.Stage);
        return r;
    }

    // ---- Finding 1: a bare "yes"/"ok" must not count as submit approval ----

    [Theory]
    [InlineData("yes")]
    [InlineData("ok")]
    [InlineData("نعم")]
    public async Task Audit01_bare_affirmative_does_not_submit(string reply)
    {
        var s = "a1" + reply.Length;
        await DriveClaimToReview(s);
        var r = await _ai.Say(s, reply);
        Assert.Equal(JobStatus.AwaitingApproval, r.Application!.Status);
        Assert.False(r.Application.Events.Any(e => e.Message.Contains("Clicking submit")));
    }

    [Theory]
    [InlineData("confirm")]
    [InlineData("Confirm & submit")]
    [InlineData("تأكيد")]
    public async Task Audit01b_explicit_confirm_still_submits(string reply)
    {
        var s = "a1b" + reply.Length;
        await DriveClaimToReview(s);
        var r = await _ai.Say(s, reply);
        Assert.Equal(JobStatus.Done, r.Application!.Status);
    }

    // ---- Finding 2: "paid"/"yes" must not fabricate a policy number ----

    [Fact]
    public async Task Audit02_customer_saying_paid_does_not_fabricate_a_policy_number()
    {
        var s = "a2";
        await DriveMotorToPayment(s);
        var r = await _ai.Say(s, "paid");
        var policy = r.Application!.Outputs.GetValueOrDefault("policy_number");
        Assert.True(policy == null || !policy.StartsWith("PENDING"), $"fabricated reference: {policy}");
        Assert.True(r.Application.ReferenceNumber == null || !r.Application.ReferenceNumber.StartsWith("PENDING"));
    }

    [Fact]
    public async Task Audit02b_fabricated_reference_must_not_leak_into_a_later_claim()
    {
        var s = "a2b";
        await DriveMotorToPayment(s);
        await _ai.Say(s, "paid");
        var r = await _ai.Say(s, "I want to file a claim");
        var seeded = r.Application!.Data.GetValueOrDefault("policy_number");
        Assert.True(seeded == null || !seeded.StartsWith("PENDING"), $"claim pre-filled with placeholder policy: {seeded}");
    }

    [Fact]
    public async Task Audit02c_bare_yes_while_awaiting_payment_does_not_complete_the_job()
    {
        var s = "a2c";
        await DriveMotorToPayment(s);
        var r = await _ai.Say(s, "yes");
        Assert.NotEqual(JobStatus.Done, r.Application!.Status);
        Assert.Equal("APP_PAYMENT", r.Stage);
    }

    [Fact]
    public async Task Audit02d_paid_completes_only_once_the_portal_shows_a_policy_number()
    {
        var s = "a2d";
        await DriveMotorToPayment(s);
        var r = await _ai.Say(s, "paid");
        Assert.Equal(JobStatus.Done, r.Application!.Status);
        Assert.StartsWith("SIM-POL-", r.Application.ReferenceNumber);
        Assert.Contains(r.Application.Events, e => e.Message.Contains("Read policy number from confirmation page"));
    }

    // ---- Finding 6: switching journeys mid-intake must not silently discard data ----

    [Fact]
    public async Task Audit06_switching_journey_mid_intake_warns_instead_of_silently_cancelling()
    {
        var s = "a6";
        await _ai.Say(s, "I want to file a claim");
        var before = await _ai.Say(s, "POL-998877");
        var r = await _ai.Say(s, "track my claim");
        var oldJob = await _orchestrator.GetAsync(before.Application!.Id);
        Assert.NotEqual(JobStatus.Cancelled, oldJob!.Status);
        Assert.Contains("discard", r.Response);

        // Continuing to answer keeps the original job.
        r = await _ai.Say(s, "2098765432");
        Assert.Equal(before.Application.Id, r.Application!.Id);
        Assert.Equal("2098765432", r.Application.Data["national_id"]);

        // Asking again, then confirming, performs the switch.
        await _ai.Say(s, "track my claim");
        r = await _ai.Say(s, "switch");
        Assert.Equal(FlowIds.TrackAClaim, r.Application!.FlowId);
        Assert.Equal(JobStatus.Cancelled, (await _orchestrator.GetAsync(before.Application.Id))!.Status);
    }

    // ---- Finding 13: a failed browser run must leave the job Failed with nothing submitted ----

    [Fact]
    public async Task Audit13_failed_browser_run_leaves_job_failed_and_unsubmitted()
    {
        var fixture = new AgentFixture();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(fixture.Config);
        services.AddSingleton<MutakamelaAPI.Rules.IRulesEngine, MutakamelaAPI.Rules.RulesEngine>();
        services.AddSingleton<IFlowRegistry, FlowRegistry>();
        services.AddSingleton<IApplicationJobStore, FileApplicationJobStore>();
        services.AddSingleton<IBrowserAgent>(new PlaywrightBrowserAgent(NullLogger<PlaywrightBrowserAgent>.Instance)); // refuses unverified flows
        services.AddSingleton<IApplicationOrchestrator, ApplicationOrchestrator>();
        using var sp = services.BuildServiceProvider();
        var orch = sp.GetRequiredService<IApplicationOrchestrator>();

        var start = await orch.StartAsync("a13", FlowIds.TrackAClaim, "en", new Dictionary<string, string> { ["claim_number"] = "CLM-1", ["national_id"] = "1098765432" });
        Assert.Equal("APP_LOGIN", start.Stage);
        var r = await orch.MarkLoginCompleteAsync(start.Application!.Id, "en");
        Assert.Equal(JobStatus.Failed, r!.Status);
        Assert.NotNull(r.FailureReason);
        Assert.Null(r.ReferenceNumber);
        Assert.False(r.Events.Any(e => e.Message.Contains("Clicking submit")));
        fixture.Dispose();
    }

    // ---- Finding 12: API surface + buy-insurance landing + personal-info happy path ----

    [Fact]
    public async Task Audit12a_api_patch_data_then_cancel()
    {
        var start = await _orchestrator.StartAsync("a12a", FlowIds.TrackAClaim, "en");
        var id = start.Application!.Id;
        var patched = await _orchestrator.ApplyDataAsync(id, new Dictionary<string, string> { ["claim_number"] = "CLM-5", ["national_id"] = "bad" }, "en");
        Assert.Equal("CLM-5", patched!.Data["claim_number"]);
        Assert.DoesNotContain("national_id", patched.Data.Keys);
        Assert.Contains("national_id", patched.MissingFields);
        var cancelled = await _orchestrator.CancelAsync(id, "en");
        Assert.Equal(JobStatus.Cancelled, cancelled!.Status);
        Assert.Null(await _sp.GetRequiredService<IApplicationJobStore>().GetActiveForSessionAsync("a12a"));
    }

    [Fact]
    public async Task Audit12b_buy_insurance_landing_routes_motor()
    {
        var r = await _ai.Say("a12b", "I want to buy insurance");
        Assert.Equal(FlowIds.BuyInsurance, r.Application!.FlowId);
        r = await _ai.Say("a12b", "car");
        Assert.Equal("motor", r.Application!.Data["product"]);
    }

    [Fact]
    public async Task Audit12c_approve_endpoint_is_ignored_unless_awaiting_approval()
    {
        var start = await _orchestrator.StartAsync("a12c", FlowIds.MakeAClaim, "en");
        var r = await _orchestrator.ApproveAsync(start.Application!.Id, "en");
        Assert.Equal(JobStatus.Collecting, r!.Status);
        Assert.False(r.Events.Any(e => e.Message.Contains("Clicking submit")));
    }

    // ---- Finding 11: value extraction must not misfire on ordinary words ----

    [Fact]
    public async Task Audit11_extraction_does_not_grab_ordinary_uppercase_words_as_policy_numbers()
    {
        var s = "a11";
        await _ai.Say(s, "I want to file a claim");
        var r = await _ai.Say(s, "PLEASE HELP ME");
        var stored = r.Application!.Data.GetValueOrDefault("policy_number");
        Assert.True(stored == null || stored == "PLEASE HELP ME" || !stored.StartsWith("PLEASE"), $"extracted '{stored}' as policy number");
        Assert.NotEqual("PLEASE", stored);
    }

    // ---- Finding 3: self-reported login is only accepted in AwaitingLogin ----

    [Fact]
    public async Task Audit03_logged_in_while_collecting_is_treated_as_data_not_login()
    {
        var s = "a3";
        await _ai.Say(s, "I want to track my claim");
        var r = await _ai.Say(s, "logged in");
        Assert.Equal(JobStatus.Collecting, r.Application!.Status);
        Assert.Null(r.Application.Events.FirstOrDefault(e => e.Message.Contains("login complete")));
    }

    // ---- Finding 7: LLM session log should carry a useful outcome after a job ----

    [Fact]
    public async Task Audit07_session_history_records_job_outcome()
    {
        var s = "a7";
        await DriveClaimToReview(s);
        await _ai.Say(s, "confirm");
        var session = _sp.GetRequiredService<ISessionManager>().GetOrCreateSession(s);
        var text = session.GetConversationText();
        Assert.True(text.Contains("Done:") || text.Contains("claim number", StringComparison.OrdinalIgnoreCase),
            "session history has no record of the job outcome:\n" + text);
    }
}
