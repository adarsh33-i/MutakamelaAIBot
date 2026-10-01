using MutakamelaAPI.Models;

namespace MutakamelaAPI.Applications;

/// <summary>
/// Maps a message to one of the portal journeys the agentic engine can run.
/// Deterministic keyword matching on purpose: starting a job is a side effect
/// and must not depend on the LLM. Returns null when no journey applies.
/// </summary>
public static class PortalIntent
{
    public static string? Detect(string message)
    {
        var m = message.Trim().ToLowerInvariant();
        bool Has(params string[] words) => words.Any(m.Contains);

        if (Has("track") && Has("claim", "مطالب") || Has("claim status", "حالة المطالبة", "حالة مطالبتي", "تتبع المطالبة", "تتبع مطالبة", "تتبع مطالبتي"))
            return FlowIds.TrackAClaim;

        if (Has("claim", "مطالبة") && Has("file", "make", "submit", "open", "report", "new", "start", "تقديم", "رفع", "فتح", "أريد", "اريد", "ابغى", "أبغى") ||
            m == "claims" || m == "i want to file a claim")
            return FlowIds.MakeAClaim;

        if (Has("personal info", "personal details", "update my details", "update my info", "contact details", "my profile", "بياناتي الشخصية", "تحديث بياناتي", "معلوماتي الشخصية"))
            return FlowIds.PersonalInfo;

        var motor = Has("motor", "car", "vehicle", "سيارة", "مركبة", "مركبات");
        var buy = Has("buy", "quote", "purchase", "get insurance", "insure my", "renew", "شراء", "اشتري", "عرض سعر", "تسعير", "تجديد", "أمن", "امن");
        if (motor && buy) return FlowIds.BuyMotorInsurance;
        if (Has("buy insurance", "شراء تأمين") || (buy && Has("insurance", "تأمين") && !motor))
            return FlowIds.BuyInsurance;

        return null;
    }
}
