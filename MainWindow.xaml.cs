using System.Collections.ObjectModel;
using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AssignmentRecordationPrep.Models;
using AssignmentRecordationPrep.Services;
using Microsoft.Win32;

namespace AssignmentRecordationPrep;

// ── View model for one assignment row ────────────────────────────────────────

public enum CommentSeverity { None, Verify, Critical }

public class AssignmentRowVm : INotifyPropertyChanged
{
    private string _signatureDate = "";

    public int     Index         { get; init; }
    public string  Name          { get; init; } = "";
    public bool    IsHint        { get; init; }   // true → show date greyed/italic
    public string  DateToolTip   { get; init; } = "";
    public BitmapSource? DateImage { get; init; }
    public bool    HasDateImage  => DateImage != null;
    public string  Comments      { get; init; } = "";
    public CommentSeverity Severity { get; init; }

    public string SignatureDate
    {
        get => _signatureDate;
        set { _signatureDate = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

// ── WPF value converters ──────────────────────────────────────────────────────

public class SeverityToBrushConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object v, Type t, object p, System.Globalization.CultureInfo c)
        => v is CommentSeverity s ? s switch
        {
            CommentSeverity.Critical => new SolidColorBrush(Color.FromRgb(0xF8, 0xD7, 0xDA)),
            CommentSeverity.Verify   => new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xCD)),
            _                        => Brushes.Transparent,
        } : Brushes.Transparent;

    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
        => throw new NotSupportedException();
}

public class InvertBoolToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object v, Type t, object p, System.Globalization.CultureInfo c)
        => v is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
        => throw new NotSupportedException();
}

public class BoolToFontStyleConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object v, Type t, object p, System.Globalization.CultureInfo c)
        => v is bool b && b ? FontStyles.Italic : FontStyles.Normal;

    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
        => throw new NotSupportedException();
}

public class BoolToHintBrushConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object v, Type t, object p, System.Globalization.CultureInfo c)
        => v is bool b && b
            ? new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90))
            : Brushes.Black;

    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
        => throw new NotSupportedException();
}

public class MismatchTextConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object v, Type t, object p, System.Globalization.CultureInfo c)
        => v is string s && s.StartsWith("✓")
            ? new SolidColorBrush(Color.FromRgb(0x28, 0x7A, 0x3B))
            : new SolidColorBrush(Color.FromRgb(0xBB, 0x50, 0x00));

    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
        => throw new NotSupportedException();
}

// ── Main window ───────────────────────────────────────────────────────────────

public partial class MainWindow : Window
{
    // One ADS, and one-or-more assignment inputs (a combined PDF, a folder, or
    // several individual per-inventor PDFs).
    private string? _adsPath;
    private readonly List<string> _assignmentInputs = new();

    private readonly ObservableCollection<AssignmentRowVm> _rows = new();

    public MainWindow()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = _rows;
    }

    // ── Slot click pickers ─────────────────────────────────────────────────────

    private void AdsSlot_Click(object sender, MouseButtonEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Select the ADS (PTO/AIA/14) PDF",
            Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) == true)
            IngestPaths(new[] { dlg.FileName }, forceRole: "ads");
    }

    private void AssignSlot_Click(object sender, MouseButtonEventArgs e)
    {
        // Allow selecting one or many assignment PDFs at once.
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title      = "Select the executed assignment(s) — one combined PDF or several PDFs " +
                         "(cancel to pick a folder instead)",
            Filter     = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) == true)
        {
            IngestPaths(dlg.FileNames, forceRole: "assignments");
            return;
        }

        // Folder fallback via the OpenFileDialog "select folder" trick.
        var fDlg = new Microsoft.Win32.OpenFileDialog
        {
            Title           = "Select a FOLDER of assignment PDFs — open the folder and click Open",
            ValidateNames   = false,
            CheckFileExists = false,
            CheckPathExists = true,
            FileName        = "Select this folder",
        };
        if (fDlg.ShowDialog(this) == true)
        {
            string? dir = System.IO.Path.GetDirectoryName(fDlg.FileName);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                IngestPaths(new[] { dir }, forceRole: "assignments");
        }
    }

    // ── Whole-window drag & drop ────────────────────────────────────────────────

    private void Window_DragEnter(object sender, DragEventArgs e) => Window_DragOver(sender, e);

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        bool ok = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
        => DropOverlay.Visibility = Visibility.Collapsed;

    private void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            IngestPaths(paths);
        e.Handled = true;
    }

    // ── Ingest + auto-classify ──────────────────────────────────────────────────

    /// <summary>
    /// Classifies each dropped/selected path and routes it to the ADS slot or the
    /// assignments list. <paramref name="forceRole"/> overrides classification when
    /// the user explicitly used a role-specific picker.
    /// </summary>
    private void IngestPaths(IEnumerable<string> paths, string? forceRole = null)
    {
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            string role = forceRole ?? PdfClassifier.Classify(path);

            if (role == "ads")
            {
                _adsPath = path;
            }
            else if (role == "assignments")
            {
                if (!_assignmentInputs.Contains(path, StringComparer.OrdinalIgnoreCase))
                    _assignmentInputs.Add(path);
            }
            else
            {
                // Unknown: if we still need an ADS, treat as ADS; otherwise assignments.
                if (_adsPath == null) _adsPath = path;
                else if (!_assignmentInputs.Contains(path, StringComparer.OrdinalIgnoreCase))
                    _assignmentInputs.Add(path);
            }
        }

        UpdateSlotLabels();
    }

    private void UpdateSlotLabels()
    {
        // Assignments slot
        if (_assignmentInputs.Count == 1)
            AssignLabel.Text = ShortName(_assignmentInputs[0]);
        else if (_assignmentInputs.Count > 1)
            AssignLabel.Text = $"{_assignmentInputs.Count} files selected:\n" +
                               string.Join(", ", _assignmentInputs.Select(p => Path.GetFileName(p)));
        if (_assignmentInputs.Count > 0)
            AssignBorder.Style = (Style)Resources["PickerBoxSelected"];

        // ADS slot
        if (_adsPath != null)
        {
            AdsLabel.Text = ShortName(_adsPath);
            AdsBorder.Style = (Style)Resources["PickerBoxSelected"];
        }

        // Status + enablement
        bool ready = _adsPath != null && _assignmentInputs.Count > 0;
        ProcessButton.IsEnabled = ready;
        if (ready)
            StatusText.Text = $"Ready: assignments = {(_assignmentInputs.Count == 1 ? ShortName(_assignmentInputs[0]) : _assignmentInputs.Count + " files")}, " +
                              $"ADS = {ShortName(_adsPath!)}.  Click Process.";
        else if (_adsPath == null && _assignmentInputs.Count > 0)
            StatusText.Text = "Now add the ADS (drag it on, or click the ADS box).";
        else if (_adsPath != null && _assignmentInputs.Count == 0)
            StatusText.Text = "Now add the assignment(s) (drag them on, or click the Assignments box).";
        else
            StatusText.Text = "";
    }

    private static string ShortName(string path) =>
        Directory.Exists(path) ? Path.GetFileName(path.TrimEnd('\\', '/')) + "\\" : Path.GetFileName(path);

    // ── Processing ────────────────────────────────────────────────────────────

    private async void Process_Click(object sender, RoutedEventArgs e)
    {
        ProcessButton.IsEnabled = false;
        ResultsPanel.Visibility = Visibility.Collapsed;
        StatusText.Text = "Processing…";
        _rows.Clear();

        try
        {
            var inputs = _assignmentInputs.ToList();
            string ads = _adsPath!;
            var (rows, adsMismatches) = await Task.Run(() => RunProcessing(inputs, ads));

            foreach (var r in rows) _rows.Add(r);
            ShowAdsMismatches(adsMismatches);
            ResultsPanel.Visibility = Visibility.Visible;
            StatusText.Text = $"Done — {rows.Count} inventor{(rows.Count == 1 ? "" : "s")} processed.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            MessageBox.Show(ex.ToString(), "Processing error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ProcessButton.IsEnabled = true;
        }
    }

    private static (List<AssignmentRowVm> Rows, List<string> AdsMismatches)
        RunProcessing(List<string> assignmentInputs, string adsPath)
    {
        var ocr       = new OcrService();
        var processor = new AssignmentProcessor(ocr);

        // Expand each input: a folder → its PDFs; a file → itself. Dedupe case-insensitively.
        var pdfPaths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in assignmentInputs)
        {
            var expanded = Directory.Exists(input) ? CollectPdfs(input) : new List<string> { input };
            foreach (var p in expanded)
                if (seen.Add(p.ToLowerInvariant())) pdfPaths.Add(p);
        }

        var results = new List<AssignmentResult>();
        int idx = 1;

        foreach (var pdfPath in pdfPaths)
        {
            var ranges = AssignmentSplitter.Detect(pdfPath, ocr);
            foreach (var (start, end) in ranges)
            {
                var r = processor.Process(pdfPath, start, end, idx++);
                results.Add(r);
            }
        }

        // Cluster check
        ClusterCheck.Apply(results);

        // ADS comparison
        var ads = AdsInventorExtractor.Extract(adsPath);

        // Build mismatch list
        var unmatchedAssignments = new List<string>();
        var unmatchedAds         = ads.ToList();

        foreach (var r in results)
        {
            string name  = r.DisplayName;
            string? match = NameMatcher.MatchToAds(name, ads);
            if (match == null)
                unmatchedAssignments.Add(name);
            else
                unmatchedAds.RemoveAll(i =>
                    NameMatcher.NamesMatch(i.FullName, match));
        }

        var mismatches = new List<string>();
        foreach (var name in unmatchedAssignments)
            mismatches.Add($"⚠  '{name}' — in assignment but not on ADS");
        foreach (var inv in unmatchedAds)
            mismatches.Add($"⚠  '{inv.FullName}' — on ADS but no matching assignment");
        if (mismatches.Count == 0)
            mismatches.Add("✓  All inventors match between ADS and assignments.");

        // Convert results to view models
        var rows = results.Select(r =>
        {
            bool isHint = r.DateSource == DateSource.TrOcr
                          && !string.IsNullOrEmpty(r.DateSuggestion);

            string dateValue = !string.IsNullOrEmpty(r.EpasDate)
                ? r.EpasDate
                : (isHint ? r.DateSuggestion : "");

            string tooltip = r.DateSource switch
            {
                DateSource.TrOcr   => "Handwriting OCR hint — verify against the image before using",
                DateSource.Missing => "Date not found — enter manually",
                DateSource.Ocr     => "Date read via OCR",
                _                  => "Typed date extracted from PDF",
            };
            if (r.DateAmbiguous)
                tooltip += " — possibly written in day-month (European) order; " +
                           $"interpreted as {r.EpasDate}. Confirm against the document.";

            CommentSeverity severity = CommentSeverity.None;
            string comments = r.CommentTags();
            if (comments.Contains("NAME MISMATCH") || comments.Contains("no signature"))
                severity = CommentSeverity.Critical;
            else if (comments.Length > 0)
                severity = CommentSeverity.Verify;

            return new AssignmentRowVm
            {
                Index         = r.Index,
                Name          = r.DisplayName,
                SignatureDate = dateValue,
                IsHint        = isHint,
                DateToolTip   = tooltip,
                DateImage     = r.DateCropImage,
                Comments      = comments,
                Severity      = severity,
            };
        }).ToList();

        return (rows, mismatches);
    }

    private static List<string> CollectPdfs(string folder)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var f in Directory.EnumerateFiles(folder).OrderBy(x => x))
        {
            if (!string.Equals(Path.GetExtension(f), ".pdf", StringComparison.OrdinalIgnoreCase))
                continue;
            if (seen.Add(f.ToLowerInvariant()))
                result.Add(f);
        }
        return result;
    }

    // ── ADS comparison display ────────────────────────────────────────────────

    private void ShowAdsMismatches(List<string> mismatches)
    {
        AdsMismatchList.ItemsSource = mismatches;
    }

    // ── Copy-to-clipboard ─────────────────────────────────────────────────────

    private void CopyCell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is string text)
            Copy(text);
    }

    /// <summary>
    /// First click on a date cell copies the value and selects it; click again to edit.
    /// Keeps the cell editable (for correcting OCR hints) while matching the click-to-copy
    /// behaviour of the Name column.
    /// </summary>
    private void DateBox_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox tb && !tb.IsKeyboardFocusWithin)
        {
            tb.Focus();
            tb.SelectAll();
            Copy(tb.Text);
            e.Handled = true;
        }
    }

    private void Copy(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try { Clipboard.SetText(text); } catch { /* clipboard busy */ }
        StatusText.Text = $"Copied “{text}” to clipboard.";
    }

    // ── Date image enlargement ────────────────────────────────────────────────

    private void DateImage_Enlarge(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is AssignmentRowVm vm
            && vm.DateImage != null)
        {
            OverlayImage.Source    = vm.DateImage;
            ImageOverlay.Visibility = Visibility.Visible;
        }
    }

    private void ImageOverlay_Close(object sender, MouseButtonEventArgs e)
    {
        ImageOverlay.Visibility = Visibility.Collapsed;
    }
}
