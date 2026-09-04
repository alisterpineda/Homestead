import {
  Component,
  ChangeDetectionStrategy,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
  ElementRef,
  LOCALE_ID,
} from '@angular/core';
import { formatDate } from '@angular/common';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCalendar, lucideSend } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmAutocompleteImports } from '@/shared/components/spartan/autocomplete';
import { BrnAutocomplete } from '@spartan-ng/brain/autocomplete';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { composeOccurredAt, decomposeOccurredAt } from '@/shared/utils/occurred-at';
import { toIsoDateString } from '@/shared/utils';
import { matchSubjects } from './subject-suggestions';

/** What the composer emits on submit. `occurredAt` null = "let the server stamp now". */
export interface NotePayload {
  subject: string | null;
  body: string;
  occurredAt: string | null;
}

/** Edit-mode seed: the note as it currently stands. */
export interface NoteSeed {
  subject: string | null;
  body: string;
  occurredAt: string;
}

const MAX_SUBJECT = 200;
const MAX_BODY = 4000;

/**
 * The note-entry form, used twice by {@link ItemActivityFeedComponent}: once at the head of the feed
 * for creating, and once in place of a row when that note is being edited.
 *
 * Three fields, stacked so nothing needs a width branch: a **Subject** line with per-item
 * autocomplete (free text lives in the autocomplete's `search` model; the parent supplies the
 * suggestions, brain does no filtering of its own), a **Body** textarea that grows with its content,
 * and a **date chip** that pins the entry to when it *happened*. Enter in the body submits and
 * Shift+Enter inserts a newline — the Documents composer gesture; Enter in the subject moves to the
 * body instead. The date is deliberately *sticky* across submits (the parent clears the text, not the
 * date) so back-filling a stack of receipts is one date change, not fifteen.
 */
@Component({
  selector: 'app-item-note-composer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmButton, HlmInput, HlmAutocompleteImports],
  providers: [provideIcons({ lucideCalendar, lucideSend })],
  template: `
    <div class="flex flex-col gap-2">
      <label class="sr-only" [attr.for]="subjectInputId">Subject</label>
      <hlm-autocomplete
        [search]="subject()"
        (searchChange)="onSubjectInput($event)"
        (valueChange)="onSubjectPicked($event)"
        [itemToString]="identity"
        [disabled]="disabled()"
      >
        <hlm-autocomplete-input
          [inputId]="subjectInputId"
          placeholder="Subject (optional)"
          [showSearch]="false"
          (keydown.enter)="focusBody()"
        />
        <hlm-autocomplete-content *hlmAutocompletePortal>
          @if (filteredSubjects().length) {
            <div hlmAutocompleteList>
              @for (s of filteredSubjects(); track s) {
                <hlm-autocomplete-item [value]="s">{{ s }}</hlm-autocomplete-item>
              }
            </div>
          }
        </hlm-autocomplete-content>
      </hlm-autocomplete>

      <label class="sr-only" [attr.for]="bodyInputId">Note</label>
      <!-- h-auto + field-sizing-content overrides hlmInput's fixed h-8 so the box grows with the
           note (one row to about five, then scrolls); min-h-8 keeps it flush with the Subject
           input above at rest, which content sizing alone leaves 2px short. -->
      <textarea
        #bodyInput
        hlmInput
        [id]="bodyInputId"
        rows="1"
        [attr.maxlength]="maxBody"
        class="h-auto max-h-32 min-h-8 w-full resize-none field-sizing-content"
        placeholder="Add a note…"
        [disabled]="disabled()"
        [value]="body()"
        (input)="onBodyInput($event)"
        (keydown)="onBodyKeydown($event)"
      ></textarea>

      <div class="flex items-center gap-2">
        <!-- relative: the native date input overlays the chip (transparent, click-through) so the
             browser anchors its picker to the chip rather than to some distant ancestor. -->
        <div class="relative inline-flex">
          <button
            hlmBtn
            variant="outline"
            size="sm"
            type="button"
            [disabled]="disabled()"
            [class.invisible]="pickerFallback()"
            [attr.aria-label]="'Date: ' + chipLabel()"
            (click)="openPicker()"
          >
            <ng-icon hlm name="lucideCalendar" size="sm" />
            {{ chipLabel() }}
          </button>
          <input
            #dateInput
            type="date"
            class="absolute inset-0 h-full w-full rounded-lg border-input bg-transparent px-2 text-base md:text-sm"
            [class.opacity-0]="!pickerFallback()"
            [class.pointer-events-none]="!pickerFallback()"
            [class.border]="pickerFallback()"
            [attr.aria-hidden]="pickerFallback() ? null : 'true'"
            [attr.tabindex]="pickerFallback() ? null : -1"
            [attr.aria-label]="pickerFallback() ? 'Date' : null"
            [disabled]="disabled()"
            [max]="todayIso()"
            [value]="dateIso() ?? ''"
            (change)="onDatePicked($event)"
          />
        </div>

        @if (mode() === 'edit') {
          <div class="ml-auto flex items-center gap-2">
            <button hlmBtn variant="ghost" size="sm" type="button" (click)="cancelled.emit()">Cancel</button>
            <button hlmBtn size="sm" type="button" [disabled]="!canSubmit()" (click)="submit()">
              {{ busy() ? 'Saving...' : 'Save' }}
            </button>
          </div>
        } @else {
          <button
            hlmBtn
            size="icon-sm"
            type="button"
            class="ml-auto"
            aria-label="Add note"
            [disabled]="!canSubmit()"
            (click)="submit()"
          >
            <ng-icon hlm name="lucideSend" size="sm" />
          </button>
        }
      </div>
    </div>
  `,
})
export class ItemNoteComposerComponent {
  private static _id = 0;
  private readonly uid = ItemNoteComposerComponent._id++;
  protected readonly subjectInputId = `note-subject-${this.uid}`;
  protected readonly bodyInputId = `note-body-${this.uid}`;
  protected readonly maxBody = MAX_BODY;

  private locale = inject(LOCALE_ID);

  mode = input<'create' | 'edit'>('create');
  /** The item's distinct prior subjects, most-recent first. Brain does no filtering — we do. */
  subjects = input<readonly string[]>([]);
  /** Edit seed. Re-seeds on a change of *content* (see `seedKey`), so the reference need not be stable. */
  initial = input<NoteSeed | null>(null);
  /** The header composer is disabled while a row edit is open — only one composer at a time. */
  disabled = input(false);
  busy = input(false);

  submitted = output<NotePayload>();
  cancelled = output<void>();

  private bodyEl = viewChild<ElementRef<HTMLTextAreaElement>>('bodyInput');
  /** The subject autocomplete's brain — reached to drop its latched selection on clear (see `clearText`). */
  private autocomplete = viewChild(BrnAutocomplete);
  private dateEl = viewChild<ElementRef<HTMLInputElement>>('dateInput');

  // Seeded on a primitive key derived from the seed's *content* (the app-wide `seedKey` convention),
  // so opening the editor on a different note re-seeds, while a re-render of the same note — even
  // through a fresh but equal object — never overwrites what the user is typing.
  private seedKey = computed(() => {
    const s = this.initial();
    return s ? JSON.stringify([s.subject, s.body, s.occurredAt]) : '';
  });
  protected subject = seededLinkedSignal(this.seedKey, () => this.initial()?.subject ?? '');
  protected body = seededLinkedSignal(this.seedKey, () => this.initial()?.body ?? '');
  /** null = untouched ("Today"). In edit mode it *displays* the note's date but stays untouched. */
  protected dateIso = seededLinkedSignal(this.seedKey, () => {
    const seed = this.initial();
    return seed ? decomposeOccurredAt(seed.occurredAt).date : null;
  });
  protected dateTouched = seededLinkedSignal(this.seedKey, () => false);

  // Today is read at construction and refreshed whenever the picker opens, so a tab left open
  // across midnight can't cap the picker at yesterday.
  protected todayIso = signal(toIsoDateString(new Date()) as string);
  /** Set when the browser has no `showPicker()`; the native control is then shown in place. */
  protected pickerFallback = signal(false);

  protected readonly identity = (v: string): string => v;

  protected canSubmit = computed(
    () => (this.subject().trim().length > 0 || this.body().trim().length > 0) && !this.busy() && !this.disabled(),
  );

  protected filteredSubjects = computed(() => matchSubjects(this.subjects(), this.subject()));

  protected chipLabel = computed(() => {
    const iso = this.dateIso();
    return iso ? formatDate(iso, 'mediumDate', this.locale) : 'Today';
  });

  /** Clear the text but keep the date — the sticky-date rule. Called by the feed after a create. */
  clearText(): void {
    // Brain latches the last picked value and its model only emits on a *change*, so without this
    // reset re-picking the same suggestion for the next note would be a silent no-op.
    this.autocomplete()?.resetValue();
    this.subject.set('');
    this.body.set('');
    this.focusBody();
  }

  focusBody(): void {
    this.bodyEl()?.nativeElement.focus();
  }

  protected onSubjectInput(value: string): void {
    this.subject.set(value.slice(0, MAX_SUBJECT));
  }

  protected onSubjectPicked(value: unknown): void {
    if (typeof value === 'string') this.subject.set(value.slice(0, MAX_SUBJECT));
  }

  protected onBodyInput(event: Event): void {
    this.body.set((event.target as HTMLTextAreaElement).value);
  }

  protected onBodyKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.submit();
      return;
    }
    if (event.key === 'Escape' && this.mode() === 'edit') {
      event.preventDefault();
      this.cancelled.emit();
    }
  }

  protected openPicker(): void {
    this.todayIso.set(toIsoDateString(new Date()) as string);
    const el = this.dateEl()?.nativeElement;
    if (!el) return;
    if (typeof el.showPicker === 'function') {
      el.showPicker();
    } else {
      // No showPicker (older Safari, jsdom): reveal the native control and focus it instead.
      this.pickerFallback.set(true);
      el.focus();
    }
  }

  protected onDatePicked(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.dateTouched.set(true);
    // `max` already blocks a future date in a real browser; belt-and-braces for a typed value.
    if (!value || value > this.todayIso()) {
      this.dateIso.set(value ? this.todayIso() : null);
      return;
    }
    this.dateIso.set(value);
  }

  protected submit(): void {
    if (!this.canSubmit()) return;
    const seed = this.initial();
    const picked = this.dateTouched() && this.dateIso() ? clampToNow(composeOccurredAt(this.dateIso() as string, null)) : null;
    // Edit with an untouched chip keeps the note's original instant verbatim, so a no-op save is a
    // genuine no-op server-side rather than silently pinning a full instant to noon.
    const occurredAt = this.mode() === 'edit' ? (picked ?? seed?.occurredAt ?? null) : picked;
    this.submitted.emit({
      subject: this.subject().trim().slice(0, MAX_SUBJECT) || null,
      body: this.body().trim().slice(0, MAX_BODY),
      occurredAt,
    });
  }
}

// A date-only pick composes to noon UTC on the *local* calendar date, which for today is a future
// instant everywhere east of UTC-12 before local noon-minus-offset. The server allows only a
// minute of skew (`NotInFuture`), so cap the instant at "now" — equal-format ISO strings compare
// lexicographically, and the stored value is what the server would have stamped anyway.
function clampToNow(iso: string): string {
  const now = new Date().toISOString();
  return iso > now ? now : iso;
}
