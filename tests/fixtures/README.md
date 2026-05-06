# Test Fixtures (Public Domain)

Real-world PDFs used by Homestead's test suites alongside generated fixtures.
Each file's source URL is recorded for provenance only; once committed,
the file in this directory is the source of truth (do not silently re-download).

| File | Source | License | Notes |
|---|---|---|---|
| irs-form-w9.pdf | https://www.irs.gov/pub/irs-pdf/fw9.pdf | US federal — public domain by statute (17 U.S.C. § 105) | Rev. March 2024, 6 pages, captured 2026-05-06 |
| nist-sp-250-93-vacuum-calibration.pdf | https://nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.250-93.pdf | US federal — public domain | 43 pages, has figures; useful for thumbnail tests |
| usda-farmers-bulletin-1923-index.pdf | https://archive.org/details/sim_farmers-bulletin_1923_index-contents | Public domain (pre-1928 USDA publication) | Image-only scan with an Internet-Archive-applied OCR text layer (≈14 KB of noisy OCR — see note below) |
| gutenberg-scrap-book-elementary-math.pdf | https://www.gutenberg.org/files/40624/40624-pdf.pdf | Project Gutenberg License (US public domain) | ~350 pages of long-form prose with embedded text; for FTS tests |

## Notes on the USDA bulletin

The Internet Archive bakes its own OCR pass into the PDFs it hosts, so this
fixture is **not** strictly image-only. `pdftotext` extracts roughly 14 KB of
heavily garbled text (e.g. `AGRICULTGRE` instead of `AGRICULTURE`). This makes
the fixture useful for two scenarios:

- A scanned document with low-quality embedded OCR (realistic — lots of
  real-world scans arrive this way).
- Negative testing for FTS that needs to be resilient to OCR noise.

It does **not** cleanly exercise a "no text layer at all → must run our OCR"
code path. If a test needs that case, strip the text layer first or use a
purpose-built image-only fixture.

## Guidelines

- Tests should not assert on incidental content (page numbers, layout, exact byte counts).
  Assertions on intrinsic content (e.g., "the W-9 contains the word `Taxpayer`") are fine.
- Do not add a new fixture without a written reason in this README.
