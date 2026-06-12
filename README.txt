Assignment Recordation Helper
==============================

Extracts inventor names and execution dates from executed patent assignment PDFs,
and compares them against the ADS inventor list for USPTO Assignment Center entry.


REQUIREMENTS
------------
Windows 10/11 (x64). No Python, no internet, no admin install required.
The app is self-contained — all dependencies are bundled in the single executable.


USAGE
-----
1. Run AssignmentRecordationHelper.exe
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


NOTES
-----
- Date format: MM/DD/YYYY (USPTO Assignment Center field format).
- Both combined PDFs and folders of individual PDFs are accepted.
- OCR dates are extracted via Tesseract (bundled, eng.traineddata embedded).
- Handwriting OCR hint: the TrOCR ONNX implementation is a placeholder in v1.
  The date image crop is shown for all handwritten dates so the human can read
  the date directly.


VERSION
-------
1.0.0 — initial C# port of the Python prototype.


-------------------------------------------------------------------------------
MIT License

Copyright (c) 2025 Benjamin Keim

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
-------------------------------------------------------------------------------
