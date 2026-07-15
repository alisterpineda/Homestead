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

  // Unavailable engines are shown with their reason rather than hidden: "azure-layout needs an endpoint"
  // is more useful than the option silently not existing.
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
