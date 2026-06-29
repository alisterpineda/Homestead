---
name: document-anonymizer
description: >-
  De-identify a document (PDF, scan, or image) by TRULY removing personal /
  identifying information and verifying it is gone — not just covering it up.
  Use whenever the user wants to anonymize, de-identify, redact, scrub, sanitize,
  or strip PII from a document before sharing, committing, open-sourcing, or
  turning it into a test/eval fixture — names, addresses, phone/fax, emails,
  account or card numbers, government IDs (SSN/SIN/EIN/GST), dates of birth,
  signatures, or logos. Trigger even when the user just says "scrub this PDF" or
  "remove my info from this scan" without naming a technique.
---

# Document Anonymizer

De-identify a document so the original identifying information is **unrecoverable**,
then **prove** it's gone. This skill exists because the obvious approaches fail
*silently*:

- Drawing an opaque box over text in a text-layer PDF leaves the text fully
  selectable and extractable underneath. The document *looks* redacted; the PII is
  still there for anyone who runs `pdftotext` or copy-paste.
- Declaring a document clean when identifying data survived in **metadata**, an
  **embedded image** (logo / signature / photo), a **later page**, or a margin you
  didn't read.

So the whole skill is organized around three disciplines: **discover thoroughly,
remove truly, verify objectively.** Treat this as *assisted* de-identification with
a human in the loop — your job is to do the careful work and surface residual risk,
never to hand back false confidence.

## Setup

The scripts use PyMuPDF (and Pillow for image work); OCR uses the `tesseract`
binary. Once per environment:

```bash
python3 -m venv .venv-anon && .venv-anon/bin/pip install pymupdf pillow
tesseract --version   # if missing: apt-get install -y tesseract-ocr
```

Run the bundled scripts with that interpreter, e.g.
`.venv-anon/bin/python <skill>/scripts/inspect_doc.py <input>`.

**Optional — high-fidelity raster edits.** When an Azure image-edit deployment is
configured (in a gitignored `<skill>/.env` with `ANON_AZURE_IMAGE_ENDPOINT` /
`_KEY` / `_DEPLOYMENT`), the image path can inpaint scanned text to match its
original font instead of using the local replacement font. Off by default; falls
back to local when unconfigured. See `references/image-and-scan.md`.

## Workflow

### 1. Inspect

Run `scripts/inspect_doc.py <input>`. For each page it reports whether there's a
**text layer** or the page is **image-only**, plus the page count, document
**metadata**, and **embedded images**. This decides your technique and surfaces
blind spots (metadata, images) up front.

### 2. Discover the PII — this is the hard part

The mechanics of removal are easy; *finding everything* is where de-identification
succeeds or fails. Read the **full text of every page** and **look at every
rendered page**. Enumerate every identifying item:

- Names (people, and any org you intend to anonymize), addresses, phone/fax, email
- Account / card / member / invoice / case numbers; government IDs (SSN, SIN, EIN,
  GST/HST, passport, licence)
- Dates of birth, signatures, photos, handwriting, barcodes / QR codes
- Logos and letterhead **if** they identify a party you're anonymizing

Decide **per item: scrub or keep.** Keeping some identity is legitimate (e.g. keep
a store's brand but scrub its specific address, phone, and email). Build a
**replacement map** of `original → fabricated value`:

- Keep replacements **realistic and internally consistent** — if the document
  implies a city, keep the fabricated address in that city; use reserved-for-fiction
  ranges (`555-01xx` phone numbers, `.example` email domains).
- For **fixed-layout or centered text**, make each replacement the **same character
  length** as the original so the layout doesn't shift.

**Confirm the scrub/keep list with the user before applying.** Discovery is
judgment — the user knows their document and what must not leak.

### 3. Remove — by page type

| Page type | Technique | Reference |
|---|---|---|
| Text-layer PDF page | **Redact-and-reinsert**: locate each string, *remove* it from the content stream, draw the fabricated value back at the same position/size. | `references/text-pdf.md` + `scripts/redact_reinsert.py` |
| Image-only page / scan / standalone image | **OCR-locate → cover → reinsert**: OCR to find PII boxes, paint over those pixels, draw fabricated values on top. Covering pixels *is* removal for a raster (no hidden text layer). | `references/image-and-scan.md` |
| Embedded raster carrying PII (signature, logo, photo) | Blank/replace the image, or edit it with an image model when available. | `references/image-and-scan.md` |

A single PDF can be **mixed** (born-digital page 1, scanned page 2) — handle each
page by its own type.

`scripts/redact_reinsert.py <in.pdf> <out.pdf> <replacements.json>` runs the
text-PDF path and strips metadata in the same pass.

### 4. Strip metadata

Clear the Info dictionary and any XMP stream — producer / creator / author / title
and creation dates routinely carry identity or provenance (an author name, an
original filename, "Microsoft Access 2013"). The scripts do this; confirm it in
step 5.

### 5. Verify — non-negotiable

This is what separates *anonymized* from *looks anonymized*. Run
`scripts/verify.py <output> --strings <originals.json>`:

- PDF/text: extracts text across **all pages** and asserts every original string
  returns **zero hits**; re-checks metadata is empty.
- Image pages: re-OCRs and checks the originals don't reappear.

If any original survives, the scrub failed — fix the map and re-run. Then **look at
the rendered output yourself** — OCR/extraction can miss text it failed to read, so
a visual pass is required, not optional.

### 6. Report

Tell the user plainly:

- **Scrubbed** vs **kept** (the decisions from step 2)
- **Verification**: zero-hits confirmed per string; metadata cleared
- **Residual blind spots** you can't fully guarantee — a signature image, imperfect
  OCR on a noisy scan, anything you're unsure about
- Where the output is, and that they should do a **final human review before
  sharing** — this is assisted de-identification, not a guarantee

## Honest limits

State these; don't paper over them:

- **Discovery is assistive, not exhaustive.** You and the user must eyeball every
  page. A missed number in a footer is a real leak.
- **Text redaction never touches pixels.** PII inside a logo / photo / signature
  needs the image path even within a text PDF.
- **OCR is imperfect.** On a noisy scan it may miss or garble a word, hiding PII
  from the text-based verify — hence the required visual pass.
- **Re-identification.** Removing visible PII doesn't change that this is a real
  document from a real source; consider whether the remaining combination (dates,
  amounts, rare line items) is still identifying.
