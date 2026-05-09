import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { EditTextExtractionEngineSheetComponent } from './edit-text-extraction-engine-sheet.component';
import type { TextExtractionEngineResponse } from '@/api/generated/models/text-extraction-engine-response';

if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

function makeEngine(overrides: Partial<TextExtractionEngineResponse> = {}): TextExtractionEngineResponse {
  return {
    id: 'engine-1',
    name: 'My Engine',
    engineType: 'Tesseract',
    configuration: null,
    isBuiltIn: false,
    createdAt: '2025-01-15T00:00:00Z',
    updatedAt: '2025-01-15T00:00:00Z',
    createdByUserName: null,
    ...overrides,
  };
}

describe('EditTextExtractionEngineSheetComponent', () => {
  let fixture: ComponentFixture<EditTextExtractionEngineSheetComponent>;
  let component: EditTextExtractionEngineSheetComponent;

  beforeEach(() => {
    vi.stubGlobal('localStorage', {
      getItem: vi.fn().mockReturnValue(null),
      setItem: vi.fn(),
      removeItem: vi.fn(),
    });

    TestBed.configureTestingModule({
      providers: [provideHttpClient()],
    });

    fixture = TestBed.createComponent(EditTextExtractionEngineSheetComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('should create the component', () => {
    expect(component).toBeTruthy();
  });

  it('should have closed state by default', () => {
    expect(component.state()).toBe('closed');
  });

  it('should have null engine by default', () => {
    expect(component.engine()).toBeNull();
  });

  it('should have empty editName initially', () => {
    expect(component.editName()).toBe('');
  });

  it('should have ["Eng"] as default editLanguages', () => {
    expect(component.editLanguages()).toEqual(['Eng']);
  });

  it('should have empty editEndpoint and editApiKey initially', () => {
    expect(component.editEndpoint()).toBe('');
    expect(component.editApiKey()).toBe('');
  });

  it('should have null editConfiguration initially', () => {
    expect(component.editConfiguration()).toBeNull();
  });

  it('should not be saving initially', () => {
    expect(component.saving()).toBe(false);
  });

  it('should have no changes when engine is null', () => {
    expect(component.hasChanges()).toBe(false);
  });

  it('should have a languages list', () => {
    expect(component.languages).toBeDefined();
    expect(component.languages.length).toBeGreaterThan(0);
  });

  it('should extract input value from event via asInputValue', () => {
    const event = { target: { value: 'new name' } } as unknown as Event;
    expect(component.asInputValue(event)).toBe('new name');
  });

  describe('getEngineTypeLabel', () => {
    it('should return Tesseract for Tesseract', () => {
      expect(component.getEngineTypeLabel('Tesseract')).toBe('Tesseract');
    });

    it('should return friendly label for AzureDocumentIntelligence', () => {
      expect(component.getEngineTypeLabel('AzureDocumentIntelligence')).toBe('Azure Document Intelligence');
    });

    it('should return raw value for unknown type', () => {
      expect(component.getEngineTypeLabel('UnknownType')).toBe('UnknownType');
    });
  });

  describe('getLanguagesLabel', () => {
    it('should return English for null configuration', () => {
      expect(component.getLanguagesLabel(null)).toBe('English');
    });

    it('should return English for undefined configuration', () => {
      expect(component.getLanguagesLabel(undefined)).toBe('English');
    });

    it('should return the correct label for a single known language', () => {
      const config = JSON.stringify({ languages: ['Deu'] });
      expect(component.getLanguagesLabel(config)).toBe('German');
    });

    it('should return comma-separated labels for multiple languages', () => {
      const config = JSON.stringify({ languages: ['Eng', 'Fra'] });
      expect(component.getLanguagesLabel(config)).toBe('English, French');
    });

    it('should return the raw language value for an unknown language', () => {
      const config = JSON.stringify({ languages: ['Xyz'] });
      expect(component.getLanguagesLabel(config)).toBe('Xyz');
    });

    it('should return Unknown for invalid JSON', () => {
      expect(component.getLanguagesLabel('not-json')).toBe('Unknown');
    });

    it('should return English for empty languages array', () => {
      const config = JSON.stringify({ languages: [] });
      expect(component.getLanguagesLabel(config)).toBe('English');
    });
  });

  describe('editConfiguration (derived)', () => {
    it('should serialize languages for a Tesseract engine', () => {
      const engine = makeEngine({ engineType: 'Tesseract' });
      fixture.componentRef.setInput('engine', engine);
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();

      component.editLanguages.set(['Fra']);
      expect(component.editConfiguration()).toBe(JSON.stringify({ languages: ['Fra'] }));
    });

    it('should serialize multiple languages for a Tesseract engine', () => {
      const engine = makeEngine({ engineType: 'Tesseract' });
      fixture.componentRef.setInput('engine', engine);
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();

      component.editLanguages.set(['Eng', 'Fra']);
      expect(component.editConfiguration()).toBe(JSON.stringify({ languages: ['Eng', 'Fra'] }));
    });

    it('should serialize endpoint and apiKey for an Azure DI engine', () => {
      const engine = makeEngine({ engineType: 'AzureDocumentIntelligence' });
      fixture.componentRef.setInput('engine', engine);
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();

      component.editEndpoint.set('https://test.cognitiveservices.azure.com');
      component.editApiKey.set('key123');
      expect(component.editConfiguration()).toBe(
        JSON.stringify({ endpoint: 'https://test.cognitiveservices.azure.com', apiKey: 'key123' })
      );
    });

    it('should reflect editApiKey changes for an Azure DI engine', () => {
      const engine = makeEngine({ engineType: 'AzureDocumentIntelligence' });
      fixture.componentRef.setInput('engine', engine);
      fixture.componentRef.setInput('state', 'open');
      fixture.detectChanges();

      component.editEndpoint.set('https://test.cognitiveservices.azure.com');
      component.editApiKey.set('newkey');
      expect(component.editConfiguration()).toBe(
        JSON.stringify({ endpoint: 'https://test.cognitiveservices.azure.com', apiKey: 'newkey' })
      );
    });
  });

  describe('onStateChanged', () => {
    it('should emit closed on state change to closed', () => {
      const spy = vi.fn();
      component.closed.subscribe(spy);
      component.onStateChanged('closed');
      expect(spy).toHaveBeenCalled();
    });
  });
});
