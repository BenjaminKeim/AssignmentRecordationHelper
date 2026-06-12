# Assignment Recordation Helper

A self-contained Windows desktop app (WPF / .NET 9) that reads executed patent
assignment PDFs and the filed ADS, then produces a **four-column table** ready
for field-by-field entry into the USPTO Assignment Center web form.

```
Name              │ Signature Date (MM/DD/YYYY) │ Date Image   │ Comments
──────────────────┼─────────────────────────────┼──────────────┼────────────────────────
Inventor A        │ 10/20/2025                  │ —            │
Inventor B        │ 10/05/2025                  │ —            │
Inventor C        │ [10/02/2025]                │ [crop image] │ handwritten — verify image
```

Each **Name** and **Signature Date** cell copies to clipboard on click.
The **Date Image** column shows a tight crop of the handwritten date box for
human verification; click to enlarge. The **Comments** column flags issues
(NAME MISMATCH, no signature, ambiguous date order, date missing).

An **ADS comparison** section below the table shows only inventors that don't
match between the assignments and the ADS — a green checkmark if all match.

## What it does

1. **Classifies** dropped/selected files automatically — detects which is the
   combined assignment PDF (or folder of individual PDFs) and which is the ADS
   (`PTO/AIA/14`), based on filename patterns and document content.
2. **Splits** the combined assignment PDF into per-inventor page ranges using
   `Page 1 of N` markers; falls back to two pages each.
3. **Extracts** the inventor name (page 1 ASSIGNOR + page 2 printed name) and
   the execution date. Handles typed, scanned, and handwritten dates.
4. **Renders date box crops** with annotation support (PDFium `RenderAnnotations`)
   so ink signatures and e-signature annotation dates are visible. Trims each
   crop to a tight bounding box around the writing.
5. **Checks** name consistency, signature presence, and date validity; surfaces
   issues in the Comments column with color-coded severity.
6. **Compares** assignment inventors against the ADS using a surname + first-name
   prefix match with Unicode normalization; handles XFA dynamic PDFs (USPTO ADS
   forms are XFA) without any external PDF library.

## Execution dates

| Source | How read | Shown as |
|--------|----------|----------|
| Typed text layer | PdfPig word extraction | Filled date, normal style |
| E-signature annotation | PDFium + Tesseract crop OCR | Filled date, normal style |
| Handwritten | Date-box crop image (Tesseract hint) | Italic hint; verify against image |

Typed dates are parsed in all formats seen in real assignments:
`2 Oct 2025`, `20. Oct. 2025`, `02/10/2025`, `10/02/2025`, `2025-10-02`, …

Handwritten dates provide an OCR **hint** only — never auto-accepted. The crop
image is always shown so the human can read the date directly.

Date recovery for garbled OCR output:
- Replaces slash-like characters (`1 l | ! \`) between digits with `/`
- Fuzzy-matches garbled month tokens via Levenshtein distance
  (`"ock"` → `"Oct"`, `"Morch"` → `"Mar"`)

## Usage

1. Run `AssignmentRecordationHelper.exe`.
2. **Drag** the assignments and ADS PDFs anywhere onto the window, or click the
   ASSIGNMENTS / ADS picker boxes to select them. Files are classified automatically.
3. Click **Process**.
4. Click any **Name** or **Signature Date** cell to copy it to clipboard.

Accepts: a single combined assignment PDF, a folder of individual assignment PDFs,
or any mix. The ADS can be XFA (dynamic form) or flat text.

## Building

Requires .NET 9 SDK and Visual Studio 2022 (or `dotnet build`).

```powershell
dotnet build AssignmentRecordationHelper.csproj -c Release
```

**Self-contained publish** (single folder, no .NET runtime required on target):

```powershell
dotnet publish AssignmentRecordationHelper.csproj -r win-x64 --self-contained -c Release
```

Tesseract `eng.traineddata` is embedded as a resource and extracted to a temp
folder at runtime — no Tesseract installation needed on the target machine.

## Dependencies

| Package | Purpose |
|---------|---------|
| [PdfPig](https://github.com/UglyToad/PdfPig) | Text extraction + word layout |
| [Docnet.Core](https://github.com/GowenGit/docnet) | Page rendering (PDFium) with annotation support |
| [Tesseract](https://github.com/charlesw/tesseract) (.NET wrapper) | Date-box OCR |
| System.Drawing.Common | BGRA → PNG conversion for Tesseract input |

The XFA extractor (`Services/PdfXfaExtractor.cs`) is a self-contained PDF
object-tree parser — no third-party library needed to read USPTO ADS dynamic forms.

## License

Internal Newport IP tooling.
