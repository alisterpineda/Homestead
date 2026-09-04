import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { BrnAutocomplete } from '@spartan-ng/brain/autocomplete';
import {
  ItemNoteComposerComponent,
  type NotePayload,
  type NoteSeed,
} from './item-note-composer.component';

// The autocomplete anchors its popover with ResizeObserver.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}

function todayIso(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

describe('ItemNoteComposerComponent', () => {
  let fixture: ComponentFixture<ItemNoteComposerComponent>;
  let emitted: NotePayload[];
  let cancelled: number;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    fixture = TestBed.createComponent(ItemNoteComposerComponent);
    emitted = [];
    cancelled = 0;
    fixture.componentInstance.submitted.subscribe((p) => emitted.push(p));
    fixture.componentInstance.cancelled.subscribe(() => cancelled++);
    fixture.detectChanges();
  });

  const host = () => fixture.nativeElement as HTMLElement;
  const bodyEl = () => host().querySelector('textarea')!;
  const dateEl = () => host().querySelector<HTMLInputElement>('input[type="date"]')!;
  const sendBtn = () => host().querySelector<HTMLButtonElement>('button[aria-label="Add note"]')!;
  const chipBtn = () => host().querySelector<HTMLButtonElement>('button[aria-label^="Date:"]')!;

  function type(value: string): void {
    const el = bodyEl();
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

  function setSubject(value: string): void {
    // The subject lives in the autocomplete's `search` model; drive it through the same handler the
    // template binds to rather than through the overlay.
    (fixture.componentInstance as unknown as { onSubjectInput(v: string): void }).onSubjectInput(value);
    fixture.detectChanges();
  }

  it('submits on Enter in the body and inserts a newline on Shift+Enter', () => {
    type('Changed the oil');

    bodyEl().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true, bubbles: true }));
    expect(emitted).toHaveLength(1);
    expect(emitted[0].body).toBe('Changed the oil');

    const shift = new KeyboardEvent('keydown', { key: 'Enter', shiftKey: true, cancelable: true, bubbles: true });
    bodyEl().dispatchEvent(shift);
    expect(emitted).toHaveLength(1); // no second submit
    expect(shift.defaultPrevented).toBe(false); // the browser inserts the newline
  });

  it('disables send until either the subject or the body has content', () => {
    expect(sendBtn().disabled).toBe(true);

    setSubject('Oil change');
    expect(sendBtn().disabled).toBe(false);

    setSubject('');
    expect(sendBtn().disabled).toBe(true);

    type('  ');
    expect(sendBtn().disabled).toBe(true);
    type('anything');
    expect(sendBtn().disabled).toBe(false);
  });

  it('sends a null occurredAt while the date chip is untouched, and reads "Today"', () => {
    expect(chipBtn().textContent).toContain('Today');
    type('note');
    sendBtn().click();
    expect(emitted[0].occurredAt).toBeNull();
  });

  it('pins an explicitly picked date to noon UTC', () => {
    type('note');
    pickDate('2026-03-04');
    sendBtn().click();
    expect(emitted[0].occurredAt).toBe('2026-03-04T12:00:00.000Z');
  });

  it('keeps the picked date across clearText (sticky) while dropping the text', () => {
    setSubject('Oil change');
    type('12k service');
    pickDate('2026-03-04');

    fixture.componentInstance.clearText();
    fixture.detectChanges();

    expect(bodyEl().value).toBe('');
    expect(chipBtn().textContent).not.toContain('Today');

    type('next one');
    sendBtn().click();
    expect(emitted.at(-1)).toMatchObject({ subject: null, body: 'next one', occurredAt: '2026-03-04T12:00:00.000Z' });
  });

  it('caps the hidden date input at today so a future date cannot be picked', () => {
    expect(dateEl().getAttribute('max')).toBe(todayIso());
  });

  it('trims the subject and body, and sends a blank subject as null', () => {
    setSubject('  Oil change  ');
    type('  did it  ');
    sendBtn().click();
    expect(emitted[0]).toMatchObject({ subject: 'Oil change', body: 'did it' });
  });

  describe('edit mode', () => {
    const SEED: NoteSeed = { subject: 'Oil change', body: 'At Midas', occurredAt: '2019-06-14T09:15:00.000Z' };

    beforeEach(() => {
      fixture.componentRef.setInput('mode', 'edit');
      fixture.componentRef.setInput('initial', SEED);
      fixture.detectChanges();
    });

    it('seeds subject, body, and the chip date from the note', () => {
      expect(bodyEl().value).toBe('At Midas');
      expect(dateEl().value).toBe('2019-06-14');
      expect(chipBtn().textContent).not.toContain('Today');
    });

    it('sends the original occurredAt verbatim when the chip is untouched', () => {
      host().querySelectorAll<HTMLButtonElement>('button').forEach((b) => {
        if (b.textContent?.trim() === 'Save') b.click();
      });
      expect(emitted[0].occurredAt).toBe('2019-06-14T09:15:00.000Z');
    });

    it('re-pins to noon UTC once the chip is touched', () => {
      pickDate('2019-06-15');
      host().querySelectorAll<HTMLButtonElement>('button').forEach((b) => {
        if (b.textContent?.trim() === 'Save') b.click();
      });
      expect(emitted[0].occurredAt).toBe('2019-06-15T12:00:00.000Z');
    });

    it('emits cancelled on Escape in the body', () => {
      bodyEl().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', cancelable: true, bubbles: true }));
      expect(cancelled).toBe(1);
    });

    it('re-seeds only when the seed identity changes, never mid-edit on a re-render', () => {
      type('edited text');
      fixture.detectChanges();
      // Same seed object re-applied (a feed refresh landing underneath) must not clobber the edit.
      fixture.componentRef.setInput('initial', SEED);
      fixture.detectChanges();
      expect(bodyEl().value).toBe('edited text');

      fixture.componentRef.setInput('initial', { ...SEED, body: 'Other note' });
      fixture.detectChanges();
      expect(bodyEl().value).toBe('Other note');
    });
  });

  it('never emits a future instant for today: the noon-UTC composition is capped at now', () => {
    pickDate(todayIso());
    type('x');
    sendBtn().click();

    const at = emitted[0].occurredAt;
    expect(at).not.toBeNull();
    // The server allows one minute of skew; equal-format ISO strings compare lexicographically.
    expect(at! <= new Date().toISOString()).toBe(true);
  });

  describe('subject suggestions', () => {
    beforeEach(() => {
      fixture.componentRef.setInput('subjects', [
        'Oil change', 'Oil filter', 'Tyres', 'Brakes', 'Battery',
        'Coolant', 'Wipers', 'Air filter', 'Spark plugs',
      ]);
      fixture.detectChanges();
    });

    const suggestions = () =>
      (fixture.componentInstance as unknown as { filteredSubjects(): readonly string[] }).filteredSubjects();

    it('caps the unfiltered list at 8', () => {
      expect(suggestions()).toHaveLength(8);
    });

    it('filters case-insensitively on the typed text', () => {
      setSubject('oil');
      expect(suggestions()).toEqual(['Oil change', 'Oil filter']);
    });

    it('still sets the subject when the same suggestion is re-picked after clearText', () => {
      // Drive brain's own select(): its model only emits on a change, so a latched pick must be
      // reset by clearText or the second pick is a silent no-op (the back-filling flow).
      const brain = fixture.debugElement.query(By.css('hlm-autocomplete')).injector.get(BrnAutocomplete);
      brain.select('Tyres');
      fixture.detectChanges();
      type('first');
      sendBtn().click();
      expect(emitted[0].subject).toBe('Tyres');

      fixture.componentInstance.clearText();
      fixture.detectChanges();
      brain.select('Tyres');
      fixture.detectChanges();
      type('second');
      sendBtn().click();
      expect(emitted[1].subject).toBe('Tyres');
    });

    it('sets the subject when a suggestion is picked', () => {
      (fixture.componentInstance as unknown as { onSubjectPicked(v: unknown): void }).onSubjectPicked('Tyres');
      fixture.detectChanges();
      type('x');
      sendBtn().click();
      expect(emitted[0].subject).toBe('Tyres');
    });
  });
});
