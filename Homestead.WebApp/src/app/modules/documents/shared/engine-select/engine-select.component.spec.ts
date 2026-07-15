import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { describe, it, expect } from 'vitest';
import { of, throwError, type Observable } from 'rxjs';
import { EngineSelectComponent } from './engine-select.component';
import { DocumentApiService } from '@/api/services/document-api.service';
import type { TextExtractionEngineResponse } from '@/api/generated/models/text-extraction-engine-response';

const ENGINES: TextExtractionEngineResponse[] = [
  { key: 'tesseract', name: 'Tesseract', type: 'Tesseract', isConfigured: true, configurationError: null },
  {
    key: 'azure-layout',
    name: 'Azure Document Intelligence',
    type: 'AzureDocumentIntelligence',
    isConfigured: false,
    configurationError: 'Endpoint is required.',
  },
];

// Configured per test rather than in a beforeEach: TextExtractionEnginesService is providedIn: 'root',
// so the engine list it caches is fixed the first time the module is instantiated — a test needing a
// different list needs a fresh module.
function setup(engines: () => Observable<TextExtractionEngineResponse[]> = () => of(ENGINES)) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      { provide: DocumentApiService, useValue: { getTextExtractionEngines: engines } },
    ],
  });
  return TestBed.createComponent(EngineSelectComponent);
}

describe('EngineSelectComponent', () => {
  it('lists the configured engines', () => {
    expect(setup().componentInstance.engines().map(e => e.key)).toEqual(['tesseract', 'azure-layout']);
  });

  it('labels the empty selection with the default label', () => {
    expect(setup().componentInstance.engineToString('')).toBe('Household default');
  });

  it('labels an engine by key and name', () => {
    expect(setup().componentInstance.engineToString('tesseract')).toBe('tesseract — Tesseract');
  });

  // A key config no longer knows about should still render as itself rather than blank.
  it('falls back to the raw key for an unknown engine', () => {
    expect(setup().componentInstance.engineToString('deleted-engine')).toBe('deleted-engine');
  });

  // The reason has to live on the option row: an unavailable engine is [disabled], so it can never become
  // the selection and selectedError() can never fire for it in the reprocess dialogs.
  it('surfaces why an unavailable engine cannot run on its own row, unselected', () => {
    const [tesseract, azure] = setup().componentInstance.engines();

    expect(setup().componentInstance.unavailableReason(azure)).toBe('Endpoint is required.');
    expect(setup().componentInstance.unavailableReason(tesseract)).toBeNull();
  });

  // The backend always sends a reason alongside isConfigured: false, but an empty "()" would be worse than
  // a bare marker if it ever stopped.
  it('falls back to a bare marker when an unavailable engine has no reason', () => {
    const fixture = setup(() => of([
      { key: 'broken', name: 'Broken', type: 'Tesseract', isConfigured: false, configurationError: null },
    ]));

    expect(fixture.componentInstance.unavailableReason(fixture.componentInstance.engines()[0]))
      .toBe('unavailable');
  });

  it('surfaces why the selected engine cannot run', () => {
    const fixture = setup();
    fixture.componentRef.setInput('value', 'azure-layout');

    expect(fixture.componentInstance.selectedError()).toBe('Endpoint is required.');
  });

  it('reports no error for a usable or empty selection', () => {
    const fixture = setup();
    expect(fixture.componentInstance.selectedError()).toBeNull();

    fixture.componentRef.setInput('value', 'tesseract');
    expect(fixture.componentInstance.selectedError()).toBeNull();
  });

  it('degrades to default-only when the engine list cannot be fetched', () => {
    const fixture = setup(() => throwError(() => new Error('offline')));

    expect(fixture.componentInstance.engines()).toEqual([]);
    expect(fixture.componentInstance.selectedError()).toBeNull();
  });
});
