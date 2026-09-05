import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import {
  ItemWarrantyComposerComponent,
  type ExpiryChoice,
  type WarrantyPayload,
  type WarrantySeed,
} from './item-warranty-composer.component';

function todayIso(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** The chip menu lives in an overlay template, so the presets are reached through the component. */
interface Internals {
  presetOptions(): { years: number; iso: string; label: string }[];
  expiryChoice(): ExpiryChoice;
  chooseDate(iso: string): void;
  chooseNoExpiry(): void;
}

describe('ItemWarrantyComposerComponent', () => {
  let fixture: ComponentFixture<ItemWarrantyComposerComponent>;
  let emitted: WarrantyPayload[];
  let cancelled: number;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    fixture = TestBed.createComponent(ItemWarrantyComposerComponent);
    emitted = [];
    cancelled = 0;
    fixture.componentInstance.submitted.subscribe((p) => emitted.push(p));
    fixture.componentInstance.cancelled.subscribe(() => cancelled++);
    fixture.detectChanges();
  });

  const host = () => fixture.nativeElement as HTMLElement;
  const inputs = () => Array.from(host().querySelectorAll<HTMLInputElement>('input[hlmInputGroupInput]'));
  const providerEl = () => inputs()[0];
  const notesEl = () => inputs()[1];
  const dateEl = () => host().querySelector<HTMLInputElement>('input[type="date"]')!;
  const addBtn = () => host().querySelector<HTMLButtonElement>('button[aria-label="Add warranty"]')!;
  // Deliberately not "Add note": the durable detail page also hosts the activity composer, whose
  // submit button owns that name — two identically-named actions on one page is an a11y defect.
  const notesBtn = () => host().querySelector<HTMLButtonElement>('button[aria-label="Add warranty note"]')!;
  const chipBtn = () => host().querySelector<HTMLButtonElement>('button[aria-label^="Expiry:"]')!;
  const saveBtn = () =>
    Array.from(host().querySelectorAll<HTMLButtonElement>('button')).find(b => b.textContent?.trim() === 'Save')!;
  const internals = () => fixture.componentInstance as unknown as Internals;

  function type(el: HTMLInputElement, value: string): void {
    el.value = value;
    el.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function pickDate(value: string): void {
    const el = dateEl();
    el.value = value;
    el.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function choose(fn: (i: Internals) => void): void {
    fn(internals());
    fixture.detectChanges();
  }

  describe('the expiry chip gates submit', () => {
    it('reads "Expires" and disables Add until an expiry is chosen', () => {
      expect(chipBtn().textContent).toContain('Expires');
      expect(addBtn().disabled).toBe(true);

      choose(i => i.chooseNoExpiry());
      expect(addBtn().disabled).toBe(false);
    });

    it('never unlocks Add on provider or notes alone', () => {
      type(providerEl(), 'Home Depot');
      notesBtn().click();
      fixture.detectChanges();
      type(notesEl(), 'motor only');

      expect(addBtn().disabled).toBe(true);
    });

    it('submits a null expiresOn for "No expiry" and reads it back on the chip', () => {
      choose(i => i.chooseNoExpiry());
      expect(chipBtn().textContent).toContain('No expiry');

      addBtn().click();
      expect(emitted).toEqual([{ provider: null, expiresOn: null, notes: null }]);
    });

    it('submits a picked date verbatim as yyyy-MM-dd', () => {
      pickDate('2028-03-12');
      addBtn().click();
      expect(emitted[0].expiresOn).toBe('2028-03-12');
    });

    it('leaves the date input uncapped so a lapsed warranty can be back-filled', () => {
      expect(dateEl().hasAttribute('max')).toBe(false);
      pickDate('2019-01-05');
      addBtn().click();
      expect(emitted[0].expiresOn).toBe('2019-01-05');
    });

    it('treats clearing the native date input as a retraction, not "no expiry"', () => {
      pickDate('2028-03-12');
      pickDate('');
      expect(internals().expiryChoice()).toEqual({ kind: 'unset' });
      expect(addBtn().disabled).toBe(true);
    });
  });

  describe('year presets', () => {
    it('counts from the item acquired-on date when known', () => {
      fixture.componentRef.setInput('acquiredOn', '2026-03-12');
      fixture.detectChanges();

      expect(internals().presetOptions().map(p => [p.years, p.iso])).toEqual([
        [1, '2027-03-12'],
        [2, '2028-03-12'],
        [3, '2029-03-12'],
        [5, '2031-03-12'],
      ]);
      expect(internals().presetOptions()[1].label).toContain('2 years');
    });

    it('falls back to today when the item has no acquired-on date', () => {
      const [year, month, day] = todayIso().split('-').map(Number);
      expect(internals().presetOptions()[0].iso.slice(0, 4)).toBe(String(year + 1));
      expect(internals().presetOptions()[0].iso.slice(5)).toBe(
        `${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`,
      );
    });
  });

  describe('provider and notes', () => {
    it('placeholders the provider as "Manufacturer" and submits an empty one as null', () => {
      expect(providerEl().getAttribute('placeholder')).toBe('Manufacturer');
      expect(providerEl().getAttribute('maxlength')).toBe('200');

      choose(i => i.chooseNoExpiry());
      type(providerEl(), '   ');
      addBtn().click();
      expect(emitted[0].provider).toBeNull();
    });

    it('hides the notes input until asked for, then trims what it submits', () => {
      expect(inputs()).toHaveLength(1);

      notesBtn().click();
      fixture.detectChanges();
      expect(inputs()).toHaveLength(2);
      expect(notesEl().getAttribute('placeholder')).toBe('Note (e.g. motor only)');
      expect(notesEl().getAttribute('maxlength')).toBe('500');

      choose(i => i.chooseNoExpiry());
      type(providerEl(), '  Bosch  ');
      type(notesEl(), '  motor only  ');
      addBtn().click();
      expect(emitted[0]).toEqual({ provider: 'Bosch', expiresOn: null, notes: 'motor only' });
    });
  });

  it('submits on Enter in either input once the expiry is set', () => {
    choose(i => i.chooseDate('2028-03-12'));
    type(providerEl(), 'Bosch');

    const enter = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true, bubbles: true });
    providerEl().dispatchEvent(enter);
    expect(enter.defaultPrevented).toBe(true);
    expect(emitted).toHaveLength(1);

    notesBtn().click();
    fixture.detectChanges();
    notesEl().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true, bubbles: true }));
    expect(emitted).toHaveLength(2);
  });

  it('resets the chip along with the text on clearText — an expiry is never sticky', () => {
    choose(i => i.chooseDate('2028-03-12'));
    type(providerEl(), 'Bosch');
    notesBtn().click();
    fixture.detectChanges();
    type(notesEl(), 'motor only');

    fixture.componentInstance.clearText();
    fixture.detectChanges();

    expect(providerEl().value).toBe('');
    expect(inputs()).toHaveLength(1); // notes hidden again
    expect(chipBtn().textContent).toContain('Expires');
    expect(addBtn().disabled).toBe(true);
  });

  describe('edit mode', () => {
    const SEED: WarrantySeed = { provider: 'Bosch', expiresOn: '2028-03-12', notes: 'motor only' };

    beforeEach(() => {
      fixture.componentRef.setInput('mode', 'edit');
      fixture.componentRef.setInput('initial', SEED);
      fixture.detectChanges();
    });

    it('seeds every field, reveals the note, and swaps Add for Cancel / Save', () => {
      expect(providerEl().value).toBe('Bosch');
      expect(notesEl().value).toBe('motor only');
      expect(dateEl().value).toBe('2028-03-12');
      expect(host().querySelector('button[aria-label="Add warranty"]')).toBeNull();
      expect(saveBtn()).toBeDefined();
    });

    it('seeds an open-ended row as an explicit "No expiry", never as unset', () => {
      fixture.componentRef.setInput('initial', { provider: null, expiresOn: null, notes: null });
      fixture.detectChanges();

      expect(internals().expiryChoice()).toEqual({ kind: 'none' });
      expect(chipBtn().textContent).toContain('No expiry');
      expect(saveBtn().disabled).toBe(false);
    });

    it('emits the edited row on Save, nulling a cleared provider', () => {
      type(providerEl(), '');
      saveBtn().click();
      expect(emitted).toEqual([{ provider: null, expiresOn: '2028-03-12', notes: 'motor only' }]);
    });

    it('emits cancelled on Escape', () => {
      providerEl().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', cancelable: true, bubbles: true }));
      expect(cancelled).toBe(1);
    });

    it('re-seeds only when the seed identity changes, never mid-edit on a re-render', () => {
      type(providerEl(), 'edited');
      fixture.componentRef.setInput('initial', { ...SEED });
      fixture.detectChanges();
      expect(providerEl().value).toBe('edited');

      fixture.componentRef.setInput('initial', { ...SEED, provider: 'Other' });
      fixture.detectChanges();
      expect(providerEl().value).toBe('Other');
    });
  });
});
