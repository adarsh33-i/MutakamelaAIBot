using Microsoft.Extensions.DependencyInjection;
using MutakamelaAPI.Applications;
using MutakamelaAPI.Models;
using MutakamelaAPI.Services;
using Xunit;

namespace MutakamelaAPI.Tests;

/// <summary>
/// Agentic complaint intake: the opening message is perceived as a whole, confident
/// values are stored (after rules validation), uncertain ones are confirmed, and only
/// the missing fields are asked. The LLM is unreachable in tests, so these exercise
/// the deterministic extraction layer and the orchestrator's handling of candidates.
/// </summary>
public class SlotExtractionTests : IDisposable
{
    private readonly AgentFixture _fixture = new();
    private readonly ServiceProvider _sp;
    private readonly IAIPolicyService _ai;

    public SlotExtractionTests()
    {
        _sp = _fixture.BuildProvider();
        _ai = _sp.GetRequiredService<IAIPolicyService>();
    }

    public void Dispose() { _sp.Dispose(); _fixture.Dispose(); }

    [Fact]
    public async Task Opening_message_fills_several_complaint_fields_at_once()
    {
        var r = await _ai.Say("se1",
            "I want to file a complaint. My name is Ahmed Al Qahtani, ID 1098765432, mobile 0551234567, email ahmed@example.com. My motor insurance claim CLM-2024-118 was rejected with no explanation.");

        Assert.Equal(FlowIds.SubmitComplaint, r.Application!.FlowId);
        var data = r.Application.Data;
        Assert.Equal("Ahmed Al Qahtani", data["full_name"]);
        Assert.Equal("1098765432", data["complaint_identifier"]);
        Assert.Equal("0551234567", data["phone"]);
        Assert.Equal("ahmed@example.com", data["email"]);
        Assert.Equal("CLM-2024-118", data["claim_number"]);
        Assert.Equal("Motor Insurance", data["product"]);          // resolved through the RAG retriever
        Assert.Contains("I've noted", r.Response);

        // Only what is genuinely missing is asked next.
        Assert.DoesNotContain("full_name", r.Application.MissingFields);
        Assert.DoesNotContain("phone", r.Application.MissingFields);
        Assert.Contains("city", r.Application.MissingFields);
        Assert.Contains("description", r.Application.MissingFields);
    }

    [Fact]
    public async Task Review_card_shows_where_each_value_came_from()
    {
        var s = "se2";
        await _ai.Say(s, "file a complaint: I'm Sara Ali, 2098765432, 0559876543, sara@x.com, about my car insurance policy");
        await _ai.Say(s, "Riyadh");
        var r = await _ai.Say(s, "skip");       // policy number (optional)
        r = await _ai.Say(s, "skip");           // claim number (optional)
        r = await _ai.Say(s, "The agent promised a callback three weeks ago and never called. I want a written response.");

        Assert.Equal(JobStatus.AwaitingApproval, r.Application!.Status);
        var lines = r.Application.Review!.Lines;
        Assert.Equal("extracted", lines.Single(l => l.Field == "phone").Source);
        Assert.Equal("retrieved", lines.Single(l => l.Field == "product").Source);
        Assert.Equal("typed", lines.Single(l => l.Field == "city").Source);
        Assert.Equal("typed", lines.Single(l => l.Field == "description").Source);
    }

    [Fact]
    public async Task Extracted_values_still_go_through_the_rules_engine()
    {
        // 3098765432 is not a valid ID (must start with 1 or 2) and must not be stored.
        var r = await _ai.Say("se3", "I want to complain, my id is 3098765432 and mobile 0551234567");
        Assert.DoesNotContain("complaint_identifier", r.Application!.Data.Keys);
        Assert.Equal("0551234567", r.Application.Data["phone"]);
        // A 10-digit number that is not a valid ID/Iqama shape is asked back, not stored silently.
        Assert.Contains("3098765432", r.Response);
        Assert.Contains("Is that right?", r.Response);
        r = await _ai.Say("se3", "no");
        Assert.DoesNotContain("complaint_identifier", r.Application!.Data.Keys);
        r = await _ai.Say("se3", "1098765432");
        Assert.Equal("1098765432", r.Application!.Data["complaint_identifier"]);
    }

    [Fact]
    public async Task Answer_containing_the_value_inside_a_sentence_is_accepted()
    {
        var s = "se4";
        await _ai.Say(s, "I want to file a complaint");
        await _ai.Say(s, "Maha Al Saud");
        var r = await _ai.Say(s, "sure, my id number is 1234567890 thanks");
        Assert.Equal("1234567890", r.Application!.Data["complaint_identifier"]);
        Assert.Equal("extracted", r.Application.Review!.Lines.Single(l => l.Field == "complaint_identifier").Source);
    }

    [Fact]
    public async Task Card_numbers_in_the_opening_message_are_never_extracted()
    {
        var r = await _ai.Say("se5", "I want to file a complaint, my card 4111 1111 1111 1111 was charged twice, mobile 0551234567");
        Assert.DoesNotContain(r.Application!.Data.Values, v => v.Contains("4111"));
        Assert.Equal("0551234567", r.Application.Data["phone"]);
    }

    [Fact]
    public async Task Short_single_answers_do_not_trigger_product_resolution()
    {
        var s = "se6";
        await _ai.Say(s, "I want to file a complaint");
        var r = await _ai.Say(s, "Omar Hassan");
        Assert.Equal("Omar Hassan", r.Application!.Data["full_name"]);
        Assert.DoesNotContain("product", r.Application.Data.Keys);
    }

    [Fact]
    public async Task Portal_relayed_confirmation_closes_the_complaint_and_frees_the_chat()
    {
        var s = "se8";
        await _ai.Say(s, "file a complaint: I'm Sara Ali, 2098765432, 0559876543, sara@x.com, about my car insurance policy");
        await _ai.Say(s, "Riyadh"); await _ai.Say(s, "skip"); await _ai.Say(s, "skip");
        var r = await _ai.Say(s, "Nobody answered my calls for two weeks.");
        Assert.Equal(JobStatus.AwaitingApproval, r.Application!.Status);

        var orch = _sp.GetRequiredService<IApplicationOrchestrator>();
        var closed = await orch.ConfirmExternalSubmissionAsync(r.Application.Id, "2600099001", "en");
        Assert.Equal(JobStatus.Done, closed!.Status);
        Assert.Equal("2600099001", closed.ReferenceNumber);
        Assert.Contains("submitted on Mutakamela's website", closed.StatusMessage);
        Assert.Contains(closed.Events, e => e.Message.Contains("Browser extension relayed"));

        // Idempotent: a second relay does not change anything.
        var again = await orch.ConfirmExternalSubmissionAsync(r.Application.Id, "2600099001", "en");
        Assert.Equal(JobStatus.Done, again!.Status);

        r = await _ai.Say(s, "i got stuck in forest");
        Assert.Null(r.Application);
    }

    [Fact]
    public async Task Existing_step_by_step_flow_still_works_unchanged()
    {
        var s = "se7";
        var r = await _ai.Say(s, "I want to submit a complaint");
        foreach (var answer in new[] { "Maha Al Saud", "1234567890", "0551234567", "Jeddah", "maha@example.com", "MIP", "skip", "skip", "A delayed response to my cancellation request" })
            r = await _ai.Say(s, answer);
        Assert.Equal(JobStatus.AwaitingApproval, r.Application!.Status);
        Assert.Equal("MIP", r.Application.Data["product"]);
        Assert.Equal("jeddah", r.Application.Data["city"]);
    }
}
