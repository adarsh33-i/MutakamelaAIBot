using MutakamelaAPI.Browser;

namespace MutakamelaAPI.Applications;

/// <summary>A value the extractor believes the customer supplied for one flow field.</summary>
public record SlotCandidate(string FieldId, string Value, double Confidence, string Source);

/// <summary>
/// Perception step of the complaint/portal agent: read a free-text message and
/// propose values for the flow's fields. Proposals are never stored directly;
/// the orchestrator validates each one with the rules engine and asks the
/// customer to confirm anything below the confidence threshold.
/// </summary>
public interface ISlotExtractor
{
    Task<IReadOnlyList<SlotCandidate>> ExtractAsync(FlowSpec flow, string message, IReadOnlyCollection<string> alreadyFilled, string lang, CancellationToken ct = default);
}
