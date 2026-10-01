using System.Globalization;
using System.Text.RegularExpressions;
using MutakamelaAPI.Browser;
using MutakamelaAPI.Models;
using Newtonsoft.Json.Linq;

namespace MutakamelaAPI.Rules;

public interface IRulesEngine
{
    /// <summary>Validate and normalise one value. Returns null when valid, otherwise the problem.</summary>
    FieldProblem? Validate(FieldSpec field, string value, out string normalized);

    /// <summary>All problems across a job's data for the given flow, plus the fields still missing.</summary>
    (List<FieldProblem> Problems, List<string> Missing) ValidateAll(FlowSpec flow, IReadOnlyDictionary<string, string> data);

    bool IsRequired(FieldSpec field, IReadOnlyDictionary<string, string> data);

    /// <summary>True when the text contains something that must never be stored (card numbers, CVV...).</summary>
    bool ContainsForbiddenContent(string text);
}

/// <summary>
/// Data-driven validation. Rules are loaded from Rules/motor.json so product and
/// compliance teams can adjust limits without a code change. The LLM is never
/// consulted here: everything that gates a portal write is deterministic.
/// </summary>
public class RulesEngine : IRulesEngine
{
    private readonly JObject _rules;
    private readonly List<Regex> _forbidden = new();
    private readonly ILogger<RulesEngine> _logger;

    public RulesEngine(ILogger<RulesEngine> logger)
    {
        _logger = logger;
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "motor.json");
        try
        {
            var root = JObject.Parse(File.ReadAllText(path));
            _rules = root["rules"] as JObject ?? new JObject();
            if (root["forbidden_patterns"] is JObject forbidden)
            {
                foreach (var prop in forbidden.Properties().Where(p => !p.Name.StartsWith('_')))
                    _forbidden.Add(new Regex(prop.Value.ToString(), RegexOptions.Compiled));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load rules from {Path}; all fields will be treated as free text.", path);
            _rules = new JObject();
        }
    }

    public bool ContainsForbiddenContent(string text) =>
        _forbidden.Any(rx => rx.IsMatch(text ?? string.Empty));

    public bool IsRequired(FieldSpec field, IReadOnlyDictionary<string, string> data)
    {
        if (field.RequiredWhen is { Count: > 0 })
        {
            return field.RequiredWhen.Any(condition =>
                data.TryGetValue(condition.Key, out var current) &&
                condition.Value.Contains(current, StringComparer.OrdinalIgnoreCase));
        }
        return field.Required;
    }

    public (List<FieldProblem> Problems, List<string> Missing) ValidateAll(FlowSpec flow, IReadOnlyDictionary<string, string> data)
    {
        var problems = new List<FieldProblem>();
        var missing = new List<string>();
        foreach (var field in flow.AllFields)
        {
            var hasValue = data.TryGetValue(field.Id, out var value) && !string.IsNullOrWhiteSpace(value);
            if (!hasValue)
            {
                if (IsRequired(field, data) && field.Type != "otp" && field.Type != "file" && field.Source != "portal")
                    missing.Add(field.Id);
                continue;
            }
            var problem = Validate(field, value!, out _);
            if (problem != null) problems.Add(problem);
        }
        return (problems, missing);
    }

    public FieldProblem? Validate(FieldSpec field, string value, out string normalized)
    {
        normalized = (value ?? string.Empty).Trim();

        if (ContainsForbiddenContent(normalized))
            return Problem(field, "Card, CVV or password details are never accepted here.", "لا تُقبل بيانات البطاقة أو رمز الأمان أو كلمات المرور هنا.");

        if (string.IsNullOrEmpty(field.Rule) || _rules[field.Rule] is not JObject rule)
            return null;

        var kind = rule["kind"]?.ToString() ?? "regex";
        var message = rule["message"]?.ToString() ?? "Invalid value.";
        var messageAr = rule["message_ar"]?.ToString() ?? "قيمة غير صالحة.";

        switch (kind)
        {
            case "regex":
            {
                if (rule["normalize"]?.ToString() == "digits")
                {
                    normalized = Regex.Replace(normalized, @"[^\d+]", string.Empty);
                    if (normalized.StartsWith("+966")) normalized = "0" + normalized[4..];
                    else if (normalized.StartsWith("966")) normalized = "0" + normalized[3..];
                    else if (normalized.Length == 9 && normalized.StartsWith('5')) normalized = "0" + normalized;
                }
                var ok = Regex.IsMatch(normalized, rule["pattern"]?.ToString() ?? ".*");
                return ok ? null : Problem(field, message, messageAr);
            }
            case "length":
            {
                var min = rule["min"]?.Value<int>() ?? 0;
                var max = rule["max"]?.Value<int>() ?? int.MaxValue;
                return normalized.Length >= min && normalized.Length <= max ? null : Problem(field, message, messageAr);
            }
            case "enum":
            {
                var values = rule["values"]?.Select(v => v.ToString()).ToList() ?? new List<string>();
                var lowered = normalized.ToLowerInvariant();
                var match = values.FirstOrDefault(v => v.Equals(lowered, StringComparison.OrdinalIgnoreCase)
                                                      || v.Replace('_', ' ').Equals(lowered, StringComparison.OrdinalIgnoreCase));
                if (match == null && rule["aliases"] is JObject aliases)
                {
                    foreach (var alias in aliases.Properties())
                    {
                        if (alias.Value.Any(a => lowered.Contains(a.ToString().ToLowerInvariant())))
                        {
                            match = alias.Name;
                            break;
                        }
                    }
                }
                if (match == null) return Problem(field, message, messageAr);
                normalized = match;
                return null;
            }
            case "number_range":
            {
                var digits = Regex.Replace(normalized, @"[^\d.]", string.Empty);
                if (!decimal.TryParse(digits, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
                    return Problem(field, message, messageAr);
                var min = rule["min"]?.Value<decimal>() ?? decimal.MinValue;
                var max = rule["max"]?.Value<decimal>() ?? decimal.MaxValue;
                normalized = number.ToString("0.##", CultureInfo.InvariantCulture);
                return number >= min && number <= max ? null : Problem(field, message, messageAr);
            }
            case "year_range":
            {
                if (!int.TryParse(normalized, out var year)) return Problem(field, message, messageAr);
                var now = DateTime.UtcNow.Year;
                var min = now + (rule["min_offset"]?.Value<int>() ?? -30);
                var max = now + (rule["max_offset"]?.Value<int>() ?? 1);
                return year >= min && year <= max ? null : Problem(field, message, messageAr);
            }
            case "date_window":
            {
                if (!TryParseGregorian(normalized, out var date)) return Problem(field, message, messageAr);
                var today = DateTime.UtcNow.Date;
                var min = today.AddDays(rule["min_days"]?.Value<int>() ?? int.MinValue / 2);
                var max = today.AddDays(rule["max_days"]?.Value<int>() ?? int.MaxValue / 2);
                normalized = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return date >= min && date <= max ? null : Problem(field, message, messageAr);
            }
            case "date_of_birth":
            {
                if (!TryParseDateOfBirth(normalized, out var dob, out var calendar))
                    return Problem(field, message, messageAr);
                var age = (int)((DateTime.UtcNow.Date - dob).TotalDays / 365.25);
                var minAge = rule["min_age"]?.Value<int>() ?? 18;
                var maxAge = rule["max_age"]?.Value<int>() ?? 100;
                normalized = $"{dob:yyyy-MM-dd}|{calendar}";
                return age >= minAge && age <= maxAge ? null : Problem(field, message, messageAr);
            }
            default:
                return null;
        }
    }

    private static FieldProblem Problem(FieldSpec field, string message, string messageAr) =>
        new() { Field = field.Id, Message = message, MessageAr = messageAr };

    private static bool TryParseGregorian(string text, out DateTime date)
    {
        var formats = new[] { "yyyy-MM-dd", "yyyy/MM/dd", "dd-MM-yyyy", "dd/MM/yyyy", "d-M-yyyy", "d/M/yyyy" };
        return DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date);
    }

    /// <summary>
    /// Accepts Hijri (year 1300–1500) or Gregorian dates. Returns the Gregorian equivalent
    /// and which calendar the customer used so the portal field can be filled correctly.
    /// </summary>
    private static bool TryParseDateOfBirth(string text, out DateTime date, out string calendar)
    {
        calendar = "gregorian";
        var match = Regex.Match(text, @"^(\d{4})[-/](\d{1,2})[-/](\d{1,2})$");
        if (!match.Success) { date = default; return false; }
        var year = int.Parse(match.Groups[1].Value);
        var month = int.Parse(match.Groups[2].Value);
        var day = int.Parse(match.Groups[3].Value);

        if (year is >= 1300 and <= 1500)
        {
            try
            {
                date = new UmAlQuraCalendar().ToDateTime(year, month, day, 0, 0, 0, 0);
                calendar = "hijri";
                return true;
            }
            catch (ArgumentOutOfRangeException) { date = default; return false; }
        }

        return DateTime.TryParseExact($"{year:D4}-{month:D2}-{day:D2}", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date);
    }
}
