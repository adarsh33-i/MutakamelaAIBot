using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MutakamelaAPI.Applications;
using MutakamelaAPI.Browser;
using MutakamelaAPI.Models;
using MutakamelaAPI.Services;
using Xunit;

namespace MutakamelaAPI.Tests;

/// <summary>End-to-end journeys through the chat entry point with the simulated browser agent.</summary>
public class OrchestratorJourneyTests : IDisposable
{
    private readonly AgentFixture _fixture = new();
    private readonly ServiceProvider _sp;
    private readonly IAIPolicyService _ai;

    public OrchestratorJourneyTests()
    {
        _sp = _fixture.BuildProvider();
        _ai = _sp.GetRequiredService<IAIPolicyService>();
    }

    public void Dispose()
    {
        _sp.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public async Task Track_a_claim_collects_validates_waits_for_login_then_reads_status()
    {
        var s = "track";
        var r = await _ai.Say(s, "I want to track my claim");
        Assert.Equal("APP_COLLECT", r.Stage);
        Assert.Equal(FlowIds.TrackAClaim, r.Application!.FlowId);

        r = await _ai.Say(s, "my card number is 4111 1111 1111 1111");
        Assert.Contains("card", r.Response);
        Assert.Empty(r.Application!.Data);

        r = await _ai.Say(s, "CL");
        Assert.DoesNotContain("claim_number", r.Application!.Data.Keys);

        r = await _ai.Say(s, "CLM-2024-00123");
        Assert.Equal("CLM-2024-00123", r.Application!.Data["claim_number"]);

        r = await _ai.Say(s, "3098765432");
        Assert.Equal("APP_COLLECT", r.Stage);

        r = await _ai.Say(s, "1098765432");
        Assert.Equal("APP_LOGIN", r.Stage);
        Assert.Equal(JobStatus.AwaitingLogin, r.Application!.Status);

        r = await _ai.Say(s, "what's the weather");
        Assert.Equal("APP_LOGIN", r.Stage);

        r = await _ai.Say(s, "logged in");
        Assert.Equal("APP_DONE", r.Stage);
        Assert.Equal(JobStatus.Done, r.Application!.Status);
        Assert.True(r.Application.Outputs.ContainsKey("claim_status"));
        Assert.Contains(r.Application.Events, e => e.Type == "screenshot");
    }

    [Fact]
    public async Task Make_a_claim_requires_explicit_approval_supports_edit_and_never_submits_twice()
    {
        var s = "claim";
        var r = await _ai.Say(s, "I want to file a claim");
        Assert.Equal(FlowIds.MakeAClaim, r.Application!.FlowId);

        foreach (var answer in new[] { "POL-998877", "2098765432", "NJM-556677" })
            await _ai.Say(s, answer);

        r = await _ai.Say(s, "2099-01-01");
        Assert.DoesNotContain("accident_date", r.Application!.Data.Keys);

        foreach (var answer in new[] { "2026-09-20", "Riyadh, King Fahd Road", "ABC 1234", "yes" })
            await _ai.Say(s, answer);
        r = await _ai.Say(s, "Rear bumper damaged in a low-speed collision");
        Assert.Equal("APP_LOGIN", r.Stage);

        r = await _ai.Say(s, "logged in");
        Assert.Equal("APP_REVIEW", r.Stage);
        Assert.Equal(JobStatus.AwaitingApproval, r.Application!.Status);
        Assert.Contains("NOT submitted", r.Response);

        r = await _ai.Say(s, "random chatter");
        Assert.Equal(JobStatus.AwaitingApproval, r.Application!.Status);

        r = await _ai.Say(s, "edit location");
        Assert.Equal(JobStatus.Collecting, r.Application!.Status);
        r = await _ai.Say(s, "Jeddah, Corniche");
        Assert.Equal("Jeddah, Corniche", r.Application!.Data["accident_location"]);
        Assert.Equal("APP_REVIEW", r.Stage);

        r = await _ai.Say(s, "confirm");
        Assert.Equal(JobStatus.Done, r.Application!.Status);
        Assert.False(string.IsNullOrEmpty(r.Application.ReferenceNumber));

        var orchestrator = _sp.GetRequiredService<IApplicationOrchestrator>();
        var again = await orchestrator.ApproveAsync(r.Application.Id, "en");
        Assert.Equal(1, again!.Events.Count(e => e.Message.Contains("Clicking submit")));
    }

    [Fact]
    public async Task Motor_purchase_handles_conditional_fields_and_stops_at_payment()
    {
        var s = "motor";
        var r = await _ai.Say(s, "I want to buy car insurance");
        Assert.Equal(FlowIds.BuyMotorInsurance, r.Application!.FlowId);

        r = await _ai.Say(s, "1098765432");
        r = await _ai.Say(s, "1410-05-20");
        Assert.Equal("hijri", r.Application!.Data["date_of_birth_calendar"]);
        Assert.Equal("1989-12-18", r.Application.Data["date_of_birth"]);

        r = await _ai.Say(s, "istimara");
        Assert.Equal("sequence", r.Application!.Data["vehicle_id_type"]);

        await _ai.Say(s, "123456789");
        await _ai.Say(s, "2022");
        r = await _ai.Say(s, "third party");
        Assert.DoesNotContain("vehicle_value", r.Application!.MissingFields);

        r = await _ai.Say(s, "edit cover type");
        Assert.Contains("Which cover", r.Response);
        r = await _ai.Say(s, "comprehensive");
        r = await _ai.Say(s, "abc");
        Assert.DoesNotContain("vehicle_value", r.Application!.Data.Keys);
        await _ai.Say(s, "85,000");
        await _ai.Say(s, DateTime.UtcNow.AddDays(10).ToString("yyyy-MM-dd"));
        r = await _ai.Say(s, "no");
        Assert.Equal("APP_LOGIN", r.Stage);

        r = await _ai.Say(s, "logged in");
        Assert.Equal("APP_PAYMENT", r.Stage);
        Assert.True(r.Application!.Outputs.ContainsKey("payment_url"));
        Assert.True(r.Application.Outputs.ContainsKey("quote_options"));
        Assert.Contains("never see or enter card", r.Response);

        r = await _ai.Say(s, "paid");
        Assert.Equal(JobStatus.Done, r.Application!.Status);
        Assert.NotNull(r.Application.ReferenceNumber);
    }

    [Fact]
    public async Task Profile_values_carry_over_between_jobs_in_a_session()
    {
        var s = "carry";
        await _ai.Say(s, "track my claim");
        await _ai.Say(s, "CLM-1");
        await _ai.Say(s, "1098765432");
        await _ai.Say(s, "logged in"); // completes the read-only job
        var r = await _ai.Say(s, "I want to buy car insurance");
        Assert.Equal(FlowIds.BuyMotorInsurance, r.Application!.FlowId);
        Assert.Equal("1098765432", r.Application.Data["national_id"]);
    }

    [Fact]
    public async Task Arabic_intents_and_cancel_work()
    {
        var s = "ar";
        var r = await _ai.Say(s, "أريد تتبع مطالبتي", "ar");
        Assert.Equal(FlowIds.TrackAClaim, r.Application!.FlowId);
        await _ai.Say(s, "CLM-77", "ar");
        r = await _ai.Say(s, "إلغاء", "ar");
        Assert.Equal(JobStatus.Cancelled, r.Application!.Status);
    }

    [Fact]
    public async Task Jobs_survive_a_restart_and_the_conversation_continues()
    {
        var s = "restart";
        await _ai.Say(s, "I want to file a claim");
        var r = await _ai.Say(s, "POL-123456");
        var jobId = r.Application!.Id;

        using var sp2 = _fixture.BuildProvider(); // same data dir, fresh container
        var reloaded = await sp2.GetRequiredService<IApplicationOrchestrator>().GetAsync(jobId);
        Assert.NotNull(reloaded);
        Assert.Equal("POL-123456", reloaded!.Data["policy_number"]);
        Assert.Equal(JobStatus.Collecting, reloaded.Status);

        var cont = await sp2.GetRequiredService<IAIPolicyService>().Say(s, "1012345678");
        Assert.Equal(jobId, cont.Application!.Id);
        Assert.True(cont.Application.Data.ContainsKey("national_id"));
    }

    [Fact]
    public async Task Otp_code_is_never_persisted()
    {
        var s = "otp";
        var r = await _ai.Say(s, "update my personal details");
        Assert.Equal(FlowIds.PersonalInfo, r.Application!.FlowId);
        await _ai.Say(s, "Mohammed Al Saud");
        await _ai.Say(s, "0551234567");
        r = await _ai.Say(s, "m@example.com");
        Assert.Equal("APP_LOGIN", r.Stage);
        r = await _ai.Say(s, "logged in");
        Assert.Equal("APP_OTP", r.Stage);
        r = await _ai.Say(s, "4321");
        Assert.Equal("APP_REVIEW", r.Stage);
        Assert.DoesNotContain("otp_code", r.Application!.Data.Keys);
        var onDisk = await File.ReadAllTextAsync(Path.Combine(_fixture.DataDir, "jobs", r.Application.Id + ".json"));
        Assert.DoesNotContain("4321", onDisk);
    }

    [Fact]
    public async Task Playwright_agent_refuses_unverified_flows()
    {
        var agent = new PlaywrightBrowserAgent(NullLogger<PlaywrightBrowserAgent>.Instance);
        var flow = _sp.GetRequiredService<IFlowRegistry>().Get(FlowIds.TrackAClaim)!;
        Assert.False(flow.Verified);
        var result = await agent.RunAsync(flow, new ApplicationJob { Id = "x", LoginCompletedAt = DateTime.UtcNow }, _ => Task.CompletedTask, default);
        Assert.False(result.Succeeded);
        Assert.Contains("not been verified", result.Error);
    }
}

public class RoadsideAssistanceTests : IDisposable
{
    private readonly AgentFixture _fixture = new();
    private readonly ServiceProvider _sp;
    public RoadsideAssistanceTests() { _sp = _fixture.BuildProvider(); }
    public void Dispose() { _sp.Dispose(); _fixture.Dispose(); }

    [Theory]
    [InlineData("i got stuck in forest")]
    [InlineData("my car broke down on the highway")]
    [InlineData("need a tow truck")]
    [InlineData("تعطلت سيارتي")]
    public async Task Breakdown_messages_resolve_to_motor_roadside_assistance_without_the_llm(string message)
    {
        var r = await _sp.GetRequiredService<IAIPolicyService>().Say("rsa", message);
        Assert.Equal("IND-MOT-001", r.SelectedProduct?.Id);
        Assert.Contains("roadside assistance", r.Response);
        Assert.Contains("المساعدة على الطريق", r.ResponseAr);
    }

    [Fact]
    public async Task Stuck_at_airport_is_not_treated_as_a_breakdown()
    {
        // Falls through to the LLM path, which is unreachable in tests -> generic fallback, not motor.
        var r = await _sp.GetRequiredService<IAIPolicyService>().Say("rsa2", "stuck at the airport, flight cancelled");
        Assert.NotEqual("IND-MOT-001", r.SelectedProduct?.Id);
    }
}

public class ProceedWithPlanTests : IDisposable
{
    private readonly AgentFixture _fixture = new();
    private readonly ServiceProvider _sp;
    public ProceedWithPlanTests() { _sp = _fixture.BuildProvider(); }
    public void Dispose() { _sp.Dispose(); _fixture.Dispose(); }

    [Fact]
    public async Task Interested_in_motor_plan_offers_the_guided_journey_and_does_not_loop()
    {
        var ai = _sp.GetRequiredService<IAIPolicyService>();
        await ai.Say("p1", "Motor Insurance");
        var r = await ai.Say("p1", "I am interested in this plan");
        Assert.Equal("PROCEED_MOTOR", r.Stage);
        Assert.Contains("buy motor insurance", r.Response);
        var started = await ai.Say("p1", "I want to buy motor insurance");
        Assert.Equal(FlowIds.BuyMotorInsurance, started.Application?.FlowId);
    }

    [Fact]
    public async Task Interested_in_travel_plan_routes_to_site_instead_of_repeating_details()
    {
        var ai = _sp.GetRequiredService<IAIPolicyService>();
        await ai.Say("p2", "Travel Insurance");
        var first = await ai.Say("p2", "I am interested in this plan");
        var second = await ai.Say("p2", "I am interested in this plan");
        Assert.Equal("CONFIRM", first.Stage);
        Assert.Contains("only motor insurance", first.Response);
        Assert.Contains("mutakamela.sa", first.Response);
        Assert.Equal(first.Response, second.Response); // stable, no re-ask of "Ready to proceed?"
    }
}
