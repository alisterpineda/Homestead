import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach } from 'vitest';
import { TextExtractionProvenanceComponent } from './text-extraction-provenance.component';
import type { TextExtractionProvenanceResponse } from '@/api/generated/models/text-extraction-provenance-response';

const TESSERACT: TextExtractionProvenanceResponse = {
  engine: 'tesseract',
  engineKey: 'tesseract',
  extractedAt: '2026-07-15T10:34:02Z',
  language: 'eng',
  dpi: 300,
  psm: 3,
  oem: 3,
  pagesFromTextLayer: 12,
  pagesOcred: 0,
};

const AZURE: TextExtractionProvenanceResponse = {
  engine: 'azure-document-intelligence',
  engineKey: 'azure-layout',
  extractedAt: '2026-07-15T10:34:02Z',
  modelId: 'prebuilt-layout',
  outputContentFormat: 'Markdown',
  pageCount: 12,
};

const DOCLING: TextExtractionProvenanceResponse = {
  engine: 'docling',
  engineKey: 'docling',
  extractedAt: '2026-07-15T10:34:02Z',
  serverVersion: '1.38.2',
  tableMode: null,
  forceOcr: false,
  pageCount: 3,
  conversionStatus: 'success',
  processingSeconds: 41.66,
};

const MANUAL: TextExtractionProvenanceResponse = {
  engine: 'manual',
  engineKey: null,
  extractedAt: '2026-11-04T09:15:00Z',
  editedByDisplayName: 'Pat Proofreader',
  basisEngine: 'docling',
  // Deliberately differs from basisEngine: the line names the INSTANCE that ran, like "Extracted by" does.
  basisEngineKey: 'docling-gpu',
  basisExtractedAt: '2026-07-15T10:34:02Z',
};

describe('TextExtractionProvenanceComponent', () => {
  let fixture: ComponentFixture<TextExtractionProvenanceComponent>;
  let component: TextExtractionProvenanceComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    fixture = TestBed.createComponent(TextExtractionProvenanceComponent);
    component = fixture.componentInstance;
  });

  // The page split is the highest-value part: it reveals which branch of the hybrid PDF path ran, which
  // is otherwise unknowable without re-extracting.
  it('reports the Tesseract text-layer/OCR page split', () => {
    fixture.componentRef.setInput('provenance', TESSERACT);

    expect(component.detail()).toBe(
      "Tesseract (eng) · 12 page(s) from embedded text layer, 0 OCR'd",
    );
  });

  it('names the tool that read the text layer', () => {
    fixture.componentRef.setInput('provenance', { ...TESSERACT, textLayerExtractor: 'poppler' });
    expect(component.detail()).toBe(
      "Tesseract (eng) · 12 page(s) from embedded text layer (pdftotext -layout), 0 OCR'd",
    );

    fixture.componentRef.setInput('provenance', { ...TESSERACT, textLayerExtractor: 'pdfpig' });
    expect(component.detail()).toBe(
      "Tesseract (eng) · 12 page(s) from embedded text layer (PdfPig), 0 OCR'd",
    );
  });

  // A fully OCR'd scan never ran a text-layer read worth naming.
  it('omits the extractor when no page came from the text layer', () => {
    fixture.componentRef.setInput('provenance', {
      ...TESSERACT,
      pagesFromTextLayer: 0,
      pagesOcred: 3,
      textLayerExtractor: 'poppler',
    });

    expect(component.detail()).toBe("Tesseract (eng) · 0 page(s) from embedded text layer, 3 OCR'd");
  });

  it('reports the Azure model, format and analyzed page count', () => {
    fixture.componentRef.setInput('provenance', AZURE);

    expect(component.detail()).toBe(
      'Azure Document Intelligence (prebuilt-layout) · Markdown output · 12 page(s) analyzed',
    );
  });

  it('reports the Docling table mode, page count and processing time', () => {
    fixture.componentRef.setInput('provenance', DOCLING);
    expect(component.detail()).toBe('Docling · accurate tables · 3 page(s) · 41.7s');

    fixture.componentRef.setInput('provenance', { ...DOCLING, tableMode: 'fast', forceOcr: true });
    expect(component.detail()).toBe('Docling · fast tables · forced OCR · 3 page(s) · 41.7s');
  });

  // partial_success is the only durable sign that some pages or tables did not make it into the text.
  it('flags a partial Docling conversion', () => {
    fixture.componentRef.setInput('provenance', { ...DOCLING, conversionStatus: 'partial_success' });

    expect(component.detail()).toBe(
      'Docling · accurate tables · 3 page(s) · 41.7s · partial: some content could not be converted',
    );
  });

  // An unreadable PDF records 0 pages, and the sidecar may omit its processing time: both are left out
  // rather than shown as "0 page(s)" or a blank time.
  it('omits a Docling page count of 0 and a missing processing time', () => {
    fixture.componentRef.setInput('provenance', { ...DOCLING, pageCount: 0, processingSeconds: null });

    expect(component.detail()).toBe('Docling · accurate tables');
  });

  it('says a Docling run on an unsupported file type converted nothing', () => {
    fixture.componentRef.setInput('provenance', {
      ...DOCLING,
      conversionStatus: 'unsupported_content_type',
      pageCount: 0,
      processingSeconds: null,
      serverVersion: null,
    });

    expect(component.detail()).toBe('Docling · not converted (unsupported file type)');
  });

  // Versions extracted before provenance existed render nothing rather than an empty shell.
  it('renders nothing without provenance', () => {
    fixture.componentRef.setInput('provenance', null);
    fixture.detectChanges();

    expect(component.detail()).toBe('');
    expect(fixture.nativeElement.textContent.trim()).toBe('');
  });

  it('shows the fallback notice when a substitution happened', () => {
    fixture.componentRef.setInput('provenance', {
      ...TESSERACT,
      requestedEngineKey: 'azure-layout',
      fallbackReason: 'Endpoint is required.',
    });
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('azure-layout');
    expect(text).toContain('Endpoint is required.');
  });

  it('shows no fallback notice on a normal run', () => {
    fixture.componentRef.setInput('provenance', TESSERACT);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent as string).not.toContain('was requested but');
  });

  // A hand edit is credited to the person, never to the engine whose output they corrected.
  it('credits a manual edit to the editor, with the engine run as its basis', () => {
    fixture.componentRef.setInput('provenance', MANUAL);
    fixture.detectChanges();

    const text = (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ').trim();
    // Exactly the two manual lines: the editor line, then the basis naming the instance key.
    expect(text).toMatch(/^Edited by Pat Proofreader on .+ Based on docling-gpu extraction from .+$/);
    expect(text).not.toContain('Extracted by');
  });

  it('says text typed in over nothing was entered manually', () => {
    fixture.componentRef.setInput('provenance', {
      ...MANUAL,
      basisEngine: null,
      basisEngineKey: null,
      basisExtractedAt: null,
    });
    fixture.detectChanges();

    const text = (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ');
    expect(text).toContain('Entered manually, no engine extraction');
    expect(text).not.toContain('Based on');
  });

  // The editor's name is resolved server-side and may be missing (a user since deleted).
  it('falls back to "Someone" when the editor cannot be named', () => {
    fixture.componentRef.setInput('provenance', { ...MANUAL, editedByDisplayName: null });
    fixture.detectChanges();

    expect((fixture.nativeElement.textContent as string).replace(/\s+/g, ' ')).toContain('Edited by Someone on');
  });

  // The engine-only lines (detail, fallback notice) must not leak into the manual branch, even when a
  // stray fallback field rides along on the record.
  it('shows no engine detail or fallback notice for a manual edit', () => {
    fixture.componentRef.setInput('provenance', { ...MANUAL, requestedEngineKey: 'azure-layout' });
    fixture.detectChanges();

    const text = (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ').trim();
    expect(text).toMatch(/^Edited by Pat Proofreader on .+ Based on docling-gpu extraction from .+$/);
    expect(text).not.toContain('was requested but');
    expect(text).not.toContain('azure-layout');
  });
});
