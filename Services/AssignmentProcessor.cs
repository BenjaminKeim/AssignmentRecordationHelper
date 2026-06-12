using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using AssignmentRecordationHelper.Models;
using UglyToad.PdfPig;

namespace AssignmentRecordationHelper.Services;

/// <summary>
/// Processes one assignment (a contiguous page range within a PDF) and returns
/// an AssignmentResult. Direct C# port of _process_assignment() in the Python prototype.
/// </summary>
public class AssignmentProcessor
{
    private readonly OcrService _ocr;
    private readonly IHandwritingService _hw;

    public AssignmentProcessor(OcrService ocr, IHandwritingService? hw = null)
    {
        _ocr = ocr;
        _hw  = hw ?? new StubHandwritingService();
    }

    public AssignmentResult Process(string pdfPath, int startPage, int endPage, int index)
    {
        var result = new AssignmentResult
        {
            Index = index,
            Pages = (startPage + 1, endPage + 1),
        };

        // â”€â”€ Page 1: ASSIGNOR name â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var (p1Text, _) = BestPageText(pdfPath, startPage);
        result.P1Name = ExtractP1Name(p1Text);

        // â”€â”€ Signature page (last page): printed name + date + signature â”€â”€â”€â”€â”€â”€â”€
        var (p2Text, p2Source) = BestPageText(pdfPath, endPage);
        result.P2PrintedName   = ExtractP2PrintedName(p2Text);
        result.SignatureDetected = PageHasSignature(pdfPath, endPage, p2Text);

        // â”€â”€ Date extraction â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        string rawDate   = ExtractDateFromText(p2Text);
        string dateSource = p2Source;

        if (string.IsNullOrEmpty(rawDate) && p2Source == "text")
        {
            // Digital page with no typed date â€” the date may be handwritten.
            // Run OCR as a first check before the handwriting path.
            string ocrText = _ocr.OcrPage(pdfPath, endPage);
            rawDate    = ExtractDateFromText(ocrText);
            dateSource = string.IsNullOrEmpty(rawDate) ? "missing" : "ocr";
        }
        else if (string.IsNullOrEmpty(rawDate))
        {
            dateSource = "missing";
        }

        // â”€â”€ Date-box path (handwritten / e-signature annotation dates) â”€â”€â”€â”€â”€â”€â”€â”€
        // The typed-text layer had no date, so the date lives in the signature
        // annotation. Crop the date box (now rendered with annotations), show it as
        // a thumbnail, and OCR just that line for a pre-fill hint. The result is a
        // suggestion the human verifies against the image â€” never auto-accepted.
        if (string.IsNullOrEmpty(rawDate))
        {
            var bbox = OcrService.FindDateLabelBbox(pdfPath, endPage);
            if (bbox.HasValue)
            {
                var (cropImg, cropOcr) = _ocr.ReadDateBox(pdfPath, endPage, bbox.Value);
                result.DateCropImage = cropImg;

                // Prefer Tesseract's single-line read of the crop; fall back to the
                // handwriting model (stub/ONNX) if Tesseract returns nothing.
                string hintText = cropOcr;
                if (string.IsNullOrEmpty(hintText) && cropImg != null)
                    hintText = _hw.TryReadDate(cropImg) ?? "";

                if (!string.IsNullOrEmpty(hintText))
                {
                    var (_, epas, _) = DateParser.RecoverOcrDate(hintText);
                    result.DateSuggestion = string.IsNullOrEmpty(epas) ? hintText : epas;
                }
                dateSource = "trocr";
            }
        }

        result.RawDate    = rawDate;
        result.DateSource = dateSource switch
        {
            "text"    => DateSource.Text,
            "ocr"     => DateSource.Ocr,
            "trocr"   => DateSource.TrOcr,
            _         => DateSource.Missing,
        };

        if (!string.IsNullOrEmpty(rawDate))
        {
            var (iso, epas, ambig) = DateParser.Parse(rawDate);
            result.IsoDate      = iso;
            result.EpasDate     = epas;
            result.DateAmbiguous = ambig;
        }

        // â”€â”€ Intra-document checks â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (!string.IsNullOrEmpty(result.P1Name) && !string.IsNullOrEmpty(result.P2PrintedName))
        {
            if (NameMatcher.NamesMatch(result.P1Name, result.P2PrintedName))
                result.Checks.Add(
                    $"Name consistent: '{result.P2PrintedName}' matches ASSIGNOR on p.{startPage + 1}");
            else
                result.Warnings.Add(
                    $"NAME MISMATCH: p.{startPage + 1} ASSIGNOR = '{result.P1Name}' " +
                    $"but p.{endPage + 1} printed name = '{result.P2PrintedName}'");
        }
        else if (string.IsNullOrEmpty(result.P1Name))
            result.Warnings.Add($"Could not extract ASSIGNOR name from p.{startPage + 1}");
        else
            result.Warnings.Add($"Could not extract printed name from p.{endPage + 1}");

        if (result.SignatureDetected)
            result.Checks.Add("Signature detected on signature page");
        else
            result.Warnings.Add("Signature NOT detected â€” verify manually");

        switch (result.DateSource)
        {
            case DateSource.TrOcr:
                string hint = !string.IsNullOrEmpty(result.DateSuggestion)
                    ? result.DateSuggestion : "(no read)";
                result.Warnings.Add(
                    $"HANDWRITTEN date â€” verify against image. OCR hint: '{hint}'");
                break;
            case DateSource.Missing:
                result.Warnings.Add("Execution date not found â€” verify manually");
                break;
            default:
                if (string.IsNullOrEmpty(result.IsoDate))
                    result.Warnings.Add(
                        $"Could not parse date from '{rawDate}' â€” verify manually");
                else if (result.DateAmbiguous)
                    result.Warnings.Add(
                        $"Date '{rawDate}' is possibly written in day-month (European) order â€” " +
                        $"interpreted as {result.EpasDate}; confirm against the document");
                else
                    result.Checks.Add($"Date '{rawDate}' parsed as {result.EpasDate}");
                break;
        }

        return result;
    }

    // â”€â”€ Text extraction helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private (string Text, string Source) BestPageText(string pdfPath, int pageIndex)
    {
        try
        {
            using var doc = PdfDocument.Open(pdfPath);
            var pages = doc.GetPages().ToList();
            if (pageIndex >= pages.Count) return ("", "text");
            var page = pages[pageIndex];
            string text = PageTextFromWords(page);
            if (text.Trim().Length >= 20) return (text, "text");
        }
        catch { }

        string ocrText = _ocr.OcrPage(pdfPath, pageIndex);
        return (ocrText, "ocr");
    }

    private static string PageTextFromWords(UglyToad.PdfPig.Content.Page page)
        => PdfTextUtil.GetText(page);

    // â”€â”€ Name extraction â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static readonly Regex P1NameRe = new(
        @"(?:^|\n)\s*(?:[I1|!l]\s+)?" +
        @"([A-ZÃ€-Ã–Ã˜-Ãž][A-Za-zÃ€-Ã¿\-â€™']+" +
        @"(?:\s+[A-ZÃ€-Ã–Ã˜-Ãž][A-Za-zÃ€-Ã¿\-â€™']+){0,5}?)" +
        @"\s*[\(\[]?\s*[â€œâ€""]?\s*ASSIGNOR",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static string ExtractP1Name(string text)
    {
        var m = P1NameRe.Match(text);
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    private static readonly string[] BoilerplateKeywords =
    [
        "ASSIGNOR", "Inventor", "Signature", "Date", "Page",
        "Native Language", "APPLICATION", "ASSIGNEE", "hereby", "following",
        "Microsoft", "good and valuable", "assigns", "transfers",
    ];

    private static string ExtractP2PrintedName(string text)
    {
        var lines = text.Split('\n')
                        .Select(l => l.Trim())
                        .Where(l => l.Length > 0)
                        .ToList();

        for (int i = 0; i < lines.Count; i++)
        {
            if (!lines[i].Contains("Printed Name in English")) continue;
            var candidates = new List<string>();
            for (int j = i - 1; j >= Math.Max(0, i - 2); j--)
            {
                if (BoilerplateKeywords.Any(kw => lines[j].Contains(kw))) break;
                if (!string.IsNullOrEmpty(lines[j])) candidates.Insert(0, lines[j]);
            }
            if (candidates.Count > 0) return string.Join(" ", candidates);
        }
        return "";
    }

    // â”€â”€ Date extraction â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static readonly string[] DateExcludeKeywords =
        ["Page", "Application", "ASSIGNOR", "ASSIGNEE", "Microsoft",
         "hereby", "filing", "filed"];

    private static readonly Regex DateCandidateRe = new(
        @"\d{1,4}[\s./\-]+(?:\d{1,2}|[A-Za-z]{3,})[.,\s\-/]+\d{1,4}",
        RegexOptions.Compiled);

    private static string ExtractDateFromText(string text)
    {
        var lines = text.Split('\n')
                        .Select(l => l.Trim())
                        .Where(l => l.Length > 0)
                        .ToList();

        // Pass 1: line immediately before "Date" label
        for (int i = 0; i < lines.Count; i++)
        {
            if (Regex.IsMatch(lines[i], @"^Date\s*$", RegexOptions.IgnoreCase) && i > 0)
            {
                var (iso, _, _) = DateParser.Parse(lines[i - 1]);
                if (!string.IsNullOrEmpty(iso)) return lines[i - 1];
            }
        }

        // Pass 2: any line that parses cleanly
        foreach (var line in lines)
        {
            if (DateExcludeKeywords.Any(kw => line.Contains(kw))) continue;
            var m = DateCandidateRe.Match(line);
            if (m.Success)
            {
                var (iso, _, _) = DateParser.Parse(m.Value);
                if (!string.IsNullOrEmpty(iso)) return m.Value;
            }
            var (iso2, _, _) = DateParser.Parse(line);
            if (!string.IsNullOrEmpty(iso2)) return line;
        }
        return "";
    }

    // â”€â”€ Signature detection â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static bool PageHasSignature(string pdfPath, int pageIndex, string pageText)
    {
        try
        {
            using var doc = PdfDocument.Open(pdfPath);
            var pages = doc.GetPages().ToList();
            if (pageIndex >= pages.Count) return false;
            if (pages[pageIndex].GetImages().Any()) return true;
        }
        catch { }

        // Proxy: printed name present indicates the inventor physically signed
        return !string.IsNullOrEmpty(ExtractP2PrintedName(pageText));
    }
}
