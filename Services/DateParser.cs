using System.Text.RegularExpressions;

namespace AssignmentRecordationPrep.Services;

/// <summary>
/// Direct C# port of the Python assignment_recordation_prep.py date parsing logic.
/// parse_date() handles all date formats seen in assignment signature pages.
/// RecoverOcrDate() coerces garbled TrOCR output using slash-fix and Levenshtein fuzzy matching.
/// </summary>
public static class DateParser
{
    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jan"] = 1, ["feb"] = 2, ["mar"] = 3, ["apr"] = 4,
        ["may"] = 5, ["jun"] = 6, ["jul"] = 7, ["aug"] = 8,
        ["sep"] = 9, ["oct"] = 10, ["nov"] = 11, ["dec"] = 12,
    };

    private static int? MonthNum(string token) =>
        token.Length >= 3 && Months.TryGetValue(token[..3], out int m) ? m : null;

    // (isoDate, epasDate, ambiguous)
    public static (string Iso, string Epas, bool Ambiguous) Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ("", "", false);
        string s = raw.Trim().TrimEnd('.');

        // Spelled-out month: "20. Oct. 2025", "15th October, 2025"
        var m1 = Regex.Match(s,
            @"(\d{1,2})(?:st|nd|rd|th)?[\s.,]+([A-Za-z]+)[\s.,]+(\d{4})");
        if (m1.Success)
        {
            int day = int.Parse(m1.Groups[1].Value);
            int? mon = MonthNum(m1.Groups[2].Value);
            int year = int.Parse(m1.Groups[3].Value);
            if (mon.HasValue && day >= 1 && day <= 31 && year >= 2020 && year <= 2099)
                return (Iso(year, mon.Value, day), Epas(mon.Value, day, year), false);
        }

        // YMD with spelled month: "2025 Oct 5"
        var m2 = Regex.Match(s, @"(\d{4})[\s.]+([A-Za-z]+)[\s.]+(\d{1,2})");
        if (m2.Success)
        {
            int year = int.Parse(m2.Groups[1].Value);
            int? mon = MonthNum(m2.Groups[2].Value);
            int day = int.Parse(m2.Groups[3].Value);
            if (mon.HasValue && day >= 1 && day <= 31 && year >= 2020 && year <= 2099)
                return (Iso(year, mon.Value, day), Epas(mon.Value, day, year), false);
        }

        // Pure spelled month last: "03 October 2025"
        var m3 = Regex.Match(s,
            @"(\d{1,2})(?:st|nd|rd|th)?\s+([A-Za-z]+),?\s+(\d{4})");
        if (m3.Success)
        {
            int day = int.Parse(m3.Groups[1].Value);
            int? mon = MonthNum(m3.Groups[2].Value);
            int year = int.Parse(m3.Groups[3].Value);
            if (mon.HasValue && day >= 1 && day <= 31 && year >= 2020 && year <= 2099)
                return (Iso(year, mon.Value, day), Epas(mon.Value, day, year), false);
        }

        // Numeric: DD/MM/YYYY or MM/DD/YYYY
        var m4 = Regex.Match(s, @"(\d{1,2})[/.\-](\d{1,2})[/.\-](\d{4})");
        if (m4.Success)
        {
            int a = int.Parse(m4.Groups[1].Value);
            int b = int.Parse(m4.Groups[2].Value);
            int year = int.Parse(m4.Groups[3].Value);
            if (year >= 2020 && year <= 2099)
            {
                if (a > 12)   return (Iso(year, b, a), Epas(b, a, year), false);
                if (b > 12)   return (Iso(year, a, b), Epas(a, b, year), false);
                // Both ≤ 12: ambiguous — assume DD/MM (European convention)
                return (Iso(year, b, a), Epas(b, a, year), true);
            }
        }

        // Already ISO: YYYY-MM-DD
        var m5 = Regex.Match(s.Trim(), @"^(\d{4})-(\d{2})-(\d{2})$");
        if (m5.Success)
        {
            string y = m5.Groups[1].Value, mo = m5.Groups[2].Value, d = m5.Groups[3].Value;
            return ($"{y}-{mo}-{d}", $"{mo}/{d}/{y}", false);
        }

        return ("", "", false);
    }

    // ── OCR date recovery ─────────────────────────────────────────────────────

    private static readonly Regex SlashLikeRe = new(@"(?<=\d)[1lI|!/\\](?=\d)");

    public static (string Iso, string Epas, bool Ambiguous) RecoverOcrDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ("", "", false);

        // 1. Try as-is
        var (iso, epas, ambig) = Parse(raw);
        if (!string.IsNullOrEmpty(epas)) return (iso, epas, ambig);

        // 2. Replace slash-like chars between digits
        string coerced = SlashLikeRe.Replace(raw, "/");
        if (coerced != raw)
        {
            (iso, epas, ambig) = Parse(coerced);
            if (!string.IsNullOrEmpty(epas)) return (iso, epas, ambig);
        }

        // 3. Fixed-width digit splits
        string digits = Regex.Replace(raw, @"\D", "");
        if (digits.Length == 10)
        {
            // Contiguous digit indices — separators were already stripped by Regex.Replace.
            string cand = $"{digits[0..2]}/{digits[2..4]}/{digits[4..8]}";
            (iso, epas, ambig) = Parse(cand);
            if (!string.IsNullOrEmpty(epas)) return (iso, epas, ambig);
        }
        if (digits.Length == 8)
        {
            foreach (string cand in new[]
            {
                $"{digits[0..2]}/{digits[2..4]}/{digits[4..8]}",
                $"{digits[2..4]}/{digits[0..2]}/{digits[4..8]}",
            })
            {
                (iso, epas, ambig) = Parse(cand);
                if (!string.IsNullOrEmpty(epas)) return (iso, epas, ambig);
            }
        }

        // 4. Fuzzy-match month tokens
        string coercedMonths = CoerceMonthTokens(raw);
        if (coercedMonths != raw)
        {
            (iso, epas, ambig) = Parse(coercedMonths);
            if (!string.IsNullOrEmpty(epas)) return (iso, epas, ambig);
            string cm2 = SlashLikeRe.Replace(coercedMonths, "/");
            if (cm2 != coercedMonths)
            {
                (iso, epas, ambig) = Parse(cm2);
                if (!string.IsNullOrEmpty(epas)) return (iso, epas, ambig);
            }
        }

        return ("", "", false);
    }

    // ── Levenshtein + month coercion ──────────────────────────────────────────

    private static int Levenshtein(string a, string b)
    {
        int m = a.Length, n = b.Length;
        int[] dp = Enumerable.Range(0, n + 1).ToArray();
        for (int i = 1; i <= m; i++)
        {
            int prev = dp[0]; dp[0] = i;
            for (int j = 1; j <= n; j++)
            {
                int temp = dp[j];
                dp[j] = a[i - 1] == b[j - 1] ? prev : 1 + Math.Min(prev, Math.Min(dp[j], dp[j - 1]));
                prev = temp;
            }
        }
        return dp[n];
    }

    private static readonly Dictionary<string, string> MonthCanon =
        new Dictionary<string, string[]>[]
        {
            new() { ["Jan"] = ["january", "jan"] },
            new() { ["Feb"] = ["february", "feb"] },
            new() { ["Mar"] = ["march", "mar"] },
            new() { ["Apr"] = ["april", "apr"] },
            new() { ["May"] = ["may"] },
            new() { ["Jun"] = ["june", "jun"] },
            new() { ["Jul"] = ["july", "jul"] },
            new() { ["Aug"] = ["august", "aug"] },
            new() { ["Sep"] = ["september", "sep"] },
            new() { ["Oct"] = ["october", "oct"] },
            new() { ["Nov"] = ["november", "nov"] },
            new() { ["Dec"] = ["december", "dec"] },
        }
        .SelectMany(d => d)
        .SelectMany(kv => kv.Value.Select(name => (name, kv.Key)))
        .ToDictionary(t => t.name, t => t.Key, StringComparer.OrdinalIgnoreCase);

    private static string CoerceMonthTokens(string raw)
    {
        var parts = Regex.Split(raw, @"(\s+|[-/,.])");
        var out_ = new List<string>();
        foreach (var part in parts)
        {
            string alpha = Regex.Replace(part, @"[^a-zA-Z]", "");
            if (alpha.Length < 3) { out_.Add(part); continue; }
            int threshold = alpha.Length <= 4 ? 1 : 2;
            string lower = part.ToLowerInvariant();
            int bestDist = threshold + 1;
            string? bestCanon = null;
            foreach (var (candidate, canon) in MonthCanon)
            {
                int d = Levenshtein(lower, candidate);
                if (d < bestDist) { bestDist = d; bestCanon = canon; }
            }
            out_.Add(bestCanon ?? part);
        }
        return string.Concat(out_);
    }

    // ── Format helpers ────────────────────────────────────────────────────────

    private static string Iso(int y, int m, int d)  => $"{y:D4}-{m:D2}-{d:D2}";
    private static string Epas(int m, int d, int y) => $"{m:D2}/{d:D2}/{y:D4}";
}
