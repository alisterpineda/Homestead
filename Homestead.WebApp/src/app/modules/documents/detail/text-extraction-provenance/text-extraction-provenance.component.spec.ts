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
});
