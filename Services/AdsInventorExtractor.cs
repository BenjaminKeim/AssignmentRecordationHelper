using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AssignmentRecordationPrep.Models;
using UglyToad.PdfPig;

namespace AssignmentRecordationPrep.Services;

public static class AdsInventorExtractor
{
    public static bool IsXfa(string path)
    {
        try { return PdfXfaExtractor.HasXfa(File.ReadAllBytes(path)); }
        catch { return false; }
    }

    public static List<AdsInventor> Extract(string path)
    {
        byte[] fileBytes;
        try { fileBytes = File.ReadAllBytes(path); }
        catch { return new List<AdsInventor>(); }

        if (PdfXfaExtractor.HasXfa(fileBytes))
        {
            var xml = PdfXfaExtractor.ExtractDatasetsXml(fileBytes);
            if (xml != null)
            {
                var inventors = ParseXfa(xml);
                if (inventors.Count > 0) return inventors;
            }
        }

        // Fallback: text-based extraction for non-XFA ADS
        return ExtractFromText(path);
    }

    private static List<AdsInventor> ParseXfa(string xml)
    {
        var result = new List<AdsInventor>();
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var doc = XDocument.Parse(xml);
            foreach (var block in doc.Descendants()
                         .Where(e => e.Name.LocalName == "sfApplicantInformation"))
            {
                // Handle two XFA schema variants:
                //   Newer (rev 10-18+): sfApplicantName/firstName, middleName, lastName
                //   Older: sfNameFirst, sfNameMiddle, sfNameLast directly in the block
                var nameEl = block.Descendants()
                                  .FirstOrDefault(e => e.Name.LocalName == "sfApplicantName")
                             ?? block;

                string first  = Txt(nameEl, "firstName")  ?? Txt(block, "sfNameFirst")  ?? "";
                string middle = Txt(nameEl, "middleName") ?? Txt(block, "sfNameMiddle") ?? "";
                string last   = Txt(nameEl, "lastName")   ?? Txt(block, "sfNameLast")   ?? "";

                if (string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(last))
                    continue;

                string canonical = string.Join(" ",
                    new[] { first, middle, last }
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .Select(p => p.Trim().ToUpperInvariant()));
                if (!seen.Add(canonical)) continue;

                result.Add(new AdsInventor { First = first, Middle = middle, Last = last });
            }
        }
        catch { }

        return result;
    }

    private static List<AdsInventor> ExtractFromText(string path)
    {
        var result = new List<AdsInventor>();
        try
        {
            using var doc = PdfDocument.Open(path);
            string text = string.Join("\n",
                doc.GetPages().Select(PdfTextUtil.GetText));

            // Pattern: LAST, First [Middle]  e.g. "EHLERT, Sebastian"
            foreach (Match m in Regex.Matches(text,
                @"^([A-Z][A-Z\-]+),\s+([A-Z][a-zA-Z]+(?:\s+[A-Z][a-zA-Z]+)?)",
                RegexOptions.Multiline))
            {
                var firstParts = m.Groups[2].Value.Split(' ');
                result.Add(new AdsInventor
                {
                    Last   = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(m.Groups[1].Value.ToLowerInvariant()),
                    First  = firstParts[0],
                    Middle = firstParts.Length > 1 ? string.Join(" ", firstParts.Skip(1)) : "",
                });
            }
        }
        catch { }
        return result;
    }

    private static string? Txt(XElement? parent, string localName)
    {
        if (parent == null) return null;
        var el = parent.Descendants().FirstOrDefault(e => e.Name.LocalName == localName);
        var val = el?.Value?.Trim();
        return string.IsNullOrEmpty(val) ? null : val;
    }
}
