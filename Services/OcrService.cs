using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;
using Tesseract;
using UglyToad.PdfPig;

namespace AssignmentRecordationPrep.Services;

/// <summary>
/// Handles page rendering (Docnet) and Tesseract OCR.
/// Tessdata is extracted from the embedded resource on first use.
/// </summary>
public class OcrService : IDisposable
{
    // Docnet scale: 1.0 = 1 point per pixel (72 DPI base); 4.0 ≈ 288 DPI
    private const double OcrScale = 3.0;
    private const double CropScale = 4.0;

    private readonly string _tessDataDir;
    // Hold the singleton so we never call Dispose() on it mid-session.
    // DocLib.Instance is a process-level singleton; disposing it tears down
    // PDFium and forces a re-init on the next call, which is both slow and
    // risks instability. We keep our own reference and dispose it once on shutdown.
    private readonly Docnet.Core.IDocLib _docLib = DocLib.Instance;

    public OcrService()
    {
        _tessDataDir = GetOrExtractTessData();
    }

    public void Dispose() { /* intentionally not disposing the DocLib singleton */ }

    private static string GetOrExtractTessData()
    {
        var dir  = Path.Combine(Path.GetTempPath(), "AssignmentRecordationPrep", "tessdata");
        var dest = Path.Combine(dir, "eng.traineddata");
        if (!File.Exists(dest))
        {
            Directory.CreateDirectory(dir);
            using var stream = typeof(OcrService).Assembly
                .GetManifestResourceStream("tessdata.eng.traineddata");
            if (stream != null)
            {
                using var fs = File.Create(dest);
                stream.CopyTo(fs);
            }
        }
        return dir;
    }

    /// <summary>Render a page at OcrScale and run Tesseract. Returns extracted text.</summary>
    public string OcrPage(string pdfPath, int pageIndex)
    {
        try
        {
            var (bgra, w, h) = RenderPageBgra(pdfPath, pageIndex, OcrScale);
            if (bgra == null) return "";
            return RunTesseract(bgra, w, h);
        }
        catch { return ""; }
    }

    /// <summary>
    /// Renders the signature page, crops the date box above the "Date" label,
    /// and returns a WPF BitmapSource for display. Returns null if not found.
    ///
    /// labelBbox is (x0, plumberTop, x1, plumberBottom) in PDF points,
    /// where plumberTop is measured from the TOP of the page (pdfplumber convention).
    /// </summary>
    public BitmapSource? CropDateBox(string pdfPath, int pageIndex,
        (double X0, double PlumberTop, double X1, double PlumberBottom) labelBbox)
        => ReadDateBox(pdfPath, pageIndex, labelBbox).Image;

    /// <summary>
    /// Renders the date box (annotations included), returns both a display thumbnail
    /// and a Tesseract OCR read of just that region. OCR'ing the isolated single line
    /// reads clean typed dates (in the e-signature annotation layer) far more reliably
    /// than a full-page scan, and gives a usable hint for neat handwriting.
    /// </summary>
    public (BitmapSource? Image, string OcrText) ReadDateBox(string pdfPath, int pageIndex,
        (double X0, double PlumberTop, double X1, double PlumberBottom) labelBbox)
    {
        try
        {
            var (bgra, w, h) = RenderPageBgra(pdfPath, pageIndex, CropScale);
            if (bgra == null) return (null, "");

            // Crop: band from 50 pts above the Date label down to 4 pts above it
            // (the bottom edge sits at the signature underline). The earlier 7 pt
            // gap cut the bottoms off taller handwritten dates; extending to 4 pt
            // captures the full writing without grabbing the "Date" label below it.
            double sc = CropScale;
            int cropX = (int)Math.Max(0, (labelBbox.X0 - 8.0) * sc);
            int cropW = (int)Math.Min(w - cropX, 225.0 * sc);
            int cropY = (int)Math.Max(0, (labelBbox.PlumberTop - 50.0) * sc);
            int cropH = (int)Math.Min(h - cropY, (50.0 - 4.0) * sc);
            if (cropW <= 0 || cropH <= 0) return (null, "");

            // Extract the crop region.
            byte[] cropBgra = new byte[cropW * cropH * 4];
            for (int row = 0; row < cropH; row++)
                Buffer.BlockCopy(bgra, ((cropY + row) * w + cropX) * 4,
                                 cropBgra, row * cropW * 4, cropW * 4);

            // Trim the surrounding whitespace down to the ink bounding box, so the
            // thumbnail is mostly the date text (enlarging it enlarges the writing,
            // not the empty margins). Falls back to the full crop if it's blank.
            var (tBgra, tW, tH) = TrimToInk(cropBgra, cropW, cropH);

            var image = CropBgraToWpfBitmap(tBgra, tW, tH, 0, 0, tW, tH);
            string ocr = RunTesseract(tBgra, tW, tH, PageSegMode.SingleLine).Trim();

            return (image, ocr);
        }
        catch { return (null, ""); }
    }

    /// <summary>
    /// Crops a BGRA buffer to the bounding box of its non-white (ink) pixels, with a
    /// small margin. Returns the original buffer unchanged if no ink is found.
    /// </summary>
    private static (byte[] Bgra, int W, int H) TrimToInk(byte[] bgra, int w, int h)
    {
        const int inkThreshold = 185;   // avg brightness below this counts as ink
        int minX = w, minY = h, maxX = -1, maxY = -1;

        for (int y = 0; y < h; y++)
        {
            int rowOff = y * w * 4;

            // Count ink in the row; skip "line" rows. The signature underline spans
            // nearly the full width — including it would prevent horizontal trimming
            // and leave the date floating in whitespace.
            int rowInk = 0;
            for (int x = 0; x < w; x++)
            {
                int o = rowOff + x * 4;
                if ((bgra[o] + bgra[o + 1] + bgra[o + 2]) / 3 < inkThreshold) rowInk++;
            }
            if (rowInk >= w * 0.55) continue;

            for (int x = 0; x < w; x++)
            {
                int o = rowOff + x * 4;
                if ((bgra[o] + bgra[o + 1] + bgra[o + 2]) / 3 < inkThreshold)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (maxX < 0) return (bgra, w, h);   // all white — nothing to trim

        int pad = (int)(2.0 * CropScale);    // ~8 px breathing room at 4x
        minX = Math.Max(0, minX - pad); minY = Math.Max(0, minY - pad);
        maxX = Math.Min(w - 1, maxX + pad); maxY = Math.Min(h - 1, maxY + pad);

        int nw = maxX - minX + 1, nh = maxY - minY + 1;
        byte[] outBgra = new byte[nw * nh * 4];
        for (int y = 0; y < nh; y++)
            Buffer.BlockCopy(bgra, ((minY + y) * w + minX) * 4, outBgra, y * nw * 4, nw * 4);
        return (outBgra, nw, nh);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private (byte[]? Bgra, int W, int H) RenderPageBgra(
        string pdfPath, int pageIndex, double scale)
    {
        try
        {
            using var reader = _docLib.GetDocReader(pdfPath, new PageDimensions(scale));
            if (pageIndex >= reader.GetPageCount()) return (null, 0, 0);
            using var pageReader = reader.GetPageReader(pageIndex);
            int w = pageReader.GetPageWidth();
            int h = pageReader.GetPageHeight();
            // RenderAnnotations is essential: ink/e-signatures and many handwritten
            // dates on these assignments are annotation appearances, not page content.
            // Without this flag PDFium renders the signature/date area blank, so neither
            // OCR nor the date-crop thumbnail would see the handwritten date.
            byte[] bgra = pageReader.GetImage(RenderFlags.RenderAnnotations);
            // PDFium renders with a transparent background (BGRA 0,0,0,0). Composite
            // it over white so OCR sees dark ink on white (not on black) and the
            // whitespace-trim can tell background from ink.
            CompositeOverWhite(bgra);
            return (bgra, w, h);
        }
        catch { return (null, 0, 0); }
    }

    private static void CompositeOverWhite(byte[] bgra)
    {
        for (int o = 0; o < bgra.Length; o += 4)
        {
            int a = bgra[o + 3];
            if (a == 255) continue;
            double af = a / 255.0, inv = (1 - af) * 255;
            bgra[o]     = (byte)(bgra[o]     * af + inv);
            bgra[o + 1] = (byte)(bgra[o + 1] * af + inv);
            bgra[o + 2] = (byte)(bgra[o + 2] * af + inv);
            bgra[o + 3] = 255;
        }
    }

    private string RunTesseract(byte[] bgra, int w, int h,
        PageSegMode psm = PageSegMode.SingleBlock)
    {
        // Convert BGRA to PNG bytes for Tesseract
        byte[] pngBytes;
        using (var bmp = new System.Drawing.Bitmap(w, h,
                   System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            var bmpData = bmp.LockBits(
                new System.Drawing.Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Marshal.Copy(bgra, 0, bmpData.Scan0, bgra.Length);
            bmp.UnlockBits(bmpData);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            pngBytes = ms.ToArray();
        }

        if (!File.Exists(Path.Combine(_tessDataDir, "eng.traineddata")))
            return "";

        using var engine = new TesseractEngine(_tessDataDir, "eng", EngineMode.Default);
        using var pix    = Pix.LoadFromMemory(pngBytes);
        using var page   = engine.Process(pix, psm);
        return page.GetText() ?? "";
    }

    private static BitmapSource? CropBgraToWpfBitmap(
        byte[] bgra, int srcW, int srcH,
        int cropX, int cropY, int cropW, int cropH)
    {
        // Clamp crop to source bounds
        cropX = Math.Max(0, cropX); cropY = Math.Max(0, cropY);
        cropW = Math.Min(cropW, srcW - cropX);
        cropH = Math.Min(cropH, srcH - cropY);
        if (cropW <= 0 || cropH <= 0) return null;

        byte[] cropBgra = new byte[cropW * cropH * 4];
        for (int row = 0; row < cropH; row++)
        {
            int srcOffset  = ((cropY + row) * srcW + cropX) * 4;
            int destOffset = row * cropW * 4;
            Buffer.BlockCopy(bgra, srcOffset, cropBgra, destOffset, cropW * 4);
        }

        var bmp = BitmapSource.Create(cropW, cropH, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null, cropBgra, cropW * 4);
        bmp.Freeze(); // Required: BitmapSource created on background thread must be frozen before WPF binding
        return bmp;
    }

    /// <summary>
    /// Finds the bounding box of the "Date" label on a signature page.
    /// Returns (x0, plumberTop, x1, plumberBottom) in PDF points, or null if not found.
    /// plumberTop is measured from the TOP of the page.
    /// </summary>
    public static (double X0, double PlumberTop, double X1, double PlumberBottom)?
        FindDateLabelBbox(string pdfPath, int pageIndex)
    {
        try
        {
            using var doc   = PdfDocument.Open(pdfPath);
            var pages       = doc.GetPages().ToList();
            if (pageIndex >= pages.Count) return null;
            var page        = pages[pageIndex];
            double pageH    = page.Height;

            foreach (var word in page.GetWords())
            {
                if (word.Text.Trim() == "Date")
                {
                    var bb = word.BoundingBox;
                    return (bb.Left,
                            pageH - bb.Top,     // convert PDF y-from-bottom to y-from-top
                            bb.Right,
                            pageH - bb.Bottom);
                }
            }
        }
        catch { }
        return null;
    }
}
