#!/usr/bin/env python3
"""Inspect a document before anonymizing.

Reports, per page: text-layer vs image-only, plus page count, document metadata,
and embedded images. This decides the technique (redact-and-reinsert vs
OCR-locate) and surfaces blind spots (metadata, images) before you start.

Usage: inspect.py INPUT        (INPUT: .pdf, or an image file)
"""
import os
import sys

import fitz  # PyMuPDF


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    path = sys.argv[1]
    ext = os.path.splitext(path)[1].lower()

    if ext != ".pdf":
        print(f"{path}: standalone image ({ext or 'unknown'}). No text layer — use "
              f"the image/scan path: OCR-locate -> cover -> reinsert "
              f"(references/image-and-scan.md).")
        return

    doc = fitz.open(path)
    print(f"{path}: {doc.page_count} page(s)")
    # `format`/`encryption` are intrinsic to the file, not provenance — skip them
    # so the identity-bearing fields (creator/producer/author/dates) stand out.
    intrinsic = {"format", "encryption"}
    md = {k: v for k, v in (doc.metadata or {}).items() if v and k not in intrinsic}
    print(f"metadata: {md if md else '(empty)'}")
    for i, page in enumerate(doc):
        text = page.get_text("text").strip()
        imgs = page.get_images(full=True)
        kind = "TEXT-LAYER" if len(text) > 20 else "IMAGE-ONLY (needs OCR)"
        print(f"  page {i + 1}: {kind}; {len(text)} chars extractable; "
              f"{len(imgs)} embedded image(s)")
    print("\nAny IMAGE-ONLY page, or PII inside an embedded image, needs the image "
          "path (references/image-and-scan.md). Non-empty metadata must be stripped.")


if __name__ == "__main__":
    main()
