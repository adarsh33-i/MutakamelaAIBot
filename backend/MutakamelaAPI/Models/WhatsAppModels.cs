using Newtonsoft.Json;

namespace MutakamelaAPI.Models;

// ============ Incoming Webhook Models ============

public class WhatsAppWebhook
{
    [JsonProperty("object")]
    public string Object { get; set; } = string.Empty;

    [JsonProperty("entry")]
    public List<WebhookEntry> Entry { get; set; } = new();
}

public class WebhookEntry
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("changes")]
    public List<WebhookChange> Changes { get; set; } = new();
}

public class WebhookChange
{
    [JsonProperty("value")]
    public WebhookValue Value { get; set; } = new();

    [JsonProperty("field")]
    public string Field { get; set; } = string.Empty;
}

public class WebhookValue
{
    [JsonProperty("messaging_product")]
    public string MessagingProduct { get; set; } = string.Empty;

    [JsonProperty("metadata")]
    public WebhookMetadata Metadata { get; set; } = new();

    [JsonProperty("contacts")]
    public List<WebhookContact> Contacts { get; set; } = new();

    [JsonProperty("messages")]
    public List<WebhookMessage> Messages { get; set; } = new();

    [JsonProperty("statuses")]
    public List<WebhookStatus> Statuses { get; set; } = new();
}

public class WebhookMetadata
{
    [JsonProperty("display_phone_number")]
    public string DisplayPhoneNumber { get; set; } = string.Empty;

    [JsonProperty("phone_number_id")]
    public string PhoneNumberId { get; set; } = string.Empty;
}

public class WebhookContact
{
    [JsonProperty("profile")]
    public ContactProfile Profile { get; set; } = new();

    [JsonProperty("wa_id")]
    public string WaId { get; set; } = string.Empty;
}

public class ContactProfile
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;
}

public class WebhookMessage
{
    [JsonProperty("from")]
    public string From { get; set; } = string.Empty;

    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    [JsonProperty("text")]
    public MessageText? Text { get; set; }

    [JsonProperty("interactive")]
    public MessageInteractive? Interactive { get; set; }
}

public class MessageText
{
    [JsonProperty("body")]
    public string Body { get; set; } = string.Empty;
}

public class MessageInteractive
{
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    [JsonProperty("button_reply")]
    public InteractiveReply? ButtonReply { get; set; }

    [JsonProperty("list_reply")]
    public InteractiveReply? ListReply { get; set; }
}

public class InteractiveReply
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;
}

public class WebhookStatus
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonProperty("recipient_id")]
    public string RecipientId { get; set; } = string.Empty;
}

// ============ Outgoing Message Models ============

public class SendMessageRequest
{
    [JsonProperty("messaging_product")]
    public string MessagingProduct { get; set; } = "whatsapp";

    [JsonProperty("recipient_type")]
    public string RecipientType { get; set; } = "individual";

    [JsonProperty("to")]
    public string To { get; set; } = string.Empty;

    [JsonProperty("type")]
    public string Type { get; set; } = "text";

    [JsonProperty("text")]
    public SendMessageText? Text { get; set; }

    [JsonProperty("interactive")]
    public SendMessageInteractive? Interactive { get; set; }
}

public class SendMessageText
{
    [JsonProperty("preview_url")]
    public bool PreviewUrl { get; set; } = false;

    [JsonProperty("body")]
    public string Body { get; set; } = string.Empty;
}

public class SendMessageInteractive
{
    [JsonProperty("type")]
    public string Type { get; set; } = "button";

    [JsonProperty("body")]
    public InteractiveBody Body { get; set; } = new();

    [JsonProperty("action")]
    public InteractiveAction Action { get; set; } = new();
}

public class InteractiveBody
{
    [JsonProperty("text")]
    public string Text { get; set; } = string.Empty;
}

public class InteractiveAction
{
    [JsonProperty("buttons")]
    public List<InteractiveButton> Buttons { get; set; } = new();
}

public class InteractiveButton
{
    [JsonProperty("type")]
    public string Type { get; set; } = "reply";

    [JsonProperty("reply")]
    public ButtonReply Reply { get; set; } = new();
}

public class ButtonReply
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;
}

// ============ AI Response Models ============

public class AIPolicyResponse
{
    public string Stage { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public string DetectedLob { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public SelectedProduct? SelectedProduct { get; set; }
    public List<SelectedProduct> ProductOptions { get; set; } = new();
    public ProductDetails? ProductDetails { get; set; }
    public string Response { get; set; } = string.Empty;
    public string ResponseAr { get; set; } = string.Empty;
    public string NextAction { get; set; } = string.Empty;
    /// <summary>Present when the message belongs to an agentic portal job.</summary>
    public ApplicationJobResponse? Application { get; set; }
}

public class SelectedProduct
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}

public class ProductDetails
{
    public List<string> Coverage { get; set; } = new();
    public List<string> CoverageAr { get; set; } = new();
    public List<string> Features { get; set; } = new();
    public List<string> KeyBenefits { get; set; } = new();
}

// ============ API Request/Response Models ============

public class ChatRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
}

public class ChatResponse
{
    public bool Success { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public string ResponseAr { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string DetectedLob { get; set; } = string.Empty;
    public SelectedProduct? SelectedProduct { get; set; }
    public List<SelectedProduct> ProductOptions { get; set; } = new();
    public ProductDetails? ProductDetails { get; set; }
    public ApplicationJobResponse? Application { get; set; }
}

public class HealthResponse
{
    public string Status { get; set; } = "healthy";
    public string Service { get; set; } = "Mutakamela Insurance API";
    public int ActiveSessions { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
