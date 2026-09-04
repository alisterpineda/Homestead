import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import {
  ItemNoteComposerComponent,
  type NotePayload,
  type NoteSeed,
} from './item-note-composer.component';

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
  const bodyEl = () => host().querySelector<HTMLInputElement>('input[hlmInputGroupInput]')!;
  const dateEl = () => host().querySelector<HTMLInputElement>('input[type="date"]')!;
  const sendBtn = () => host().querySelector<HTMLButtonElement>('button[aria-label="Add note"]')!;
  const chipBtn = () => host().querySelector<HTMLButtonElement>('button[aria-label^="Date:"]')!;
  const saveBtn = () =>
    Array.from(host().querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent?.trim() === 'Save')!;

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

  it('is a single-line input: submits on Enter', () => {
    type('Changed the oil');

    const enter = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true, bubbles: true });
    bodyEl().dispatchEvent(enter);
    expect(enter.defaultPrevented).toBe(true);
    expect(emitted).toHaveLength(1);
    expect(emitted[0].body).toBe('Changed the oil');
  });

  it('disables send until the body has content', () => {
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
    type('12k service');
    pickDate('2026-03-04');

    fixture.componentInstance.clearText();
    fixture.detectChanges();

    expect(bodyEl().value).toBe('');
    expect(chipBtn().textContent).not.toContain('Today');

    type('next one');
    sendBtn().click();
    expect(emitted.at(-1)).toEqual({ body: 'next one', occurredAt: '2026-03-04T12:00:00.000Z' });
  });

  it('caps the hidden date input at today so a future date cannot be picked', () => {
    expect(dateEl().getAttribute('max')).toBe(todayIso());
  });

  it('caps the input at 4000 characters and trims the body', () => {
    expect(bodyEl().getAttribute('maxlength')).toBe('4000');
    type('  did it  ');
    sendBtn().click();
    expect(emitted[0].body).toBe('did it');
  });

  describe('edit mode', () => {
    const SEED: NoteSeed = { body: 'At Midas', occurredAt: '2019-06-14T09:15:00.000Z' };

    beforeEach(() => {
      fixture.componentRef.setInput('mode', 'edit');
      fixture.componentRef.setInput('initial', SEED);
      fixture.detectChanges();
    });

    it('seeds the body and the chip date from the note, and swaps send for Cancel / Save', () => {
      expect(bodyEl().value).toBe('At Midas');
      expect(dateEl().value).toBe('2019-06-14');
      expect(chipBtn().textContent).not.toContain('Today');
      expect(host().querySelector('button[aria-label="Add note"]')).toBeNull();
      expect(saveBtn()).toBeDefined();
    });

    it('joins a legacy multi-line body with spaces, so the single-line input shows what Save sends', () => {
      fixture.componentRef.setInput('initial', { ...SEED, body: 'Oil change\n5W-30, 4 quarts' });
      fixture.detectChanges();
      expect(bodyEl().value).toBe('Oil change 5W-30, 4 quarts');

      saveBtn().click();
      expect(emitted[0].body).toBe('Oil change 5W-30, 4 quarts');
    });

    it('sends the original occurredAt verbatim when the chip is untouched', () => {
      saveBtn().click();
      expect(emitted[0].occurredAt).toBe('2019-06-14T09:15:00.000Z');
    });

    it('re-pins to noon UTC once the chip is touched', () => {
      pickDate('2019-06-15');
      saveBtn().click();
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
});
