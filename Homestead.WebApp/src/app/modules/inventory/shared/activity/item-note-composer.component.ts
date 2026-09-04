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
import { HlmInputGroupImports } from '@/shared/components/spartan/input-group';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { composeOccurredAt, decomposeOccurredAt } from '@/shared/utils/occurred-at';
import { toIsoDateString } from '@/shared/utils';

/** What the composer emits on submit. `occurredAt` null = "let the server stamp now". */
export interface NotePayload {
  body: string;
  occurredAt: string | null;
}

/** Edit-mode seed: the note as it currently stands. */
export interface NoteSeed {
  body: string;
  occurredAt: string;
}

const MAX_BODY = 4000;

/**
 * The note-entry line, used twice by {@link ItemActivityFeedComponent}: once at the head of the feed
 * for creating, and once in place of a row when that note is being edited.
 *
 * The Documents comment composer — one input with a send button, Enter submits — plus a **date chip**
 * ahead of it that pins the entry to when it *happened*. The date is deliberately *sticky* across
 * submits (the parent clears the text, not the date) so back-filling a stack of receipts is one date
 * change, not fifteen. In edit mode the send button gives way to Cancel / Save, and Escape cancels.
 */
@Component({
  selector: 'app-item-note-composer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmButton, HlmInputGroupImports],
  providers: [provideIcons({ lucideCalendar, lucideSend })],
  template: `
    <!-- flex-wrap only matters in edit mode, where Cancel / Save can drop to their own line at
         narrow widths; the input keeps a basis so it never collapses beside the chip. -->
    <div class="flex flex-wrap items-center gap-2">
      <!-- relative: the native date input overlays the chip (transparent, click-through) so the
           browser anchors its picker to the chip rather than to some distant ancestor. -->
      <div class="relative inline-flex shrink-0">
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

      <label class="sr-only" [attr.for]="bodyInputId">Note</label>
      <div hlmInputGroup class="min-w-0 flex-1 basis-40">
        <input
          #bodyInput
          hlmInputGroupInput
          [id]="bodyInputId"
          [attr.maxlength]="maxBody"
          placeholder="Add a note..."
          [disabled]="disabled()"
          [value]="body()"
          (input)="onBodyInput($event)"
          (keydown)="onBodyKeydown($event)"
        />
        @if (mode() === 'create') {
          <hlm-input-group-addon align="inline-end">
            <button
              hlmInputGroupButton
              size="icon-xs"
              type="button"
              aria-label="Add note"
              [disabled]="!canSubmit()"
              (click)="submit()"
            >
              <ng-icon hlm name="lucideSend" size="sm" />
            </button>
          </hlm-input-group-addon>
        }
      </div>

      @if (mode() === 'edit') {
        <div class="ml-auto flex items-center gap-2">
          <button hlmBtn variant="ghost" size="sm" type="button" (click)="cancelled.emit()">Cancel</button>
          <button hlmBtn size="sm" type="button" [disabled]="!canSubmit()" (click)="submit()">
            {{ busy() ? 'Saving...' : 'Save' }}
          </button>
        </div>
      }
    </div>
  `,
})
export class ItemNoteComposerComponent {
  private static _id = 0;
  private readonly uid = ItemNoteComposerComponent._id++;
  protected readonly bodyInputId = `note-body-${this.uid}`;
  protected readonly maxBody = MAX_BODY;

  private locale = inject(LOCALE_ID);

  mode = input<'create' | 'edit'>('create');
  /** Edit seed. Re-seeds on a change of *content* (see `seedKey`), so the reference need not be stable. */
  initial = input<NoteSeed | null>(null);
  /** The header composer is disabled while a row edit is open — only one composer at a time. */
  disabled = input(false);
  busy = input(false);

  submitted = output<NotePayload>();
  cancelled = output<void>();

  private bodyEl = viewChild<ElementRef<HTMLInputElement>>('bodyInput');
  private dateEl = viewChild<ElementRef<HTMLInputElement>>('dateInput');

  // Seeded on a primitive key derived from the seed's *content* (the app-wide `seedKey` convention),
  // so opening the editor on a different note re-seeds, while a re-render of the same note — even
  // through a fresh but equal object — never overwrites what the user is typing.
  private seedKey = computed(() => {
    const s = this.initial();
    return s ? JSON.stringify([s.body, s.occurredAt]) : '';
  });
  protected body = seededLinkedSignal(this.seedKey, () => toSingleLine(this.initial()?.body ?? ''));
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

  protected canSubmit = computed(() => this.body().trim().length > 0 && !this.busy() && !this.disabled());

  protected chipLabel = computed(() => {
    const iso = this.dateIso();
    return iso ? formatDate(iso, 'mediumDate', this.locale) : 'Today';
  });

  /** Clear the text but keep the date — the sticky-date rule. Called by the feed after a create. */
  clearText(): void {
    this.body.set('');
    this.focusBody();
  }

  focusBody(): void {
    this.bodyEl()?.nativeElement.focus();
  }

  protected onBodyInput(event: Event): void {
    this.body.set((event.target as HTMLInputElement).value);
  }

  protected onBodyKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
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
      body: this.body().trim().slice(0, MAX_BODY),
      occurredAt,
    });
  }
}

/**
 * The body is one line. A legacy multi-line note (typed under the old textarea composer) is joined
 * with spaces on seed, because a text `<input>` silently *strips* line breaks — left alone, the user
 * would see the lines fused together ("Oil change5W-30") and Save would persist exactly that.
 */
function toSingleLine(text: string): string {
  return text.replace(/\s*\r?\n\s*/g, ' ');
}

// A date-only pick composes to noon UTC on the *local* calendar date, which for today is a future
// instant everywhere east of UTC-12 before local noon-minus-offset. The server allows only a
// minute of skew (`NotInFuture`), so cap the instant at "now" — equal-format ISO strings compare
// lexicographically, and the stored value is what the server would have stamped anyway.
function clampToNow(iso: string): string {
  const now = new Date().toISOString();
  return iso > now ? now : iso;
}
