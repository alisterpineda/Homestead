#!/usr/bin/env python3
"""Redact-and-reinsert anonymizer for text-layer PDFs.

Removes each target string from the PDF content stream (so it is no longer
selectable or extractable) and draws a fabricated replacement back at the same
position and font size. Also strips document metadata.

Usage:
    redact_reinsert.py INPUT.pdf OUTPUT.pdf REPLACEMENTS.json

REPLACEMENTS.json maps each original string to its replacement:
    {"Jane Q. Public": "Jordan Maxwell", "(780) 123-4567": "(587) 555-0143"}

Keep replacements the SAME character length as the original to preserve centered
or fixed-layout alignment. Searches every page; redacts every occurrence.
"""
import json
import sys

import fitz  # PyMuPDF


def int_to_rgb(c):
    return ((c >> 16 & 255) / 255, (c >> 8 & 255) / 255, (c & 255) / 255)


def span_at(page, rect):
    """Font size + color of the first text span intersecting rect."""
    for blk in page.get_text("dict")["blocks"]:
        for line in blk.get("lines", []):
            for span in line.get("spans", []):
                if fitz.Rect(span["bbox"]).intersects(rect):
                    return span["size"], int_to_rgb(span["color"])
    return 10.0, (0, 0, 0)


def main():
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    inp, outp, repl_path = sys.argv[1:4]
    with open(repl_path) as fh:
        replacements = json.load(fh)

    doc = fitz.open(inp)
    found = {k: 0 for k in replacements}

    for page in doc:
        # Pass 1: locate every target on this page, recording rect + size + color
        # BEFORE redacting (search can't find the text once it's removed).
        targets = []
        for original, replacement in replacements.items():
            for rect in page.search_for(original):
                size, color = span_at(page, rect)
                targets.append((rect, replacement, size, color))
                found[original] += 1
        if not targets:
            continue
        # Pass 2: truly remove the underlying text (keep images, e.g. a logo).
        for rect, *_ in targets:
            page.add_redact_annot(rect, fill=(1, 1, 1))
        page.apply_redactions(images=fitz.PDF_REDACT_IMAGE_NONE)
        # Pass 3: draw the fabricated value back at the original baseline + size.
        for rect, replacement, size, color in targets:
            page.insert_text(fitz.Point(rect.x0, rect.y1 - size * 0.18),
                             replacement, fontsize=size, fontname="helv", color=color)

    # Strip Info dict + XMP (Producer/Creator/Author/dates carry provenance).
    doc.set_metadata({})
    doc.del_xml_metadata()
    doc.save(outp, garbage=4, deflate=True)

    for original, n in found.items():
        print(f"  [{'ok' if n else '!! NOT FOUND'}] {n}x  {original!r}")
    print(f"wrote {outp}")
    if any(n == 0 for n in found.values()):
        print("WARNING: some targets were not found — check exact spelling/spacing "
              "(text may be split across spans, or live in an image).", file=sys.stderr)


if __name__ == "__main__":
    main()
