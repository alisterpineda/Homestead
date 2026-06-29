# Image-only pages, scans, and embedded rasters

When a page is a scanned image (or you're handed a `.png` / `.jpg`), there is **no
text layer** — the words are pixels. The upside: **covering the pixels *is*
removal** (nothing is hidden underneath), as long as you flatten the result and
don't retain the original image. The work is *locating* the PII, since you can't
`search_for` text that isn't there.

## Replace vs. redact — pick the right goal

By default this path **replaces** PII with fabricated values so the document still
looks real (the same intent as the text path). One exception: for **legal / formal
redaction** of a sensitive document, prefer a **visible opaque box** and do *not*
fabricate — a plausible fake (e.g. a fake SSN) could be mistaken for real. Replace
for realistic de-identification; cover-only when the reader must see that
information was withheld.

## OCR-locate → cover → reinsert

1. **Locate** with tesseract's word boxes — TSV gives `left top width height` per
   word:

   ```bash
   tesseract input.png out tsv     # -> out.tsv (one row per recognized word)
   ```

   Parse the TSV, find the words/runs that make up each PII string, and take the
   union of their boxes. Multi-word values span several rows — group by `line_num`
   and match the sequence, not single words.

2. **Cover** each PII box with a filled rectangle — white, or a sampled background
   color for a realistic look:
   - Raw image: Pillow `ImageDraw.Draw(img).rectangle([x0, y0, x1, y1], fill=...)`.
   - Inside a PDF: PyMuPDF `page.draw_rect(rect, fill=..., color=...)`.

3. **Reinsert** the fabricated value as text drawn on top, sized to the box
   (Pillow `ImageDraw.text`, or PyMuPDF `page.insert_text` / `insert_textbox`).

4. **Verify**: `scripts/verify.py OUTPUT --strings originals.json` re-OCRs and
   checks the originals don't reappear — **and look at the result**, because OCR on
   a noisy scan can silently miss a word.

## Embedded raster carrying PII (signature, photo, logo)

Text redaction never touches pixels inside an embedded image. If a logo / signature
/ photo identifies a party you're anonymizing:

- **Blank or replace** the image (swap the XObject, or paint over its region), or
- **Edit it with an image model** (an image-editing model) to fabricate the
  content, then reinsert. Confirm the edit didn't keep a real value or garble it,
  and that no real value remains anywhere in the file.

If you intend to **keep** a logo/brand (legitimate — e.g. scrub a store's address
but keep its brand), leave it and say so in your report.

## Rebuild a PDF from an edited raster

Wrap the image as a single-page PDF with `img2pdf`, or PyMuPDF
`Page.insert_image(rect, filename=...)`. For an image-based document the **image
channel** is what carries meaning; supply any separate text yourself.

## Optional: high-fidelity edits via an Azure image-edit model (key-gated)

The local cover-and-reinsert path can't match an unknown scanned font — it pastes a
clean replacement that may not blend in. When an Azure image-edit deployment is
configured, `scripts/redact_image_ai.py` instead **inpaints** each PII region
(crop → mask → edit → paste back), matching the surrounding font style and
background far better. It is **off by default** and falls back to the local path
when unconfigured.

Configure in a gitignored `<skill>/.env` file — the only config source (never
committed; the committed skill knows only the key names):

```ini
ANON_AZURE_IMAGE_ENDPOINT=https://<resource>.openai.azure.com
ANON_AZURE_IMAGE_KEY=<resource key>
ANON_AZURE_IMAGE_DEPLOYMENT=<image-edit deployment, e.g. gpt-image-1>
# ANON_AZURE_IMAGE_APIVERSION=2025-04-01-preview   # optional
```

Then `pip install openai pillow` and run
`redact_image_ai.py INPUT.png OUTPUT.png replacements.json`.

Know the trade-offs — they matter *more* for a privacy tool, not less:

- **Not pixel/font-perfect.** The model regenerates pixels; it matches font *style*,
  not the exact typeface, and can garble numbers. Expect "convincingly seamless,"
  not "bit-exact."
- **The safety guarantee inverts.** A generative edit can leave a faint trace of the
  original or render the wrong value, so treat the model as a *renderer*, never as
  the thing that guarantees removal. The `verify.py` gate (originals **gone** AND the
  intended replacements **present**) plus a visual diff is mandatory — exactly as for
  the local path.
- **The PII leaves the machine.** This uploads the un-anonymized crop to Azure. For
  data that must not leave the device, stay on the local path — enterprise Foundry
  terms help, but it is still egress of the very data you're protecting.
