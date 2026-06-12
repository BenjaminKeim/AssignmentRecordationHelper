Assignment Recordation Prep
==========================

Extracts inventor names and execution dates from executed patent assignment PDFs,
and compares them against the ADS inventor list for USPTO Assignment Center entry.


REQUIREMENTS
------------
Windows 10/11 (x64). No Python, no internet, no admin install required.
The app is self-contained — all dependencies are bundled.


USAGE
-----
1. Run AssignmentRecordationPrep.exe
2. Drag the assignments and ADS PDFs anywhere onto the window, or click the
   ASSIGNMENTS / ADS picker boxes to select them.
   - Accepts a combined PDF, a folder of individual PDFs, or several PDFs at once.
   - Files are classified automatically — drop them in any order.
3. Click Process.


RESULTS TABLE
-------------
Four columns:
  Name            - Click to copy the inventor name to clipboard.
  Signature Date  - Editable. Typed dates are pre-filled (MM/DD/YYYY).
                    Handwritten dates show an OCR hint (greyed/italic) — verify
                    against the Date Image before using. Click to copy.
  Date Image      - Crop of the date box area. Click to enlarge.
                    Shown for handwritten and annotation-layer dates.
  Comments        - Flags and caveats:
                      Yellow  = "handwritten — verify image",
                                "Possibly written in day-month order",
                                "date missing"
                      Red     = "NAME MISMATCH", "no signature"


ADS COMPARISON
--------------
Shows only mismatches (inventors on ADS but not in assignments, or vice versa).
A green checkmark means all inventors match.


PUBLISHING (self-contained exe)
-------------------------------
  dotnet publish AssignmentRecordationPrep.csproj -r win-x64 --self-contained -c Release

Produces a single folder; zip and distribute. No .NET runtime needed on the
target machine.


NOTES
-----
- Date format: MM/DD/YYYY (USPTO Assignment Center field format).
- Both combined PDFs and folders of individual PDFs are accepted.
- OCR dates are extracted via Tesseract (bundled, eng.traineddata embedded).
- Handwriting OCR hint: the TrOCR ONNX implementation is a placeholder in v1.
  The date image crop is shown for all handwritten dates so the human can read
  the date directly. See Services/HandwritingService.cs for the ONNX spec.


VERSION
-------
1.0.0 — initial C# port of the Python prototype.
