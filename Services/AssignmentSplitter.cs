using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace AssignmentRecordationHelper.Services;

/// <summary>
/// Splits a combined assignment PDF into per-inventor page ranges by detecting
/// "Page 1 of N" / "Page N of N" markers. Falls back to 2-pages-each.
/// </summary>
public static class AssignmentSplitter
{
    private static readonly Regex Page1OfN = new(@"Page\s+1\s+of\s+(\d+)", RegexOptions.IgnoreCase);

    public static List<(int Start, int End)> Detect(string pdfPath, OcrService? ocr = null)
    {
        List<string> pageTexts;
        int nPages;

        try
        {
            using var doc = PdfDocument.Open(pdfPath);
            var pages = doc.GetPages().ToList();
            nPages = pages.Count;
            // Use layout-aware reconstruction so "Page 1 of N" markers read in the
            // right order (a raw word join scrambles them, e.g. "of 2 1 Page").
            pageTexts = pages.Select(PdfTextUtil.GetText).ToList();
        }
        catch { return new List<(int, int)>(); }

        // Fall back to OCR for image-only pages
        if (ocr != null)
        {
            for (int i = 0; i < pageTexts.Count; i++)
            {
                if (pageTexts[i].Trim().Length < 20)
                    pageTexts[i] = ocr.OcrPage(pdfPath, i);
            }
        }

        var starts = new List<(int PageIdx, int N)>();
        for (int i = 0; i < pageTexts.Count; i++)
        {
            var m = Page1OfN.Match(pageTexts[i]);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int n))
                starts.Add((i, n));
        }

        if (starts.Count > 0)
        {
            var ranges = new List<(int Start, int End)>();
            for (int k = 0; k < starts.Count; k++)
            {
                var (s, n) = starts[k];
                int preferredEnd = s + n - 1;
                int nextStart = k + 1 < starts.Count ? starts[k + 1].PageIdx : nPages;
                int end = Math.Min(preferredEnd, nextStart - 1);
                ranges.Add((s, end));
            }
            return ranges;
        }

        // Fallback: 2 pages each
        var fallback = new List<(int Start, int End)>();
        for (int i = 0; i + 1 < nPages; i += 2)
            fallback.Add((i, i + 1));
        return fallback;
    }
}
