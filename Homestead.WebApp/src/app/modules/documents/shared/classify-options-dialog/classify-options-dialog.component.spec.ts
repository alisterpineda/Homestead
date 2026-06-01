import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { ClassifyOptionsDialogComponent } from './classify-options-dialog.component';
import { DocumentApiService } from '@/api/services/document-api.service';
import { AuthService } from '@/shared/services/auth.service';
import type { AiProviderSummary } from '@/api/generated/models/ai-provider-summary';

function provider(over: Partial<AiProviderSummary> = {}): AiProviderSummary {
  return { id: 'p1', name: 'OpenAI', isEnabled: true, providerType: 'OpenAI', ...over };
}

describe('ClassifyOptionsDialogComponent', () => {
  let storage: Record<string, string>;

  function setup(opts: { providers?: AiProviderSummary[]; isAdmin?: boolean } = {}) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        {
          provide: DocumentApiService,
          useValue: { getAiProviderSummaries: vi.fn(() => of(opts.providers ?? [])) },
        },
        { provide: AuthService, useValue: { isAdmin: signal(opts.isAdmin ?? false) } },
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
    const fixture = setup({ providers: [provider()] });
    fixture.componentRef.setInput('showFields', false);
    const c = fixture.componentInstance;
    c.onStateChanged('open');

    expect(c.confirmDisabled()).toBe(false);

    let emitted: { aiProviderId: string | null; fields: string[] } | undefined;
    c.confirmed.subscribe(v => (emitted = v));
    c.confirm({ close: vi.fn() });

    expect(emitted?.fields).toEqual([]);
  });

  it('flags no-providers and disables confirm once an empty list has loaded', () => {
    const c = setup({ providers: [] }).componentInstance;
    expect(c.noProviders()).toBe(false); // not loaded yet

    c.onStateChanged('open');

    expect(c.providersLoaded()).toBe(true);
    expect(c.noProviders()).toBe(true);
    expect(c.confirmDisabled()).toBe(true);
  });

  it('treats a list with only disabled providers as no-providers', () => {
    const c = setup({ providers: [provider({ isEnabled: false })] }).componentInstance;
    c.onStateChanged('open');

    expect(c.enabledProviders()).toEqual([]);
    expect(c.noProviders()).toBe(true);
    expect(c.confirmDisabled()).toBe(true);
  });

  it('does not flag no-providers when an enabled provider exists', () => {
    const c = setup({ providers: [provider()] }).componentInstance;
    c.onStateChanged('open');

    expect(c.noProviders()).toBe(false);
    expect(c.confirmDisabled()).toBe(false);
  });

  it('re-evaluates no-providers when reopened with a different result', () => {
    let response: AiProviderSummary[] = [];
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        { provide: DocumentApiService, useValue: { getAiProviderSummaries: vi.fn(() => of(response)) } },
        { provide: AuthService, useValue: { isAdmin: signal(false) } },
      ],
    });
    TestBed.overrideComponent(ClassifyOptionsDialogComponent, { set: { template: '<div></div>' } });
    const c = TestBed.createComponent(ClassifyOptionsDialogComponent).componentInstance;

    c.onStateChanged('open');
    expect(c.noProviders()).toBe(true);

    // A provider was added in the meantime; reopening resets the loaded flag and re-fetches.
    response = [provider()];
    c.onStateChanged('open');
    expect(c.noProviders()).toBe(false);
  });

  it('tailors the empty-state message for admins', () => {
    const c = setup({ isAdmin: true }).componentInstance;
    expect(c.emptyStateMessage()).toContain('Add and enable one to classify documents');
  });

  it('tailors the empty-state message for non-admins', () => {
    const c = setup({ isAdmin: false }).componentInstance;
    expect(c.emptyStateMessage()).toContain('An administrator must add and enable one');
  });

  it('empty-state message mentions summaries when showFields is false', () => {
    const fixture = setup({ isAdmin: true });
    fixture.componentRef.setInput('showFields', false);
    expect(fixture.componentInstance.emptyStateMessage()).toContain('generate summaries');
  });
});
