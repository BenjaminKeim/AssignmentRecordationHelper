using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AssignmentRecordationPrep.Services;

public static class NameMatcher
{
    /// <summary>Lowercase, strip diacritics, collapse whitespace.</summary>
    public static string Normalize(string s)
    {
        s = s.Normalize(NormalizationForm.FormD);
        s = new string(s.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return Regex.Replace(s, @"\s+", " ").Trim().ToLowerInvariant();
    }

    public static bool NamesMatch(string a, string b) =>
        Normalize(a) == Normalize(b);

    /// <summary>
    /// Returns the matching ADS name string if the assignment name matches any ADS inventor,
    /// or null if not. Matches on surname + at least one first-name token prefix.
    /// </summary>
    public static string? MatchToAds(string assignmentName, IEnumerable<Models.AdsInventor> ads)
    {
        string normA = Normalize(assignmentName);
        foreach (var inv in ads)
        {
            string normFull = Normalize(inv.FullName);
            string normLast = Normalize(inv.Last);
            string normFirst = Normalize(inv.First);
            if (string.IsNullOrEmpty(normLast)) continue;
            if (!normA.Contains(normLast)) continue;
            // First-name prefix match (at least 3 chars)
            if (normFirst.Length >= 3 && normA.Contains(normFirst[..3])) return inv.FullName;
            if (!string.IsNullOrEmpty(normFull) && normA.Contains(normFull)) return inv.FullName;
            // Surname-only match (last resort)
            return inv.FullName;
        }
        return null;
    }
}
