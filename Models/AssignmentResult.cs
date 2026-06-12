using System.Windows.Media.Imaging;

namespace AssignmentRecordationPrep.Models;

public enum DateSource { Text, Ocr, TrOcr, Missing }

public class AssignmentResult
{
    public int Index { get; init; }
    public (int Start, int End) Pages { get; init; }   // 1-based

    public string P1Name            { get; set; } = "";
    public string P2PrintedName     { get; set; } = "";

    public string RawDate           { get; set; } = "";
    public string IsoDate           { get; set; } = "";   // YYYY-MM-DD
    public string EpasDate          { get; set; } = "";   // MM/DD/YYYY
    public DateSource DateSource    { get; set; } = DateSource.Missing;
    public bool DateAmbiguous       { get; set; }

    /// <summary>Raw text from handwriting OCR — never auto-accepted as the EPAS value.</summary>
    public string DateSuggestion    { get; set; } = "";

    /// <summary>Cropped date-box image for human verification. Null for typed dates.</summary>
    public BitmapSource? DateCropImage { get; set; }

    public bool ClusterOutlier      { get; set; }
    public bool SignatureDetected   { get; set; }

    public List<string> Checks      { get; } = new();
    public List<string> Warnings    { get; } = new();

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(P2PrintedName) ? P2PrintedName : P1Name;

    public string CommentTags()
    {
        var tags = new List<string>();
        if (!string.IsNullOrEmpty(P1Name) && !string.IsNullOrEmpty(P2PrintedName)
            && !Services.NameMatcher.NamesMatch(P1Name, P2PrintedName))
            tags.Add("NAME MISMATCH");
        if (!SignatureDetected)
            tags.Add("no signature");
        if (DateSource == DateSource.TrOcr)
            tags.Add("handwritten — verify image");
        else if (DateAmbiguous)
            tags.Add("Possibly written in day-month order");
        else if (string.IsNullOrEmpty(EpasDate))
            tags.Add("date missing");
        return string.Join("; ", tags);
    }
}
