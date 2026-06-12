using System.IO;
using UglyToad.PdfPig;

namespace AssignmentRecordationHelper.Services;

/// <summary>
/// Classifies a path as "ads", "assignments", or "unknown".
/// A directory is always "assignments" (folder of individual PDFs).
/// Checks filename patterns first (fast), then first-two-pages content.
/// </summary>
public static class PdfClassifier
{
    private static readonly string[] AdsNameHints =
        ["ads", "application data", "aia-14", "aia14", "pto-aia", "pto_aia",
         "datasheet", "data_sheet", "data sheet"];

    private static readonly string[] AssignNameHints =
        ["assignment", "assign", "executed", "exec", "convey"];

    private static readonly string[] AdsContentSignals =
        ["application data sheet", "applicant information",
         "inventor or joint inventor", "assignee information",
         "domestic benefit", "earliest publication"];

    private static readonly string[] AssignContentSignals =
        ["assignor", "page 1 of", "assignment of invention",
         "assigns and transfers", "reel/frame"];

    public static string Classify(string path)
    {
        if (Directory.Exists(path)) return "assignments";

        string name = Path.GetFileName(path).ToLowerInvariant();

        foreach (var hint in AdsNameHints)
            if (name.Contains(hint)) return "ads";
        foreach (var hint in AssignNameHints)
            if (name.Contains(hint)) return "assignments";

        try
        {
            using var doc = PdfDocument.Open(path);
            var pages = doc.GetPages().Take(2).ToList();
            // Layout-aware reconstruction so multi-word signals ("page 1 of",
            // "assigns and transfers") read in order rather than scrambled.
            string text = string.Join("\n", pages.Select(PdfTextUtil.GetText))
                                .ToLowerInvariant();

            int adsScore    = AdsContentSignals.Count(s => text.Contains(s));
            int assignScore = AssignContentSignals.Count(s => text.Contains(s));

            if (adsScore > assignScore)    return "ads";
            if (assignScore > adsScore)    return "assignments";
        }
        catch { }

        return "unknown";
    }
}
