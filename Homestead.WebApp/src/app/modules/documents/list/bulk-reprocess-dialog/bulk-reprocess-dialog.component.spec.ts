import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { of } from 'rxjs';
import { BulkReprocessDialogComponent } from './bulk-reprocess-dialog.component';
import { DocumentApiService } from '@/api/services/document-api.service';
import type { TextExtractionEngineResponse } from '@/api/generated/models/text-extraction-engine-response';

const ENGINES: TextExtractionEngineResponse[] = [
  { key: 'tesseract', name: 'Tesseract', type: 'Tesseract', isConfigured: true, configurationError: null },
  {
    key: 'azure-layout',
    name: 'Azure Document Intelligence',
    type: 'AzureDocumentIntelligence',
    isConfigured: true,
    configurationError: null,
  },
];

describe('BulkReprocessDialogComponent', () => {
  let fixture: ComponentFixture<BulkReprocessDialogComponent>;
  let storage: Record<string, string>;
  let component: BulkReprocessDialogComponent;
  let bulkReprocessDocuments: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    // persistedSignal reads/writes localStorage; stub it per test so the engine choice cannot leak
    // between tests (same pattern as classify-options-dialog).
    storage = {};
    vi.stubGlobal('localStorage', {
      getItem: vi.fn((k: string) => storage[k] ?? null),
      setItem: vi.fn((k: string, v: string) => { storage[k] = v; }),
      removeItem: vi.fn((k: string) => { delete storage[k]; }),
    });

    bulkReprocessDocuments = vi.fn().mockReturnValue(of({ jobIds: ['job-1'] }));

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        {
          provide: DocumentApiService,
          useValue: {
            bulkReprocessDocuments,
            getTextExtractionEngines: () => of(ENGINES),
          },
        },
      ],
    });
    fixture = TestBed.createComponent(BulkReprocessDialogComponent);
    fixture.componentRef.setInput('documentIds', new Set(['doc-1', 'doc-2']));
    component = fixture.componentInstance;
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should not be reprocessing initially', () => {
    expect(component.reprocessing()).toBe(false);
  });

  it('should compute document count from input', () => {
    expect(component.documentCount()).toBe(2);
  });

  it('should reset reprocessing when dialog opens', () => {
    component.reprocessing.set(true);
    component.onStateChanged('open');
    expect(component.reprocessing()).toBe(false);
  });

  it('sends the selected engine key with the ids', () => {
    component.engineKey.set('azure-layout');

    component.handleReprocess({ close: () => {} });

    expect(bulkReprocessDocuments).toHaveBeenCalledWith({
      body: { ids: ['doc-1', 'doc-2'], engineKey: 'azure-layout' },
    });
  });

  it('sends a null engine key when nothing is selected', () => {
    component.handleReprocess({ close: () => {} });

    expect(bulkReprocessDocuments).toHaveBeenCalledWith({
      body: { ids: ['doc-1', 'doc-2'], engineKey: null },
    });
  });

  // A bulk reprocess has no page or document cap, so a metered engine over a large selection can spend
  // real money on one click.
  it('warns about cost only when a metered engine is picked', () => {
    expect(component.showCostWarning()).toBe(false);

    component.engineKey.set('tesseract');
    expect(component.showCostWarning()).toBe(false);

    component.engineKey.set('azure-layout');
    expect(component.showCostWarning()).toBe(true);
  });
});
