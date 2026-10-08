using Newtonsoft.Json;

namespace MutakamelaAPI.Browser;

/// <summary>
/// Declarative description of one portal journey: the screens in order, the
/// fields on each screen, how to locate them, and what to read back.
/// Specs live in Browser/Flows/*.json and are the only place that knows
/// about portal URLs and selectors, so a portal change is a data edit.
/// </summary>
public class FlowSpec
{
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("title")] public string Title { get; set; } = string.Empty;
    [JsonProperty("title_ar")] public string TitleAr { get; set; } = string.Empty;
    [JsonProperty("url")] public string Url { get; set; } = string.Empty;

    /// <summary>read-only | write | write-with-payment-handoff | external-form-handoff</summary>
    [JsonProperty("kind")] public string Kind { get; set; } = "read-only";

    /// <summary>Whether the portal requires a customer login before this journey.</summary>
    [JsonProperty("requires_login")] public bool RequiresLogin { get; set; } = true;

    /// <summary>Whether a customer approval gate is required before the final submit.</summary>
    [JsonProperty("requires_approval")] public bool RequiresApproval { get; set; }

    /// <summary>Whether to hand off a prefilled public form for the customer to submit themselves.</summary>
    [JsonProperty("external_handoff")] public bool ExternalHandoff { get; set; }

    /// <summary>
    /// Whether this spec has been verified against the live portal. Unverified specs
    /// are refused by the real browser agent and only run in simulation.
    /// </summary>
    [JsonProperty("verified")] public bool Verified { get; set; }

    [JsonProperty("screens")] public List<ScreenSpec> Screens { get; set; } = new();

    /// <summary>Values to read from the final screen, keyed by output id.</summary>
    [JsonProperty("outputs")] public List<OutputSpec> Outputs { get; set; } = new();

    public IEnumerable<FieldSpec> AllFields => Screens.SelectMany(s => s.Fields);

    public FieldSpec? FindField(string id) =>
        AllFields.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));
}

public class ScreenSpec
{
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("title")] public string Title { get; set; } = string.Empty;
    [JsonProperty("title_ar")] public string TitleAr { get; set; } = string.Empty;

    /// <summary>Optional URL fragment or path that identifies this screen once loaded.</summary>
    [JsonProperty("url_contains")] public string? UrlContains { get; set; }

    /// <summary>Selector for an element that proves the screen is ready.</summary>
    [JsonProperty("ready_selector")] public string? ReadySelector { get; set; }

    [JsonProperty("fields")] public List<FieldSpec> Fields { get; set; } = new();

    /// <summary>Selector for the button that advances to the next screen.</summary>
    [JsonProperty("next_selector")] public string? NextSelector { get; set; }

    /// <summary>Selector that matches portal validation messages on this screen.</summary>
    [JsonProperty("error_selector")] public string? ErrorSelector { get; set; }

    /// <summary>
    /// true when this is the payment screen: the agent stops here, captures the
    /// URL for the customer and never interacts with card fields.
    /// </summary>
    [JsonProperty("payment_handoff")] public bool PaymentHandoff { get; set; }

    /// <summary>true when advancing from this screen performs the final submit.</summary>
    [JsonProperty("is_submit")] public bool IsSubmit { get; set; }

    /// <summary>true when the portal sends a one-time code on this screen.</summary>
    [JsonProperty("otp_screen")] public bool OtpScreen { get; set; }
}

public class FieldSpec
{
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("label")] public string Label { get; set; } = string.Empty;
    [JsonProperty("label_ar")] public string LabelAr { get; set; } = string.Empty;

    /// <summary>text | select | date | radio | checkbox | file | otp</summary>
    [JsonProperty("type")] public string Type { get; set; } = "text";
    [JsonProperty("required")] public bool Required { get; set; } = true;

    /// <summary>Only required when another field has one of these values, e.g. {"cover_type": ["comprehensive"]}.</summary>
    [JsonProperty("required_when")] public Dictionary<string, string[]>? RequiredWhen { get; set; }

    /// <summary>Name of a rule in Rules/motor.json applied to this field.</summary>
    [JsonProperty("rule")] public string? Rule { get; set; }

    /// <summary>Allowed values for select/radio fields.</summary>
    [JsonProperty("options")] public List<string>? Options { get; set; }

    /// <summary>
    /// Ordered selector candidates. Prefer role/label locators (both languages);
    /// CSS last. The agent tries them in order and fails safely if none match.
    /// </summary>
    [JsonProperty("selectors")] public List<string> Selectors { get; set; } = new();

    /// <summary>Question the chat asks when this value is missing.</summary>
    [JsonProperty("prompt")] public string Prompt { get; set; } = string.Empty;
    [JsonProperty("prompt_ar")] public string PromptAr { get; set; } = string.Empty;

    /// <summary>customer | document | profile | portal — where the value normally comes from.</summary>
    [JsonProperty("source")] public string Source { get; set; } = "customer";

    /// <summary>Never persisted or echoed back (e.g. OTP codes).</summary>
    [JsonProperty("sensitive")] public bool Sensitive { get; set; }
}

public class OutputSpec
{
    [JsonProperty("id")] public string Id { get; set; } = string.Empty;
    [JsonProperty("label")] public string Label { get; set; } = string.Empty;
    [JsonProperty("label_ar")] public string LabelAr { get; set; } = string.Empty;
    [JsonProperty("selectors")] public List<string> Selectors { get; set; } = new();
}
