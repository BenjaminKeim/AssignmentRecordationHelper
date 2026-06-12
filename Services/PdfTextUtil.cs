using UglyToad.PdfPig.Content;

namespace AssignmentRecordationPrep.Services;

/// <summary>
/// Layout-aware text reconstruction from a PdfPig page.
///
/// Clusters words into lines by their vertical center FIRST, then orders each line
/// left-to-right. This matches how pdfplumber's extract_text() lays out text — and
/// crucially avoids the scrambling that a global (Top, Left) sort produces when words
/// on the same visual line have slightly different baselines.
/// </summary>
public static class PdfTextUtil
{
    private const double LineTolerance = 4.0;

    private static double Center(Word w) => (w.BoundingBox.Top + w.BoundingBox.Bottom) / 2.0;

    public static List<string> GetLines(Page page)
    {
        var words = page.GetWords()
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .ToList();
        if (words.Count == 0) return new List<string>();

        // Sorted top-to-bottom, words on the same visual line are adjacent.
        var ordered = words.OrderByDescending(Center).ToList();
        var lines   = new List<List<Word>>();
        var cur     = new List<Word>();
        double prevY = double.NaN;

        foreach (var w in ordered)
        {
            double cy = Center(w);
            if (cur.Count == 0 || prevY - cy <= LineTolerance) cur.Add(w);
            else { lines.Add(cur); cur = new List<Word> { w }; }
            prevY = cy;
        }
        if (cur.Count > 0) lines.Add(cur);

        return lines
            .Select(l => string.Join(" ", l.OrderBy(x => x.BoundingBox.Left).Select(x => x.Text)))
            .ToList();
    }

    public static string GetText(Page page) => string.Join("\n", GetLines(page));
}
