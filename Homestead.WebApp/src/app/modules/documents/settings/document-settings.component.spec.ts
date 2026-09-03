import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { of } from 'rxjs';
import { DocumentSettingsComponent } from './document-settings.component';
import { DocumentApiService } from '@/api/services/document-api.service';
import type { TextExtractionEngineResponse } from '@/api/generated/models/text-extraction-engine-response';

const ENGINES: TextExtractionEngineResponse[] = [
  { key: 'tesseract', name: 'Tesseract', type: 'Tesseract', isConfigured: true, configurationError: null, isDefault: false },
  {
    key: 'azure-layout',
    name: 'Azure Document Intelligence',
    type: 'AzureDocumentIntelligence',
    isConfigured: false,
    configurationError: 'Endpoint is required.',
    isDefault: true,
  },
];

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
      of({ autoClassifyOnUpload: true }),
    );

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideRouter([]),
        {
          provide: DocumentApiService,
          useValue: {
            getDocumentSettings: () => of({ autoClassifyOnUpload: null }),
            patchDocumentSettings,
            getTextExtractionEngines: () => of(ENGINES),
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
  });

  it('should set selectedAutoClassify and report a change when toggled', () => {
    component.onAutoClassifyChange(true);
    expect(component.selectedAutoClassify()).toBe(true);
    expect(component.hasAiChanges()).toBe(true);
  });

  it('saves only the AI op from the AI card', () => {
    component.onAutoClassifyChange(true);

    component.saveAi();

    expect(patchDocumentSettings).toHaveBeenCalledWith({
      body: [{ op: 'replace', path: '/autoClassifyOnUpload', value: true }],
    });
  });

  // The engine card is read-only: engines and the default come from Ocr:* config, so the page reports
  // them the way the admin AI status page does.
  it('lists the configured engines', () => {
    fixture.detectChanges();
    const rows = Array.from(fixture.nativeElement.querySelectorAll('[data-testid="engine-row"]')) as HTMLElement[];

    expect(rows.map(r => r.textContent)).toHaveLength(2);
    expect(rows[0].textContent).toContain('tesseract');
    expect(rows[0].textContent).toContain('Configured');
    expect(rows[1].textContent).toContain('azure-layout');
    expect(rows[1].textContent).toContain('Not configured');
    expect(rows[1].textContent).toContain('Endpoint is required.');
    expect(fixture.nativeElement.textContent).toContain('Ocr:DefaultEngine');
  });

  it('marks the default engine', () => {
    fixture.detectChanges();
    const rows = Array.from(fixture.nativeElement.querySelectorAll('[data-testid="engine-row"]')) as HTMLElement[];

    expect(rows[0].textContent).not.toContain('Default');
    expect(rows[1].textContent).toContain('Default');
  });
});
