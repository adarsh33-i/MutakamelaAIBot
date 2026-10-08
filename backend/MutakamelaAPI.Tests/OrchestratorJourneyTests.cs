using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MutakamelaAPI.Applications;
using MutakamelaAPI.Browser;
using MutakamelaAPI.Models;
using MutakamelaAPI.Rules;
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
    public async Task Track_a_claim_prefills_claim_number_and_leaves_lookup_to_customer()
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
        Assert.Equal("APP_REVIEW", r.Stage);
        Assert.Equal(JobStatus.AwaitingApproval, r.Application!.Status);
        Assert.Equal("CLM-2024-00123", r.Application.Data["claim_number"]);
        Assert.Contains("Track Status yourself", r.Response);
        Assert.Contains("has not sent a lookup", r.Response);
        Assert.DoesNotContain("national_id", r.Application.Data.Keys);

        var flow = _sp.GetRequiredService<IFlowRegistry>().Get(FlowIds.TrackAClaim)!;
        Assert.True(flow.Verified);
        Assert.True(flow.ExternalHandoff);
        Assert.False(flow.RequiresLogin);
        Assert.Equal("https://mutakamela.sa/claim-center/", flow.Url);
        Assert.Contains(flow.AllFields, field =>
            field.Id == "claim_number" &&
            field.Selectors.Contains("#form-field-field1_05bd650"));
        Assert.Equal(FlowIds.TrackAClaim, PortalIntent.Detect("Track claim status"));
    }

    [Fact]
    public async Task Track_claim_status_is_labeled_as_customer_reported_not_portal_verified()
    {
        const string session = "track-reported-status";
        await _ai.Say(session, "Track my claim");
        await _ai.Say(session, "CLM-2026-001");

        var r = await _ai.Say(session, "The portal says status: Under review");

        Assert.Equal("APP_DONE", r.Stage);
        Assert.Equal(JobStatus.Done, r.Application!.Status);
        Assert.Equal("under review", r.Application.Outputs["customerReportedStatus"]);
        Assert.Contains("did not retrieve or independently verify it", r.Response);
    }

    [Fact]
    public async Task Make_a_claim_opens_the_live_entry_gate_without_collecting_identity_or_consent()
    {
        var s = "claim";
        var r = await _ai.Say(s, "I want to file a claim");
        Assert.Equal(FlowIds.MakeAClaim, r.Application!.FlowId);
        Assert.Equal("APP_REVIEW", r.Stage);
        Assert.Equal(JobStatus.AwaitingApproval, r.Application!.Status);
        Assert.Empty(r.Application.Data);
        Assert.Contains("authorization", r.Response);
        Assert.Contains("submit them yourself", r.Response);
        Assert.Equal(FlowIds.MakeAClaim, PortalIntent.Detect("file a claim"));

        var flow = _sp.GetRequiredService<IFlowRegistry>().Get(FlowIds.MakeAClaim)!;
        Assert.True(flow.ExternalHandoff);
        Assert.False(flow.Verified);
        Assert.False(flow.RequiresLogin);
        Assert.Equal("https://eservices.mutakamela.sa/myInsurance/make-a-claim", flow.Url);
        Assert.Empty(flow.AllFields);

        r = await _ai.Say(s, "confirm");
        Assert.Equal(JobStatus.AwaitingApproval, r.Application!.Status);
        Assert.DoesNotContain(r.Application.Events, e => e.Message.Contains("Clicking submit"));
    }

    [Fact]
    public async Task Complaint_handoff_opens_only_after_validation_and_never_submits_from_chat()
    {
        var flow = _sp.GetRequiredService<IFlowRegistry>().Get(FlowIds.SubmitComplaint)!;
        Assert.True(flow.Verified);
        Assert.True(flow.ExternalHandoff);
        Assert.Equal("https://mutakamela.sa/submit-your-complaints/", flow.Url);
        Assert.Equal(FlowIds.SubmitComplaint, PortalIntent.Detect("أريد تقديم شكوى"));
        Assert.Null(_sp.GetRequiredService<IRulesEngine>()
            .Validate(flow.FindField("city")!, "Northern Province", out var normalizedCity));
        Assert.Equal("northen province", normalizedCity);
        Assert.Contains(flow.AllFields, field =>
            field.Id == "product" && field.Type == "select" &&
            field.Selectors.Contains("#form-field-field_e732973"));

        const string session = "complaint";
        var r = await _ai.Say(session, "I want to submit a complaint");
        Assert.Equal(FlowIds.SubmitComplaint, r.Application!.FlowId);
        Assert.Equal("full_name", r.Application.MissingFields[0]);

        foreach (var answer in new[]
        {
            "Maha Al Saud", "1234567890", "0551234567", "Jeddah",
            "maha@example.com", "MIP", "A delayed response to my policy cancellation request"
        })
            r = await _ai.Say(session, answer);

        Assert.Equal("APP_REVIEW", r.Stage);
        Assert.Equal(JobStatus.AwaitingApproval, r.Application!.Status);
        Assert.Equal("jeddah", r.Application.Data["city"]);
        Assert.Equal("MIP", r.Application.Data["product"]);
        Assert.Null(r.Application.ReferenceNumber);
        Assert.Contains("click Submit there", r.Response);
        Assert.Contains("Nothing was submitted", r.Application.Review!.Disclaimer);
        Assert.Contains(r.Application.Review.Lines, line => line.Field == "product" && line.Value == "MIP");

        var orchestrator = _sp.GetRequiredService<IApplicationOrchestrator>();
        var apiApproval = await orchestrator.ApproveAsync(r.Application.Id, "en");
        Assert.Equal(JobStatus.AwaitingApproval, apiApproval!.Status);

        r = await _ai.Say(session, "confirm");
        Assert.Equal(JobStatus.AwaitingApproval, r.Application!.Status);
        Assert.DoesNotContain(r.Application.Events, e => e.Message.Contains("Clicking submit"));
        Assert.DoesNotContain((await orchestrator.GetAsync(r.Application.Id))!.Events, e => e.Message.Contains("Submitting →"));

        r = await _ai.Say(session, "i submitted 2600044565 this is a complaint no");
        Assert.Equal(JobStatus.Done, r.Application!.Status);
        Assert.Equal("APP_DONE", r.Stage);
        Assert.Equal("true", r.Application.Outputs["externalSubmissionReported"]);
        Assert.Equal("2600044565", r.Application.Outputs["complaintNumber"]);
        Assert.Equal("2600044565", r.Application.ReferenceNumber);
        Assert.Contains("Complaint number: 2600044565", r.Response);
        Assert.Contains("now closed", r.Response);

        // The job is closed: the customer is free to do anything else.
        r = await _ai.Say(session, "show me travel insurance");
        Assert.Null(r.Application);
        Assert.DoesNotContain("complaint", r.Response, StringComparison.OrdinalIgnoreCase);

        // And can start a brand-new complaint if they want.
        r = await _ai.Say(session, "I want to file another complaint");
        Assert.Equal(FlowIds.SubmitComplaint, r.Application!.FlowId);
        Assert.Equal(JobStatus.Collecting, r.Application.Status);
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
    public async Task Track_claim_only_requests_claim_number_and_keeps_identity_entry_on_portal()
    {
        var s = "carry";
        var r = await _ai.Say(s, "track my claim");
        Assert.Equal(FlowIds.TrackAClaim, r.Application!.FlowId);
        Assert.Equal("claim_number", r.Application.MissingFields.Single());
        r = await _ai.Say(s, "CLM-1");
        Assert.Equal(JobStatus.AwaitingApproval, r.Application!.Status);
        Assert.DoesNotContain("national_id", r.Application.Data.Keys);
        Assert.Contains("Enter your ID/Iqama/CR yourself", r.Response);
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
        await _ai.Say(s, "update my personal details");
        await _ai.Say(s, "Maha Al Saud");
        await _ai.Say(s, "0551234567");
        var r = await _ai.Say(s, "maha@example.com");
        var jobId = r.Application!.Id;

        using var sp2 = _fixture.BuildProvider(); // same data dir, fresh container
        var reloaded = await sp2.GetRequiredService<IApplicationOrchestrator>().GetAsync(jobId);
        Assert.NotNull(reloaded);
        Assert.Equal("0551234567", reloaded!.Data["mobile"]);
        Assert.Equal("maha@example.com", reloaded.Data["email"]);
        Assert.Equal(JobStatus.AwaitingLogin, reloaded.Status);

        var cont = await sp2.GetRequiredService<IAIPolicyService>().Say(s, "logged in");
        Assert.Equal(jobId, cont.Application!.Id);
        Assert.Equal("APP_OTP", cont.Stage);
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
        var flow = _sp.GetRequiredService<IFlowRegistry>().Get(FlowIds.MakeAClaim)!;
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
    public async Task Breakdown_messages_resolve_to_motor_via_retrieval(string message)
    {
        // The LLM is unreachable in tests; the RAG fallback must still ground the answer in Motor.
        var r = await _sp.GetRequiredService<IAIPolicyService>().Say("rsa", message);
        Assert.Equal("IND-MOT-001", r.SelectedProduct?.Id);
    }

    [Fact]
    public async Task Stuck_at_airport_is_not_treated_as_a_breakdown()
    {
        // Falls through to the LLM path, which is unreachable in tests -> generic fallback, not motor.
        var r = await _sp.GetRequiredService<IAIPolicyService>().Say("rsa2", "stuck at the airport, flight cancelled");
        Assert.NotEqual("IND-MOT-001", r.SelectedProduct?.Id);
    }

    [Fact]
    public async Task Motor_quick_option_returns_catalog_details_instead_of_sales_handoff()
    {
        var r = await _sp.GetRequiredService<IAIPolicyService>().Say("motor-quick-option", "Motor Insurance");
        Assert.Equal("DETAILS", r.Stage);
        Assert.Equal("IND-MOT-001", r.SelectedProduct?.Id);
        Assert.Contains(r.ProductDetails!.Coverage, item => item.Contains("Third Party Liability"));
        Assert.DoesNotContain("quote requests", r.Response, StringComparison.OrdinalIgnoreCase);
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
