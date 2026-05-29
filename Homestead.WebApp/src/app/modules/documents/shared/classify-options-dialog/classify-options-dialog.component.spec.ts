import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { ClassifyOptionsDialogComponent } from './classify-options-dialog.component';
import { DocumentApiService } from '@/api/services/document-api.service';

describe('ClassifyOptionsDialogComponent', () => {
  let storage: Record<string, string>;

  function setup() {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        { provide: DocumentApiService, useValue: { getAiProviderSummaries: vi.fn(() => of([])) } },
      ],
    });
    // The real template renders Spartan dialog/select portals that jsdom can't host;
    // we only exercise the component's field/provider logic.
    TestBed.overrideComponent(ClassifyOptionsDialogComponent, { set: { template: '<div></div>' } });
    return TestBed.createComponent(ClassifyOptionsDialogComponent);
  }

  beforeEach(() => {
    storage = {};
    vi.stubGlobal('localStorage', {
      getItem: vi.fn((k: string) => storage[k] ?? null),
      setItem: vi.fn((k: string, v: string) => { storage[k] = v; }),
      removeItem: vi.fn((k: string) => { delete storage[k]; }),
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('defaults all three fields checked and enables confirm', () => {
    const c = setup().componentInstance;
    expect(c.fields()).toEqual({ tags: true, parties: true, name: true });
    expect(c.confirmDisabled()).toBe(false);
  });

  it('disables confirm when no field is selected', () => {
    const c = setup().componentInstance;
    c.setField('tags', false);
    c.setField('parties', false);
    c.setField('name', false);
    expect(c.noFieldsSelected()).toBe(true);
    expect(c.confirmDisabled()).toBe(true);
  });

  it('emits aiProviderId and the selected fields on confirm', () => {
    const c = setup().componentInstance;
    c.selectedProviderId.set('provider-1');
    c.setField('parties', false); // leaves Tags + Name

    let emitted: { aiProviderId: string | null; fields: string[] } | undefined;
    c.confirmed.subscribe(v => (emitted = v));

    const close = vi.fn();
    c.confirm({ close });

    expect(close).toHaveBeenCalled();
    expect(emitted).toEqual({ aiProviderId: 'provider-1', fields: ['Tags', 'Name'] });
  });

  it('restores the remembered field choice from storage', () => {
    storage['homestead-classify-fields'] = JSON.stringify({ tags: false, parties: true, name: false });
    const c = setup().componentInstance;
    expect(c.fields()).toEqual({ tags: false, parties: true, name: false });
  });

  it('emits empty fields and is never gated when showFields is false (summarize)', () => {
    const fixture = setup();
    fixture.componentRef.setInput('showFields', false);
    const c = fixture.componentInstance;

    expect(c.confirmDisabled()).toBe(false);

    let emitted: { aiProviderId: string | null; fields: string[] } | undefined;
    c.confirmed.subscribe(v => (emitted = v));
    c.confirm({ close: vi.fn() });

    expect(emitted?.fields).toEqual([]);
  });
});
