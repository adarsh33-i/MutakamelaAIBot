using Microsoft.Extensions.Logging.Abstractions;
using MutakamelaAPI.Browser;
using MutakamelaAPI.Rules;
using Xunit;

namespace MutakamelaAPI.Tests;

public class RulesEngineTests
{
    private readonly RulesEngine _rules = new(NullLogger<RulesEngine>.Instance);

    private static FieldSpec Field(string rule) => new() { Id = rule, Rule = rule };

    [Theory]
    [InlineData("national_id", "1098765432", true)]
    [InlineData("national_id", "2098765432", true)]
    [InlineData("national_id", "3098765432", false)]
    [InlineData("national_id", "109876543", false)]
    [InlineData("email", "m@example.com", true)]
    [InlineData("email", "not-an-email", false)]
    [InlineData("claim_number", "CLM-2024-00123", true)]
    [InlineData("claim_number", "CL", false)]
    [InlineData("plate_number", "ABC 1234", true)]
    [InlineData("plate_number", "أ ب ج 1234", true)]
    [InlineData("otp", "1234", true)]
    [InlineData("otp", "12", false)]
    public void Regex_rules(string rule, string value, bool valid)
    {
        var problem = _rules.Validate(Field(rule), value, out _);
        Assert.Equal(valid, problem == null);
    }

    [Theory]
    [InlineData("0551234567", "0551234567")]
    [InlineData("+966551234567", "0551234567")]
    [InlineData("966 55 123 4567", "0551234567")]
    [InlineData("551234567", "0551234567")]
    public void Saudi_mobile_is_normalised(string input, string expected)
    {
        Assert.Null(_rules.Validate(Field("saudi_mobile"), input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("third party", "third_party")]
    [InlineData("ضد الغير", "third_party")]
    [InlineData("comprehensive", "comprehensive")]
    [InlineData("شامل", "comprehensive")]
    public void Enum_aliases_map_to_canonical_values(string input, string expected)
    {
        Assert.Null(_rules.Validate(Field("cover_type"), input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void Hijri_date_of_birth_is_converted_to_gregorian()
    {
        Assert.Null(_rules.Validate(Field("date_of_birth"), "1410-05-20", out var normalized));
        Assert.Equal("1989-12-18|hijri", normalized);
    }

    [Fact]
    public void Underage_date_of_birth_is_rejected()
    {
        var tooYoung = DateTime.UtcNow.AddYears(-10).ToString("yyyy-MM-dd");
        Assert.NotNull(_rules.Validate(Field("date_of_birth"), tooYoung, out _));
    }

    [Fact]
    public void Policy_start_date_must_be_within_90_days()
    {
        Assert.Null(_rules.Validate(Field("policy_start_date"), DateTime.UtcNow.AddDays(10).ToString("yyyy-MM-dd"), out _));
        Assert.NotNull(_rules.Validate(Field("policy_start_date"), DateTime.UtcNow.AddDays(120).ToString("yyyy-MM-dd"), out _));
        Assert.NotNull(_rules.Validate(Field("policy_start_date"), DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd"), out _));
    }

    [Fact]
    public void Vehicle_value_accepts_thousands_separators_and_enforces_range()
    {
        Assert.Null(_rules.Validate(Field("vehicle_value"), "85,000", out var normalized));
        Assert.Equal("85000", normalized);
        Assert.NotNull(_rules.Validate(Field("vehicle_value"), "100", out _));
    }

    [Theory]
    [InlineData("my card number is 4111 1111 1111 1111")]
    [InlineData("cvv 123")]
    [InlineData("the password is hunter2")]
    public void Forbidden_content_is_detected(string text)
    {
        Assert.True(_rules.ContainsForbiddenContent(text));
        Assert.NotNull(_rules.Validate(Field("free_text"), text, out _));
    }

    [Fact]
    public void Conditional_requirement_follows_the_controlling_field()
    {
        var field = new FieldSpec { Id = "vehicle_value", Required = false, RequiredWhen = new() { ["cover_type"] = new[] { "comprehensive" } } };
        Assert.False(_rules.IsRequired(field, new Dictionary<string, string> { ["cover_type"] = "third_party" }));
        Assert.True(_rules.IsRequired(field, new Dictionary<string, string> { ["cover_type"] = "comprehensive" }));
    }
}
