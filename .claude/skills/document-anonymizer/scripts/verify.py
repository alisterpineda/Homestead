#!/usr/bin/env python3
"""Verify an anonymized document: assert each original PII string is GONE.

Usage:
    verify.py OUTPUT --strings STRINGS.json

  OUTPUT       the anonymized .pdf or image
  STRINGS.json a JSON array of the original strings you scrubbed:
                 ["Jane Q. Public", "(780) 123-4567", "123456789RT"]

For a PDF this checks the EXTRACTABLE TEXT LAYER across all pages — the exact
channel a "box over the text" redaction fails to clear — and that metadata is
empty. For an image it re-OCRs. Exit code is non-zero if anything still appears.

Verification is necessary but not sufficient: always ALSO look at the rendered
output, because OCR/extraction can miss text it failed to read.
"""
import json
import os
import subprocess
import sys


def pdf_text(path):
    return subprocess.run(["pdftotext", "-layout", path, "-"],
                          capture_output=True, text=True).stdout


def ocr_text(path):
    # "stdout" is tesseract's documented stdout target (matches redact_image_ai.py);
    # "-" is not portable across builds.
    return subprocess.run(["tesseract", path, "stdout"],
                          capture_output=True, text=True).stdout


def main():
    args = sys.argv[1:]
    if len(args) < 3 or "--strings" not in args:
        sys.exit(__doc__)
    path = args[0]
    with open(args[args.index("--strings") + 1]) as fh:
        strings = json.load(fh)
    ext = os.path.splitext(path)[1].lower()

    text = pdf_text(path) if ext == ".pdf" else ocr_text(path)
    leaks = []
    for s in strings:
        n = text.count(s)
        print(f"  [{'LEAK' if n else 'gone'}] {n}x  {s!r}")
        if n:
            leaks.append(s)

    meta_bad = False
    if ext == ".pdf":
        import fitz
        # `format` (PDF version) and `encryption` are intrinsic to the file and
        # are not provenance — set_metadata({}) can't clear them, so ignore them.
        intrinsic = {"format", "encryption"}
        md = {k: v for k, v in (fitz.open(path).metadata or {}).items()
              if v and k not in intrinsic}
        if md:
            meta_bad = True
            print(f"  [LEAK] metadata not empty: {md}")
        else:
            print("  [gone] metadata empty")

    if leaks or meta_bad:
        print("\nFAILED — re-scrub the items above, then re-verify.", file=sys.stderr)
        sys.exit(1)
    print("\nPASS — no scrubbed strings remain. Now do a VISUAL review of the render.")


if __name__ == "__main__":
    main()
