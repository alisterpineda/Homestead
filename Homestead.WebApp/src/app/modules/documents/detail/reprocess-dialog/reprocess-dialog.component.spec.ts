import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { of } from 'rxjs';
import { ReprocessDialogComponent } from './reprocess-dialog.component';
import { DocumentVersionApiService } from '@/api/services/document-version-api.service';
import { DocumentApiService } from '@/api/services/document-api.service';

describe('ReprocessDialogComponent', () => {
  let fixture: ComponentFixture<ReprocessDialogComponent>;
  let storage: Record<string, string>;
  let component: ReprocessDialogComponent;
  let reprocessDocumentVersion: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    // persistedSignal reads/writes localStorage; stub it per test so the engine choice cannot leak
    // between tests (same pattern as classify-options-dialog).
    storage = {};
    vi.stubGlobal('localStorage', {
      getItem: vi.fn((k: string) => storage[k] ?? null),
      setItem: vi.fn((k: string, v: string) => { storage[k] = v; }),
      removeItem: vi.fn((k: string) => { delete storage[k]; }),
    });

    reprocessDocumentVersion = vi.fn().mockReturnValue(of({ jobId: 'job-1' }));

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        { provide: DocumentVersionApiService, useValue: { reprocessDocumentVersion } },
        { provide: DocumentApiService, useValue: { getTextExtractionEngines: () => of([]) } },
      ],
    });
    fixture = TestBed.createComponent(ReprocessDialogComponent);
    fixture.componentRef.setInput('documentId', 'doc-1');
    fixture.componentRef.setInput('versionId', 'ver-1');
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

  it('should reset reprocessing when dialog opens', () => {
    component.reprocessing.set(true);
    component.onStateChanged('open');
    expect(component.reprocessing()).toBe(false);
  });

  it('should not reset state when dialog closes', () => {
    component.reprocessing.set(true);
    component.onStateChanged('closed');
    expect(component.reprocessing()).toBe(true);
  });

  it('sends the selected engine key', () => {
    component.engineKey.set('azure-layout');

    component.handleReprocess({ close: () => {} });

    expect(reprocessDocumentVersion).toHaveBeenCalledWith({
      documentId: 'doc-1',
      versionId: 'ver-1',
      body: { engineKey: 'azure-layout' },
    });
  });

  // No selection must mean "configured default", not "no engine" — the backend distinguishes the two.
  it('sends a null engine key when nothing is selected', () => {
    component.handleReprocess({ close: () => {} });

    expect(reprocessDocumentVersion).toHaveBeenCalledWith(
      expect.objectContaining({ body: { engineKey: null } }),
    );
  });

  it('emits the job id and closes on success', () => {
    const close = vi.fn();
    const emitted: string[] = [];
    component.reprocessStarted.subscribe(id => emitted.push(id));

    component.handleReprocess({ close });

    expect(close).toHaveBeenCalled();
    expect(emitted).toEqual(['job-1']);
    expect(component.reprocessing()).toBe(false);
  });
});
