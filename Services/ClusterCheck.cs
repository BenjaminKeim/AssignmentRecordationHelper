using AssignmentRecordationPrep.Models;

namespace AssignmentRecordationPrep.Services;

public static class ClusterCheck
{
    /// <summary>
    /// Flags assignments whose date is more than windowDays from the median of
    /// confidently-typed dates. Returns (median, lo, hi), or nulls if &lt; 3 confident dates.
    /// </summary>
    public static (DateOnly? Median, DateOnly? Lo, DateOnly? Hi) Apply(
        IList<AssignmentResult> results, int windowDays = 30)
    {
        var confident = results
            .Where(r => (r.DateSource == DateSource.Text || r.DateSource == DateSource.Ocr)
                        && !string.IsNullOrEmpty(r.IsoDate))
            .Select(r => DateOnly.ParseExact(r.IsoDate, "yyyy-MM-dd", null))
            .OrderBy(d => d)
            .ToList();

        if (confident.Count < 3) return (null, null, null);

        var median = confident[confident.Count / 2];
        var lo     = confident[0];
        var hi     = confident[^1];

        foreach (var r in results)
        {
            DateOnly? cand = null;
            if (!string.IsNullOrEmpty(r.IsoDate) &&
                DateOnly.TryParseExact(r.IsoDate, "yyyy-MM-dd", null,
                    System.Globalization.DateTimeStyles.None, out var d))
                cand = d;

            if (cand == null && !string.IsNullOrEmpty(r.DateSuggestion))
            {
                var (iso, _, _) = DateParser.Parse(r.DateSuggestion);
                if (!string.IsNullOrEmpty(iso) &&
                    DateOnly.TryParseExact(iso, "yyyy-MM-dd", null,
                        System.Globalization.DateTimeStyles.None, out var ds))
                    cand = ds;
            }

            if (cand == null) continue;

            int diff = Math.Abs((cand.Value.ToDateTime(TimeOnly.MinValue) -
                                 median.ToDateTime(TimeOnly.MinValue)).Days);
            if (diff > windowDays)
            {
                r.ClusterOutlier = true;
                r.Warnings.Add(
                    $"CLUSTER OUTLIER: {cand:MM/dd/yyyy} is {diff} days from the others " +
                    $"(cohort {lo:MM/dd/yyyy}–{hi:MM/dd/yyyy}, median {median:MM/dd/yyyy}) " +
                    "— likely wrong; verify / re-read");
            }
        }

        return (median, lo, hi);
    }
}
