import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { of } from 'rxjs';
import { DocumentSettingsComponent } from './document-settings.component';
import { DocumentApiService } from '@/api/services/document-api.service';

describe('DocumentSettingsComponent', () => {
  let fixture: ComponentFixture<DocumentSettingsComponent>;
  let component: DocumentSettingsComponent;
  let patchDocumentSettings: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.stubGlobal('localStorage', {
      getItem: vi.fn().mockReturnValue(null),
      setItem: vi.fn(),
      removeItem: vi.fn(),
    });

    patchDocumentSettings = vi.fn().mockReturnValue(
      of({ autoClassifyOnUpload: true, defaultEngineKey: 'tesseract' }),
    );

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideRouter([]),
        {
          provide: DocumentApiService,
          useValue: {
            getDocumentSettings: () => of({ autoClassifyOnUpload: null, defaultEngineKey: null }),
            patchDocumentSettings,
            getTextExtractionEngines: () => of([]),
          },
        },
      ],
    });

    fixture = TestBed.createComponent(DocumentSettingsComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('should create the component', () => {
    expect(component).toBeTruthy();
  });

  it('should initialise with saving false', () => {
    expect(component.saving()).toBe(false);
  });

  it('should initialise with no load error', () => {
    expect(component.loadError()).toBe(false);
  });

  it('should have no changes initially', () => {
    expect(component.hasAiChanges()).toBe(false);
    expect(component.hasEngineChanges()).toBe(false);
  });

  it('should set selectedAutoClassify and report a change when toggled', () => {
    component.onAutoClassifyChange(true);
    expect(component.selectedAutoClassify()).toBe(true);
    expect(component.hasAiChanges()).toBe(true);
  });

  // Each card has its own Save button, which implies the two settings are independent — a pending
  // engine change must not light up the AI card's Save.
  it('tracks dirty state per card, not per page', () => {
    component.onEngineKeyChange('tesseract');

    expect(component.hasEngineChanges()).toBe(true);
    expect(component.hasAiChanges()).toBe(false);
  });

  it('empty engine selection means no preference', () => {
    component.onEngineKeyChange('');
    expect(component.selectedEngineKey()).toBeNull();
  });

  it('saves only the AI op from the AI card', () => {
    component.onAutoClassifyChange(true);

    component.saveAi();

    expect(patchDocumentSettings).toHaveBeenCalledWith({
      body: [{ op: 'replace', path: '/autoClassifyOnUpload', value: true }],
    });
  });

  it('saves only the engine op from the Text Extraction card', () => {
    component.onEngineKeyChange('tesseract');

    component.saveEngine();

    expect(patchDocumentSettings).toHaveBeenCalledWith({
      body: [{ op: 'replace', path: '/defaultEngineKey', value: 'tesseract' }],
    });
  });

  // The response carries both fields, but re-seeding the card that was NOT saved would throw away an
  // edit the user still has pending there.
  it('saving one card leaves the other card\'s pending edit alone', () => {
    component.onAutoClassifyChange(true);
    component.onEngineKeyChange('azure-layout');

    component.saveAi();

    expect(component.selectedEngineKey()).toBe('azure-layout');
    expect(component.hasEngineChanges()).toBe(true);
  });
});
