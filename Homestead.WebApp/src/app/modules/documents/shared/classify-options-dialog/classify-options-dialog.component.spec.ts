import { TestBed } from '@angular/core/testing';
import { ClassifyOptionsDialogComponent } from './classify-options-dialog.component';

describe('ClassifyOptionsDialogComponent', () => {
  let storage: Record<string, string>;

  function setup() {
    TestBed.configureTestingModule({});
    // The real template renders Spartan dialog portals that jsdom can't host;
    // we only exercise the component's field-selection logic.
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

  it('defaults all five fields checked and enables confirm', () => {
    const c = setup().componentInstance;
    expect(c.fields()).toEqual({ tags: true, parties: true, name: true, documentDate: true, expiryDate: true });
    expect(c.confirmDisabled()).toBe(false);
  });

  it('disables confirm when no field is selected', () => {
    const c = setup().componentInstance;
    c.setField('tags', false);
    c.setField('parties', false);
    c.setField('name', false);
    expect(c.noFieldsSelected()).toBe(false); // the dates are still ticked
    c.setField('documentDate', false);
    c.setField('expiryDate', false);
    expect(c.noFieldsSelected()).toBe(true);
    expect(c.confirmDisabled()).toBe(true);
  });

  it('emits the selected fields on confirm', () => {
    const c = setup().componentInstance;
    c.setField('parties', false); // leaves Tags + Name + the dates

    let emitted: { fields: string[] } | undefined;
    c.confirmed.subscribe(v => (emitted = v));

    const close = vi.fn();
    c.confirm({ close });

    expect(close).toHaveBeenCalled();
    expect(emitted).toEqual({ fields: ['Tags', 'Name', 'DocumentDate', 'ExpiryDate'] });
  });

  it('emits only the date fields when only they are selected', () => {
    const c = setup().componentInstance;
    c.setField('tags', false);
    c.setField('parties', false);
    c.setField('name', false);
    c.setField('expiryDate', false);

    let emitted: { fields: string[] } | undefined;
    c.confirmed.subscribe(v => (emitted = v));
    c.confirm({ close: vi.fn() });

    expect(emitted).toEqual({ fields: ['DocumentDate'] });
  });

  it('restores the remembered field choice from storage', () => {
    storage['homestead-classify-fields'] = JSON.stringify({
      tags: false, parties: true, name: false, documentDate: false, expiryDate: true,
    });
    const c = setup().componentInstance;
    expect(c.fields()).toEqual({ tags: false, parties: true, name: false, documentDate: false, expiryDate: true });
  });

  it('restores a legacy three-key selection with the dates ticked', () => {
    storage['homestead-classify-fields'] = JSON.stringify({ tags: false, parties: true, name: false });
    const c = setup().componentInstance;
    expect(c.fields()).toEqual({ tags: false, parties: true, name: false, documentDate: true, expiryDate: true });
  });

  it('falls back to the defaults when a stored date key is not a boolean', () => {
    storage['homestead-classify-fields'] = JSON.stringify({ tags: false, parties: true, name: false, documentDate: 'yes' });
    const c = setup().componentInstance;
    expect(c.fields()).toEqual({ tags: true, parties: true, name: true, documentDate: true, expiryDate: true });
  });

  it('emits empty fields and is never gated when showFields is false (summarize)', () => {
    const fixture = setup();
    fixture.componentRef.setInput('showFields', false);
    const c = fixture.componentInstance;

    expect(c.confirmDisabled()).toBe(false);

    let emitted: { fields: string[] } | undefined;
    c.confirmed.subscribe(v => (emitted = v));
    c.confirm({ close: vi.fn() });

    expect(emitted?.fields).toEqual([]);
  });
});
