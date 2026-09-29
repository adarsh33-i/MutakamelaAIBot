using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using MutakamelaAPI.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MutakamelaAPI.Services;

public class AIPolicyService : IAIPolicyService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<AIPolicyService> _logger;
    private readonly ISessionManager _sessionManager;
    private readonly string _apiUrl;
    private readonly string _model;
    private readonly string _productsJson;
    private readonly JArray _productCatalog = new();
    private readonly ConcurrentDictionary<string, ClaimDraft> _claimDrafts = new();

    private enum ClaimStep
    {
        PolicyReference,
        IncidentDate,
        IncidentDescription,
        Contact,
        EditField,
        EditValue,
        Confirm,
        Complete
    }

    private enum ClaimField
    {
        PolicyReference,
        IncidentDate,
        IncidentDescription,
        Contact
    }

    private sealed class ClaimDraft
    {
        public ClaimStep Step { get; set; } = ClaimStep.PolicyReference;
        public string PolicyReference { get; set; } = string.Empty;
        public string IncidentDate { get; set; } = string.Empty;
        public string IncidentDescription { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;
        public ClaimField? EditingField { get; set; }
    }

    public AIPolicyService(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<AIPolicyService> logger,
        ISessionManager sessionManager)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
        _sessionManager = sessionManager;

        _apiUrl = (_config["AI:BaseUrl"] ?? "http://localhost:11434/v1").TrimEnd('/');
        _model = _config["AI:Model"] ?? "qwen3:8b";

        var apiKey = _config["AI:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        // Load products
        _productsJson = LoadProducts();
    }

    private string LoadProducts()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "products.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var products = JObject.Parse(json)["products"];
                if (products is JArray productArray)
                {
                    foreach (var product in productArray)
                        _productCatalog.Add(product.DeepClone());
                }
                return BuildProductSummary(products);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load products.json");
        }

        return GetDefaultProducts();
    }

    private string BuildProductSummary(JToken? products)
    {
        if (products == null) return GetDefaultProducts();

        var sb = new StringBuilder();
        string currentLob = "";

        foreach (var p in products)
        {
            var lob = p["lob"]?.ToString() ?? "";
            if (lob != currentLob)
            {
                currentLob = lob;
                sb.AppendLine($"\n=== {lob} ===");
            }

            var coverage = string.Join(", ", p["coverage"]?.Take(3).Select(c => c.ToString()) ?? Array.Empty<string>());
            sb.AppendLine($"{p["id"]}: {p["name"]} ({p["name_ar"]})");
            sb.AppendLine($"  Coverage: {coverage}");
            sb.AppendLine($"  Best for: {string.Join(", ", p["suitable_for"]?.Take(2).Select(s => s.ToString()) ?? Array.Empty<string>())}");
        }

        return sb.ToString();
    }

    private string GetDefaultProducts()
    {
        return @"
=== MOTOR ===
IND-MOT-001: Motor Insurance (تأمين المركبات)
  Coverage: Third Party, Own Damage, Theft
  Best for: Car owners, Daily commuters

=== TRAVEL ===
IND-TRV-001: Travel Insurance (تأمين السفر)
  Coverage: Medical emergencies, Trip cancellation, Lost baggage
  Best for: Vacationers, Business travelers

=== HEALTH ===
CORP-HLT-001: Corporate Health Insurance (التأمين الصحي للشركات)
  Coverage: Hospitalization, Outpatient, Emergency
  Best for: Companies, Employees

=== MARINE ===
CORP-MAR-001: Marine Cargo Insurance (تأمين البضائع البحرية)
  Coverage: Goods in transit, Damage, Theft
  Best for: Importers, Exporters

=== ENGINEERING ===
CORP-ENG-001: Contractors All Risks (جميع أخطار المقاولين)
  Coverage: Construction works, Equipment, Third party
  Best for: Contractors, Developers
";
    }

    private static AIPolicyResponse GetSalesHandoffResponse()
    {
        return new AIPolicyResponse
        {
            Stage = "CONFIRM",
            Intent = "GET_QUOTE",
            Response = "This chat can explain insurance products, but quote requests, payments, and policy activation are not connected here. Please contact Mutakamela at mutakamela.sa for official next steps.",
            ResponseAr = "يمكن لهذه المحادثة شرح منتجات التأمين، لكن طلبات عروض الأسعار والمدفوعات وتفعيل الوثائق غير متصلة هنا. للمتابعة الرسمية، يرجى التواصل مع متكاملة عبر mutakamela.sa.",
            NextAction = "Visit Mutakamela for official assistance"
        };
    }

    private static bool IsUnsupportedSalesRequest(string message)
    {
        var normalized = message.Trim().ToLowerInvariant();
        var containsContactDetails =
            normalized.Contains('@') ||
            normalized.Contains("phone") ||
            normalized.Contains("email") ||
            normalized.Contains("mobile number") ||
            normalized.Contains("contact detail") ||
            normalized.Contains("contact information") ||
            normalized.Contains("my name is");

        return containsContactDetails ||
               normalized.Contains("quote") ||
               normalized.Contains("payment") ||
               normalized.Contains("purchase") ||
               normalized.Contains("buy this policy") ||
               normalized.Contains("continue with this plan") ||
               normalized == "i am interested in this plan" ||
               normalized == "yes, continue";
    }

    private static bool IsOtherOptionsRequest(string message)
    {
        var normalized = message.Trim().ToLowerInvariant();
        return normalized.Contains("other option") || normalized.Contains("other plan") ||
               normalized.Contains("different option") || normalized.Contains("alternative") ||
               normalized.Contains("something else") || normalized.Contains("خيارات أخرى") ||
               normalized.Contains("خيارات اخرى") || normalized.Contains("بدائل");
    }

    private static bool IsMoreDetailsRequest(string message)
    {
        var normalized = message.Trim().ToLowerInvariant();
        return normalized.Contains("tell me more") || normalized.Contains("more details") ||
               normalized.Contains("more info") || normalized.Contains("give me details") ||
               normalized.Contains("تفاصيل أكثر") || normalized.Contains("تفاصيل اكثر") ||
               normalized.Contains("مزيد من التفاصيل") || normalized.Contains("أخبرني المزيد");
    }

    private static int ProductMentionScore(JToken product, string message)
    {
        var directIdentifiers = new[]
        {
            product["id"]?.ToString(),
            product["name"]?.ToString(),
            product["name_ar"]?.ToString()
        };

        if (directIdentifiers.Any(identifier =>
                !string.IsNullOrWhiteSpace(identifier) &&
                message.Contains(identifier, StringComparison.OrdinalIgnoreCase)))
            return 3;

        var lob = product["lob"]?.ToString();
        if (!string.IsNullOrWhiteSpace(lob) && message.Contains(lob, StringComparison.OrdinalIgnoreCase))
            return 2;

        return product["keywords"] is JArray keywords && keywords.Any(keyword =>
            keyword.Type == JTokenType.String &&
            keyword.ToString().Length > 2 &&
            message.Contains(keyword.ToString(), StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
    }

    private JToken? FindProductInText(string message)
    {
        return _productCatalog
            .Select(product => new { Product = product, Score = ProductMentionScore(product, message) })
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .Select(match => match.Product)
            .FirstOrDefault();
    }

    private JToken? FindPriorProduct(UserSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.SelectedProductId))
        {
            var selectedProduct = _productCatalog.FirstOrDefault(product =>
                product["id"]?.ToString() == session.SelectedProductId);
            if (selectedProduct != null) return selectedProduct;
        }

        var previousMessages = session.Messages.Take(Math.Max(0, session.Messages.Count - 1)).ToArray();
        foreach (var message in previousMessages.Reverse())
        {
            var product = FindProductInText(message.Content);
            if (product != null)
            {
                session.SelectedProductId = product["id"]?.ToString();
                return product;
            }
        }

        return null;
    }

    private AIPolicyResponse BuildProductDetailsResponse(UserSession session)
    {
        var product = FindPriorProduct(session);
        if (product == null)
        {
            return new AIPolicyResponse
            {
                Stage = "IDENTIFY",
                Intent = "GET_INFO",
                Response = "Which insurance product would you like more details about?",
                ResponseAr = "عن أي منتج تأميني ترغب في معرفة المزيد؟"
            };
        }

        var name = product["name"]?.ToString() ?? "Insurance product";
        var nameAr = product["name_ar"]?.ToString() ?? name;
        var description = product["description"]?.ToString() ?? string.Empty;
        var descriptionAr = product["description_ar"]?.ToString() ?? string.Empty;
        var coverage = product["coverage"]?.Select(item => item.ToString()).Take(4).ToList() ?? new List<string>();
        var coverageAr = product["coverage_ar"]?.Select(item => item.ToString()).Take(4).ToList() ?? new List<string>();
        var features = product["features"]?.Select(item => item.ToString()).Take(3).ToList() ?? new List<string>();
        var suitableFor = product["suitable_for"]?.Select(item => item.ToString()).Take(3).ToList() ?? new List<string>();
        var response = $"{name}: {description} Coverage includes {string.Join("; ", coverage)}. Suitable for {string.Join(", ", suitableFor)}.";
        var responseAr = $"{nameAr}: {descriptionAr}" + (coverageAr.Count > 0
            ? $" تشمل التغطية: {string.Join("، ", coverageAr)}."
            : string.Empty);

        return new AIPolicyResponse
        {
            Stage = "DETAILS",
            Intent = "GET_INFO",
            DetectedLob = product["lob"]?.ToString() ?? string.Empty,
            Confidence = 1,
            SelectedProduct = new SelectedProduct
            {
                Id = product["id"]?.ToString() ?? string.Empty,
                Name = name,
                NameAr = nameAr,
                Category = product["category"]?.ToString() ?? string.Empty
            },
            ProductDetails = new ProductDetails
            {
                Coverage = coverage,
                CoverageAr = coverageAr,
                Features = features
            },
            Response = response,
            ResponseAr = responseAr,
            NextAction = "Choose whether to continue, compare options, or ask a question"
        };
    }

    private AIPolicyResponse BuildOtherOptionsResponse(UserSession session)
    {
        var priorProduct = FindPriorProduct(session);

        var sameCategoryOptions = priorProduct == null
            ? new List<JToken>()
            : _productCatalog
                .Where(product => product["id"]?.ToString() != priorProduct["id"]?.ToString() &&
                                  product["lob"]?.ToString() == priorProduct["lob"]?.ToString())
                .Take(3)
                .ToList();
        var options = sameCategoryOptions.Count > 0
            ? sameCategoryOptions
            : _productCatalog
                .Where(product => product["id"]?.ToString() != priorProduct?["id"]?.ToString() &&
                                  (priorProduct == null || product["lob"]?.ToString() != priorProduct["lob"]?.ToString()))
                .GroupBy(product => product["lob"]?.ToString())
                .Select(group => group.First())
                .Take(3)
                .ToList();

        var optionNames = options.Select(product => product["name"]?.ToString()).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
        var optionNamesAr = options.Select(product => product["name_ar"]?.ToString()).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
        var hasSameCategoryOptions = sameCategoryOptions.Count > 0;
        var response = optionNames.Length == 0
            ? "I couldn't find other plans in the available catalog. Please visit mutakamela.sa to explore current coverage."
            : hasSameCategoryOptions
                ? $"Other {priorProduct!["lob"]} options available: {string.Join(", ", optionNames)}. Which would you like to explore?"
                : priorProduct == null
                    ? $"Available insurance options include: {string.Join(", ", optionNames)}. Which would you like to explore?"
                    : $"The catalog doesn't list another {priorProduct["lob"]} plan. Other available options include: {string.Join(", ", optionNames)}. Which would you like to explore?";
        var responseAr = optionNamesAr.Length == 0
            ? "لم أجد خططاً أخرى في قائمة المنتجات المتاحة. يرجى زيارة mutakamela.sa للاطلاع على التغطيات الحالية."
            : hasSameCategoryOptions
                ? $"تتوفر خيارات أخرى ضمن فئة {priorProduct!["lob"]}: {string.Join("، ", optionNamesAr)}. أيها تود معرفة المزيد عنها؟"
                : priorProduct == null
                    ? $"تشمل خيارات التأمين المتاحة: {string.Join("، ", optionNamesAr)}. أيها تود معرفة المزيد عنها؟"
                    : $"لا تتضمن القائمة خطة أخرى ضمن فئة {priorProduct["lob"]}. تشمل الخيارات الأخرى المتاحة: {string.Join("، ", optionNamesAr)}. أيها تود معرفة المزيد عنها؟";

        return new AIPolicyResponse
        {
            Stage = "IDENTIFY",
            Intent = "GET_INFO",
            DetectedLob = priorProduct?["lob"]?.ToString() ?? string.Empty,
            Response = response,
            ResponseAr = responseAr,
            NextAction = "Choose an option to explore"
        };
    }

    private static bool IsClaimStartRequest(string message)
    {
        var normalized = message.Trim().ToLowerInvariant();
        return normalized.Contains("claim") &&
               (normalized.Contains("file") || normalized.Contains("start") ||
                normalized.Contains("open") || normalized.Contains("report") ||
                normalized == "claims");
    }

    private static bool ContainsPaymentCredentials(string message)
    {
        var normalized = message.ToLowerInvariant();
        var digits = Regex.Replace(message, @"\D", string.Empty);
        return normalized.Contains("credit card") || normalized.Contains("debit card") ||
               normalized.Contains("card number") || normalized.Contains("cvv") ||
               normalized.Contains("cvc") || normalized.Contains("bank account") ||
               normalized.Contains("iban") || normalized.Contains("routing number") ||
               (digits.Length is >= 13 and <= 19);
    }

    private static bool LooksLikeContact(string message)
    {
        var digits = Regex.Replace(message, @"\D", string.Empty);
        return message.Contains('@') || digits.Length >= 7;
    }

    private static AIPolicyResponse BuildClaimResponse(string stage, string response, string responseAr = "")
    {
        return new AIPolicyResponse
        {
            Stage = stage,
            Intent = "CLAIM",
            Response = response,
            ResponseAr = responseAr,
            NextAction = stage
        };
    }

    private AIPolicyResponse ProcessClaimMessage(UserSession session, string sessionId, string message, ClaimDraft? draft)
    {
        if (draft == null)
        {
            draft = new ClaimDraft();
            _claimDrafts[sessionId] = draft;
            session.AddMessage("user", "Claim intake started.");
            var started = BuildClaimResponse(
                "CLAIM_INTAKE",
                "I can prepare a claim draft. It will not be sent to the insurer from this prototype. What is your policy reference? Please do not share payment or card details.",
                "يمكنني إعداد مسودة مطالبة، لكنها لن تُرسل إلى شركة التأمين من هذا النموذج الأولي. ما رقم وثيقتك؟ يرجى عدم مشاركة بيانات الدفع أو البطاقة.");
            session.AddMessage("assistant", "Claim intake started.");
            return started;
        }

        if (ContainsPaymentCredentials(message))
        {
            session.AddMessage("user", "Payment details were not collected.");
            var stage = draft.Step switch
            {
                ClaimStep.Confirm => "CLAIM_CONFIRM",
                ClaimStep.EditField => "CLAIM_EDIT",
                ClaimStep.EditValue => "CLAIM_EDIT_VALUE",
                _ => "CLAIM_INTAKE"
            };
            return BuildClaimResponse(
                stage,
                "Please do not share card, bank, CVV, or payment details here. They are not needed to prepare this claim draft. " + GetClaimStepPrompt(draft.Step),
                "يرجى عدم مشاركة بيانات البطاقة أو الحساب البنكي أو رمز الأمان أو الدفع هنا. هذه البيانات غير مطلوبة لإعداد مسودة المطالبة. " + GetClaimStepPromptAr(draft.Step));
        }

        if (draft.Step == ClaimStep.EditField)
        {
            session.AddMessage("user", "Claim edit field selected.");
            var field = ParseClaimField(message);
            if (field == null)
            {
                return BuildClaimResponse(
                    "CLAIM_EDIT",
                    "Choose policy reference, incident date, incident description, or contact details.",
                    "اختر رقم الوثيقة أو تاريخ الحادث أو وصف الحادث أو معلومات التواصل.");
            }

            draft.EditingField = field;
            draft.Step = ClaimStep.EditValue;
            var prompt = GetClaimEditPrompt(field.Value);
            return BuildClaimResponse("CLAIM_EDIT_VALUE", prompt.English, prompt.Arabic);
        }

        if (draft.Step == ClaimStep.EditValue)
        {
            switch (draft.EditingField)
            {
                case ClaimField.PolicyReference:
                    draft.PolicyReference = message.Trim();
                    break;
                case ClaimField.IncidentDate:
                    draft.IncidentDate = message.Trim();
                    break;
                case ClaimField.IncidentDescription:
                    draft.IncidentDescription = message.Trim();
                    break;
                case ClaimField.Contact:
                    if (!LooksLikeContact(message))
                    {
                        session.AddMessage("user", "Claim contact update rejected.");
                        return BuildClaimResponse(
                            "CLAIM_EDIT_VALUE",
                            "Please provide a contact phone number or email address. Do not include payment information.",
                            "يرجى إدخال رقم هاتف أو بريد إلكتروني للتواصل، دون معلومات الدفع.");
                    }
                    draft.Contact = message.Trim();
                    break;
            }

            session.AddMessage("user", "Claim detail updated.");
            draft.EditingField = null;
            draft.Step = ClaimStep.Confirm;
            return BuildClaimReviewResponse(draft);
        }

        if (draft.Step == ClaimStep.Complete)
        {
            if (message.Contains("new claim", StringComparison.OrdinalIgnoreCase))
            {
                draft = new ClaimDraft();
                _claimDrafts[sessionId] = draft;
                session.AddMessage("user", "Started a new claim draft.");
                session.AddMessage("assistant", "Claim intake started.");
                return BuildClaimResponse("CLAIM_INTAKE", "Let's prepare another claim draft. What is your policy reference?", "لنُعدّ مسودة مطالبة أخرى. ما رقم وثيقتك؟");
            }
            else
            {
                session.AddMessage("user", "Claim draft follow-up received.");
                return BuildClaimResponse("CLAIM_COMPLETE", "This claim draft remains in this test session only and has not been submitted. Start a new chat to prepare another draft.", "تبقى مسودة المطالبة في جلسة الاختبار هذه ولم تُرسل. ابدأ محادثة جديدة لإعداد مسودة أخرى.");
            }
        }

        session.AddMessage("user", "Claim information provided.");
        switch (draft.Step)
        {
            case ClaimStep.PolicyReference:
                draft.PolicyReference = message.Trim();
                draft.Step = ClaimStep.IncidentDate;
                return BuildClaimResponse("CLAIM_INTAKE", "What date did the incident happen? Please use YYYY-MM-DD if possible.", "ما تاريخ وقوع الحادث؟ يرجى استخدام التنسيق YYYY-MM-DD إن أمكن.");

            case ClaimStep.IncidentDate:
                draft.IncidentDate = message.Trim();
                draft.Step = ClaimStep.IncidentDescription;
                return BuildClaimResponse("CLAIM_INTAKE", "Briefly describe what happened and the type of claim (for example, vehicle damage, theft, or travel interruption).", "يرجى وصف ما حدث ونوع المطالبة بإيجاز (مثل تلف المركبة أو السرقة أو تعطل السفر).");

            case ClaimStep.IncidentDescription:
                draft.IncidentDescription = message.Trim();
                draft.Step = ClaimStep.Contact;
                return BuildClaimResponse("CLAIM_INTAKE", "What phone number or email should Mutakamela use to contact you about this claim draft?", "ما رقم الهاتف أو البريد الإلكتروني الذي يمكن لمتّكاملة استخدامه للتواصل معك بشأن مسودة المطالبة؟");

            case ClaimStep.Contact:
                if (!LooksLikeContact(message))
                {
                    session.AddMessage("user", "A valid contact method was not provided.");
                    return BuildClaimResponse("CLAIM_INTAKE", "Please provide a contact phone number or email address. Do not include payment information.", "يرجى إدخال رقم هاتف أو بريد إلكتروني للتواصل، دون معلومات الدفع.");
                }

                draft.Contact = message.Trim();
                draft.Step = ClaimStep.Confirm;
                return BuildClaimReviewResponse(draft);

            case ClaimStep.Confirm:
                if (message.Contains("edit", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("change", StringComparison.OrdinalIgnoreCase))
                {
                    draft.Step = ClaimStep.EditField;
                    return BuildClaimResponse(
                        "CLAIM_EDIT",
                        "Which detail would you like to edit: policy reference, incident date, incident description, or contact details?",
                        "ما المعلومة التي ترغب في تعديلها: رقم الوثيقة أو تاريخ الحادث أو وصف الحادث أو معلومات التواصل؟");
                }

                if (message.Contains("confirm claim draft", StringComparison.OrdinalIgnoreCase) ||
                    message.Equals("yes", StringComparison.OrdinalIgnoreCase))
                {
                    draft.Step = ClaimStep.Complete;
                    return BuildClaimResponse("CLAIM_COMPLETE", "Your claim draft is ready in this test session. It has not been submitted and no claim has been opened. Contact Mutakamela to submit it through the official process.", "أصبحت مسودة المطالبة جاهزة في جلسة الاختبار هذه. لم تُرسل ولم تُفتح مطالبة. يرجى التواصل مع متكاملة لتقديمها عبر القنوات الرسمية.");
                }

                return BuildClaimResponse("CLAIM_CONFIRM", "Please choose Confirm claim draft or Edit details. Confirming only prepares this prototype draft; it does not submit a claim.", "يرجى اختيار تأكيد مسودة المطالبة أو تعديل التفاصيل. التأكيد يجهز المسودة فقط ولا يرسل المطالبة.");
        }

        return BuildClaimResponse("CLAIM_INTAKE", GetClaimStepPrompt(draft.Step), GetClaimStepPromptAr(draft.Step));
    }

    private static string GetClaimStepPrompt(ClaimStep step)
    {
        return step switch
        {
            ClaimStep.PolicyReference => "What is your policy reference?",
            ClaimStep.IncidentDate => "What date did the incident happen?",
            ClaimStep.IncidentDescription => "Briefly describe what happened and the type of claim.",
            ClaimStep.Contact => "What phone number or email should Mutakamela use to contact you?",
            ClaimStep.Confirm => "Please review and confirm or edit the claim draft.",
            _ => "How can I help with your claim draft?"
        };
    }

    private static string GetClaimStepPromptAr(ClaimStep step)
    {
        return step switch
        {
            ClaimStep.PolicyReference => "ما رقم وثيقتك؟",
            ClaimStep.IncidentDate => "ما تاريخ وقوع الحادث؟ يرجى استخدام التنسيق YYYY-MM-DD إن أمكن.",
            ClaimStep.IncidentDescription => "يرجى وصف ما حدث ونوع المطالبة بإيجاز.",
            ClaimStep.Contact => "ما رقم الهاتف أو البريد الإلكتروني للتواصل معك؟",
            ClaimStep.EditField => "ما المعلومة التي ترغب في تعديلها؟",
            ClaimStep.EditValue => "أدخل القيمة الجديدة.",
            ClaimStep.Confirm => "يرجى مراجعة مسودة المطالبة وتأكيدها أو تعديلها.",
            _ => "كيف يمكنني مساعدتك في مسودة المطالبة؟"
        };
    }

    private static ClaimField? ParseClaimField(string message)
    {
        var normalized = message.Trim().ToLowerInvariant();
        if (normalized.Contains("policy") || normalized.Contains("reference") || normalized.Contains("وثيقة"))
            return ClaimField.PolicyReference;
        if (normalized.Contains("date") || normalized.Contains("تاريخ"))
            return ClaimField.IncidentDate;
        if (normalized.Contains("description") || normalized.Contains("incident") || normalized.Contains("وصف") || normalized.Contains("الحادث"))
            return ClaimField.IncidentDescription;
        if (normalized.Contains("contact") || normalized.Contains("phone") || normalized.Contains("email") || normalized.Contains("تواصل"))
            return ClaimField.Contact;
        return null;
    }

    private static (string English, string Arabic) GetClaimEditPrompt(ClaimField field)
    {
        return field switch
        {
            ClaimField.PolicyReference => ("Enter the updated policy reference.", "أدخل رقم الوثيقة الجديد."),
            ClaimField.IncidentDate => ("Enter the updated incident date.", "أدخل تاريخ الحادث الجديد."),
            ClaimField.IncidentDescription => ("Enter the updated incident description.", "أدخل وصف الحادث الجديد."),
            _ => ("Enter the updated contact phone number or email address. Do not include payment information.", "أدخل رقم الهاتف أو البريد الإلكتروني الجديد للتواصل، دون معلومات الدفع.")
        };
    }

    private static AIPolicyResponse BuildClaimReviewResponse(ClaimDraft draft)
    {
        var summary = $"Claim draft summary:\nPolicy reference: {draft.PolicyReference}\nIncident date: {draft.IncidentDate}\nIncident: {draft.IncidentDescription}\nPreferred contact: {draft.Contact}\n\nThis is a prototype draft only. It has not been submitted and no claim has been opened.";
        var summaryAr = $"ملخص مسودة المطالبة:\nرقم الوثيقة: {draft.PolicyReference}\nتاريخ الحادث: {draft.IncidentDate}\nتفاصيل الحادث: {draft.IncidentDescription}\nوسيلة التواصل: {draft.Contact}\n\nهذه مسودة أولية فقط. لم تُرسل ولم تُفتح مطالبة.";
        return BuildClaimResponse("CLAIM_CONFIRM", summary, summaryAr);
    }

    private AIPolicyResponse AttachCatalogProduct(AIPolicyResponse response)
    {
        var selectedId = response.SelectedProduct?.Id;
        var product = !string.IsNullOrWhiteSpace(selectedId)
            ? _productCatalog.FirstOrDefault(item => item["id"]?.ToString() == selectedId)
            : null;
        product ??= FindProductInText(response.Response ?? string.Empty);
        if (product == null) return response;

        var name = product["name"]?.ToString() ?? string.Empty;
        var nameAr = product["name_ar"]?.ToString() ?? name;
        response.SelectedProduct = new SelectedProduct
        {
            Id = product["id"]?.ToString() ?? string.Empty,
            Name = name,
            NameAr = nameAr,
            Category = product["category"]?.ToString() ?? string.Empty
        };
        response.ProductDetails ??= new ProductDetails
        {
            Coverage = product["coverage"]?.Select(item => item.ToString()).Take(4).ToList() ?? new List<string>(),
            Features = product["features"]?.Select(item => item.ToString()).Take(3).ToList() ?? new List<string>()
        };
        response.ProductDetails.CoverageAr = product["coverage_ar"]?.Select(item => item.ToString()).Take(4).ToList() ?? new List<string>();
        if (string.Equals(response.Stage, "IDENTIFY", StringComparison.OrdinalIgnoreCase))
            response.Stage = "RECOMMEND";
        return response;
    }

    private static bool IsUnsupportedSalesResponse(string response)
    {
        var normalized = response.ToLowerInvariant();
        var asksForContactDetails =
            (normalized.Contains("name") && normalized.Contains("phone")) ||
            (normalized.Contains("phone") && normalized.Contains("email")) ||
            normalized.Contains("your email address") ||
            normalized.Contains("your phone number") ||
            normalized.Contains("policy number");

        var claimsUnsupportedAction =
            normalized.Contains("payment") ||
            normalized.Contains("purchase") ||
            normalized.Contains("finalize") ||
            normalized.Contains("policy is now active") ||
            normalized.Contains("policy is active") ||
            normalized.Contains("policy is confirmed") ||
            normalized.Contains("policy has been issued") ||
            normalized.Contains("quote has been") ||
            normalized.Contains("quote is ready") ||
            normalized.Contains("details are on file") ||
            normalized.Contains("is all set");

        return asksForContactDetails || claimsUnsupportedAction;
    }

    public async Task<AIPolicyResponse> ProcessMessageAsync(string sessionId, string message, string lang = "en")
    {
        var session = _sessionManager.GetOrCreateSession(sessionId);

        if (_claimDrafts.TryGetValue(sessionId, out var claimDraft) || IsClaimStartRequest(message))
            return ProcessClaimMessage(session, sessionId, message, claimDraft);

        if (IsUnsupportedSalesRequest(message))
        {
            session.AddMessage("user", "Customer requested quote or contact assistance.");
            var handoff = GetSalesHandoffResponse();
            session.AddMessage("assistant", handoff.Response);
            return handoff;
        }

        session.AddMessage("user", message);
        if (IsOtherOptionsRequest(message))
        {
            var options = BuildOtherOptionsResponse(session);
            session.AddMessage("assistant", options.Response);
            return options;
        }

        if (IsMoreDetailsRequest(message))
        {
            var requestedProduct = FindProductInText(message);
            if (requestedProduct != null)
                session.SelectedProductId = requestedProduct["id"]?.ToString();
            var details = BuildProductDetailsResponse(session);
            session.AddMessage("assistant", details.Response);
            return details;
        }

        var conversationHistory = session.GetConversationText();

        var languageInstruction = lang == "ar"
            ? "MANDATORY LANGUAGE: Write response entirely in Arabic and response_ar in English. Do not duplicate the same text across both fields."
            : "MANDATORY LANGUAGE: Write response entirely in English and response_ar in Arabic. Do not duplicate the same text across both fields.";
        var systemPrompt = $@"You are Mutakamela Insurance AI - friendly, helpful, and NEVER repetitive.

CONVERSATION RULES:
1. PROGRESS the conversation - don't repeat questions already answered
2. When customer says ""yes"" or confirms - GIVE the information, don't ask again
3. When customer first selects a product - SHOW full details immediately
4. Be concise - no long introductions, get to the point
5. After showing details, ask ""Ready to proceed?"" or ""Need anything else?""
6. This demo cannot create quotes, accept payments, or issue policies. Never ask for names, phone numbers, email addresses, policy numbers, or payment details.
7. If the customer wants to proceed with a quote or purchase, explain this limitation and direct them to mutakamela.sa. Never claim a quote or policy was submitted, confirmed, active, or purchased.

STAGES: IDENTIFY → RECOMMEND → DETAILS → CONFIRM → COMPLETE

{languageInstruction}
Always respond in JSON format.";

        var primaryResponseExample = lang == "ar" ? "الرد الطبيعي باللغة العربية" : "Your natural response in English";
        var secondaryResponseExample = lang == "ar" ? "English translation of the response" : "الرد بالعربية";
        var userPrompt = $@"PRODUCT CATALOG:
{_productsJson}

CONVERSATION SO FAR:
{conversationHistory}

REQUESTED RESPONSE LANGUAGE: {lang}

CURRENT MESSAGE: ""{message}""

Provide the NEXT appropriate response in JSON:
{{
    ""stage"": ""IDENTIFY|RECOMMEND|DETAILS|CONFIRM|COMPLETE"",
    ""intent"": ""BUY_POLICY|GET_QUOTE|GET_INFO|CLAIM"",
    ""detected_lob"": ""MOTOR|HEALTH|TRAVEL|PROPERTY|MARINE|LIABILITY|ENGINEERING"",
    ""confidence"": 0.95,
    ""selected_product"": {{""id"": """", ""name"": """", ""name_ar"": """", ""category"": """"}},
    ""product_details"": {{
        ""coverage"": [""list of coverage items""],
        ""features"": [""key features""],
        ""key_benefits"": [""benefit1"", ""benefit2""]
    }},
    ""response"": ""{primaryResponseExample}"",
    ""response_ar"": ""{secondaryResponseExample}"",
    ""next_action"": ""What happens next""
}}";

        try
        {
            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.4,
                response_format = new { type = "json_object" },
                max_tokens = 1200
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_apiUrl}/chat/completions", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var responseObj = JObject.Parse(responseJson);
            var aiContent = responseObj["choices"]?[0]?["message"]?["content"]?.ToString() ?? "";

            // Clean JSON if wrapped in markdown
            if (aiContent.Contains("```"))
            {
                aiContent = aiContent.Split("```")[1];
                if (aiContent.StartsWith("json"))
                    aiContent = aiContent.Substring(4);
                aiContent = aiContent.Trim();
            }

            var result = JsonConvert.DeserializeObject<AIPolicyResponse>(aiContent) ?? new AIPolicyResponse();
            var assistantResponse = result.Response ?? string.Empty;
            if (IsUnsupportedSalesResponse(assistantResponse))
            {
                result = GetSalesHandoffResponse();
                assistantResponse = result.Response;
            }
            else
            {
                result = AttachCatalogProduct(result);
                assistantResponse = result.Response ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(result.SelectedProduct?.Id))
                    session.SelectedProductId = result.SelectedProduct.Id;
                result.Response = assistantResponse;
            }

            // Store AI response in session
            session.AddMessage("assistant", assistantResponse);

            _logger.LogInformation("✅ AI processed message for session {SessionId}", sessionId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ AI processing failed for session {SessionId}", sessionId);
            return new AIPolicyResponse
            {
                Stage = "IDENTIFY",
                Response = "I'm sorry, I encountered an issue. How can I help you with insurance today?",
                ResponseAr = "أنا آسف، واجهت مشكلة. كيف يمكنني مساعدتك في التأمين اليوم؟"
            };
        }
    }

    public void ClearSession(string sessionId)
    {
        _claimDrafts.TryRemove(sessionId, out _);
        _sessionManager.ClearSession(sessionId);
        _logger.LogInformation("🗑️ Session cleared: {SessionId}", sessionId);
    }
}
