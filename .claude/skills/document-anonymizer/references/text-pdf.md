# Text-layer PDFs — redact-and-reinsert

A born-digital PDF (exported from Word / Access / a web app) draws text as glyph
operators over the page. Two things make naive redaction fail:

- **An opaque rectangle hides pixels, not text.** The string stays in the content
  stream — `pdftotext`, copy-paste, and any parser read it straight out. You must
  *remove* the text, not cover it.
- **Editing the text in place is brittle.** Fonts are usually subsetted with custom
  encodings and per-glyph widths, so swapping "John" for "Jane" can break rendering
  or shift the line. Don't hand-edit content streams.

The reliable method is **redact-and-reinsert** (`scripts/redact_reinsert.py`):

1. `page.search_for(original)` → bounding rect(s) for each occurrence.
2. Capture the font **size** and **color** from the span under that rect (so the
   replacement matches the surrounding text).
3. `page.add_redact_annot(rect, fill=(1,1,1))` then `page.apply_redactions(...)` —
   this genuinely deletes the underlying text/graphics inside the rect.
4. `page.insert_text((rect.x0, rect.y1 - size*0.18), replacement, fontsize=size,
   fontname="helv", color=color)` — draw the fabricated value back on the blank
   spot. The `-size*0.18` drops the baseline to where the original sat.

## Guidelines

- **Same character length** as the original keeps centered / fixed-layout lines
  aligned: a same-width replacement at the same `x0` stays centered automatically,
  so you don't need separate center-alignment logic.
- **Font**: base-14 `helv` (Helvetica) matches most Arial/Helvetica documents
  closely. A distinctive original font won't match perfectly — acceptable for
  de-identification; mention it if it looks off.
- **Multiple occurrences / pages**: search every page and redact each hit (the
  script loops all pages).
- **`apply_redactions(images=fitz.PDF_REDACT_IMAGE_NONE)`** leaves embedded images
  (e.g. a logo you're keeping) untouched while removing the text.
- **Not found?** If a target isn't located, the text may be split across spans
  ("Jo" + "hn"), spaced oddly, or it actually lives inside an **image** — switch to
  the image path for that item.
- **Mixed documents**: a page with little/no extractable text is image-only — use
  the image/scan path for it.

## Verify

`scripts/verify.py OUTPUT --strings originals.json` must report **zero hits** in
the extracted text layer and empty metadata. Then look at the render to confirm
placement and that nothing visual was missed.
