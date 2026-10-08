using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MutakamelaAPI.Retrieval;

/// <summary>
/// Bilingual tokenisation for retrieval. English is lower-cased and lightly
/// stemmed; Arabic has diacritics stripped and common letter variants unified
/// (أإآ→ا, ة→ه, ى→ي) and the definite article removed, so "السيارة", "سيارتي"
/// and "سياره" all land on the same token.
/// </summary>
public static class TextNormalizer
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "i","me","my","we","our","you","your","a","an","the","and","or","of","to","in","on","for","with","is","am","are",
        "was","were","be","been","it","this","that","there","got","get","have","has","had","do","does","did","need","want",
        "please","can","could","would","should","about","some","any","at","by","from","as","into","if","so","just",
        "في","من","على","الى","إلى","عن","مع","هذا","هذه","ذلك","انا","أنا","احتاج","أحتاج","ابغى","أبغى","ابي","أبي",
        "اريد","أريد","لو","سمحت","ممكن","هل","ما","لا","او","أو","و","ثم","كان","كانت","يكون","عندي","لي","لدي"
    };

    private static readonly Regex Diacritics = new("[ً-ْٰـ]", RegexOptions.Compiled);
    private static readonly Regex Token = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled);

    public static IEnumerable<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var normalized = NormalizeArabic(text.ToLowerInvariant().Normalize(NormalizationForm.FormKC));
        foreach (Match m in Token.Matches(normalized))
        {
            var token = m.Value;
            if (token.Length < 2 || StopWords.Contains(token)) continue;
            yield return Stem(token);
        }
    }

    public static string NormalizeArabic(string s)
    {
        s = Diacritics.Replace(s, string.Empty);
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            sb.Append(ch switch
            {
                'أ' or 'إ' or 'آ' => 'ا',
                'ة' => 'ه',
                'ى' => 'ي',
                'ؤ' => 'و',
                'ئ' => 'ي',
                _ => ch
            });
        }
        return sb.ToString();
    }

    private static string Stem(string token)
    {
        if (IsArabic(token))
        {
            // Strip conjunction/preposition prefixes (و، ف، ب، ل، ك) and the article (ال),
            // in that order, so "ولتعليم" → "تعليم" and "بالسيارة" → "سيار".
            foreach (var prefix in new[] { "و", "ف" })
                if (token.Length > 4 && token.StartsWith(prefix)) { token = token[1..]; break; }
            if (token.Length > 4 && token.StartsWith("ال")) token = token[2..];
            else if (token.Length > 4 && (token.StartsWith("ب") || token.StartsWith("ل") || token.StartsWith("ك")))
            {
                var stripped = token[1..];
                if (stripped.StartsWith("ال") && stripped.Length > 4) stripped = stripped[2..];
                token = stripped;
            }
            foreach (var suffix in new[] { "تي", "ها", "هم", "كم", "ات", "ون", "ين", "ه", "ي" })
                if (token.Length > 3 + suffix.Length && token.EndsWith(suffix)) { token = token[..^suffix.Length]; break; }
            return token;
        }
        // Minimal English stemming: plurals, -ing, -ed.
        if (token.Length > 5 && token.EndsWith("ing")) return token[..^3];
        if (token.Length > 4 && token.EndsWith("ed")) return token[..^2];
        if (token.Length > 4 && token.EndsWith("ies")) return token[..^3] + "y";
        if (token.Length > 3 && token.EndsWith("es") && !token.EndsWith("ses")) return token[..^2];
        if (token.Length > 3 && token.EndsWith("s") && !token.EndsWith("ss")) return token[..^1];
        return token;
    }

    public static bool IsArabic(string s) => s.Any(c => c >= '؀' && c <= 'ۿ');
}
