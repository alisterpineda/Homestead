import {
  Component,
  ChangeDetectionStrategy,
  Injector,
  afterNextRender,
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
import { lucideCalendar, lucideMessageSquareText, lucideSend } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInputGroupImports } from '@/shared/components/spartan/input-group';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { toIsoDateString } from '@/shared/utils';
import { EXPIRY_PRESETS, addYearsIso, presetLabel } from './warranty-expiry';

/** What the composer emits on submit. Every field is nullable — an all-null row is a valid warranty. */
export interface WarrantyPayload {
  /** null = the manufacturer (resolved to the item's brand at display time, never stored). */
  provider: string | null;
  /** `yyyy-MM-dd`, or null for an open-ended / lifetime warranty. */
  expiresOn: string | null;
  notes: string | null;
}

/** Edit-mode seed: the warranty as it currently stands. */
export interface WarrantySeed {
  provider: string | null;
  expiresOn: string | null;
  notes: string | null;
}

/**
 * The expiry is an explicit three-way choice, never an implied default. `unset` is the create-mode
 * resting state and the only one that blocks submit: "no expiry" and "expires on a date" are
 * opposite claims about the warranty, and guessing either from silence writes wrong data.
 */
export type ExpiryChoice = { kind: 'unset' } | { kind: 'none' } | { kind: 'date'; iso: string };

const MAX_PROVIDER = 200;
const MAX_NOTES = 500;

/**
 * The warranty-entry line, used twice by {@link ItemWarrantiesSectionComponent}: once at the head of
 * the section for creating, and once in place of a row when that warranty is being edited.
 *
 * Shaped like the note composer — a chip, one input, an inline send button, Enter submits — but with
 * the chip's meaning inverted. A note's date chip has a sensible default ("Today") and is *sticky*
 * across submits; an expiry has neither. Add stays disabled until the chip is set, and a successful
 * create resets it back to unset, because a stale expiry carried into the next warranty would
 * silently record a wrong date rather than merely an unhelpful one.
 *
 * Provider and notes never gate submit: the manufacturer is the assumed provider (hence the
 * "Manufacturer" placeholder rather than a pre-filled value), and notes are revealed on demand for
 * the tier case ("motor only") that most rows don't need.
 */
@Component({
  selector: 'app-item-warranty-composer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmButton, HlmInputGroupImports, HlmDropdownMenuImports],
  providers: [provideIcons({ lucideCalendar, lucideMessageSquareText, lucideSend })],
  template: `
    <!-- flex-wrap carries the notes line onto its own row, and in edit mode lets Cancel / Save drop
         below at narrow widths; the provider input keeps a basis so it never collapses beside the chip. -->
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
          [class.text-muted-foreground]="expiryChoice().kind === 'unset'"
          [attr.aria-label]="'Expiry: ' + chipLabel()"
          [hlmDropdownMenuTrigger]="expiryMenu"
          (click)="refreshToday()"
        >
          <ng-icon hlm name="lucideCalendar" size="sm" />
          {{ chipLabel() }}
        </button>
        <ng-template #expiryMenu>
          <hlm-dropdown-menu class="w-56">
            <hlm-dropdown-menu-group>
              @for (preset of presetOptions(); track preset.years) {
                <button hlmDropdownMenuItem (triggered)="chooseDate(preset.iso)">{{ preset.label }}</button>
              }
            </hlm-dropdown-menu-group>
            <hlm-dropdown-menu-separator />
            <hlm-dropdown-menu-group>
              <button hlmDropdownMenuItem (triggered)="chooseNoExpiry()">No expiry</button>
              <button hlmDropdownMenuItem (triggered)="openPicker()">Pick a date...</button>
            </hlm-dropdown-menu-group>
          </hlm-dropdown-menu>
        </ng-template>
        <!-- No max attribute: back-filling a warranty that has already lapsed is legitimate. -->
        <input
          #dateInput
          type="date"
          class="absolute inset-0 h-full w-full rounded-lg border-input bg-transparent px-2 text-base md:text-sm"
          [class.opacity-0]="!pickerFallback()"
          [class.pointer-events-none]="!pickerFallback()"
          [class.border]="pickerFallback()"
          [attr.aria-hidden]="pickerFallback() ? null : 'true'"
          [attr.tabindex]="pickerFallback() ? null : -1"
          [attr.aria-label]="pickerFallback() ? 'Expiry date' : null"
          [disabled]="disabled()"
          [value]="pickedIso()"
          (change)="onDatePicked($event)"
        />
      </div>

      <label class="sr-only" [attr.for]="providerInputId">Warranty provider</label>
      <div hlmInputGroup class="min-w-0 flex-1 basis-40">
        <input
          #providerInput
          hlmInputGroupInput
          [id]="providerInputId"
          [attr.maxlength]="maxProvider"
          placeholder="Manufacturer"
          [disabled]="disabled()"
          [value]="provider()"
          (input)="onProviderInput($event)"
          (keydown)="onKeydown($event)"
        />
        <hlm-input-group-addon align="inline-end">
          @if (!notesShown()) {
            <button
              hlmInputGroupButton
              size="icon-xs"
              type="button"
              aria-label="Add warranty note"
              [disabled]="disabled()"
              (click)="revealNotes()"
            >
              <ng-icon hlm name="lucideMessageSquareText" size="sm" />
            </button>
          }
          @if (mode() === 'create') {
            <button
              hlmInputGroupButton
              size="icon-xs"
              type="button"
              aria-label="Add warranty"
              [disabled]="!canSubmit()"
              (click)="submit()"
            >
              <ng-icon hlm name="lucideSend" size="sm" />
            </button>
          }
        </hlm-input-group-addon>
      </div>

      @if (mode() === 'edit') {
        <div class="ml-auto flex items-center gap-2">
          <button hlmBtn variant="ghost" size="sm" type="button" (click)="cancelled.emit()">Cancel</button>
          <button hlmBtn size="sm" type="button" [disabled]="!canSubmit()" (click)="submit()">
            {{ busy() ? 'Saving...' : 'Save' }}
          </button>
        </div>
      }

      @if (notesShown()) {
        <label class="sr-only" [attr.for]="notesInputId">Warranty note</label>
        <div hlmInputGroup class="min-w-0 basis-full">
          <input
            #notesInput
            hlmInputGroupInput
            [id]="notesInputId"
            [attr.maxlength]="maxNotes"
            placeholder="Note (e.g. motor only)"
            [disabled]="disabled()"
            [value]="notes()"
            (input)="onNotesInput($event)"
            (keydown)="onKeydown($event)"
          />
        </div>
      }
    </div>
  `,
})
export class ItemWarrantyComposerComponent {
  private static _id = 0;
  private readonly uid = ItemWarrantyComposerComponent._id++;
  protected readonly providerInputId = `warranty-provider-${this.uid}`;
  protected readonly notesInputId = `warranty-notes-${this.uid}`;
  protected readonly maxProvider = MAX_PROVIDER;
  protected readonly maxNotes = MAX_NOTES;

  private locale = inject(LOCALE_ID);
  private injector = inject(Injector);

  mode = input<'create' | 'edit'>('create');
  /** Edit seed. Re-seeds on a change of *content* (see `seedKey`), so the reference need not be stable. */
  initial = input<WarrantySeed | null>(null);
  /** The item's acquired-on date — the base the year presets count from; null falls back to today. */
  acquiredOn = input<string | null>(null);
  /** The header composer is disabled while a row edit is open — only one composer at a time. */
  disabled = input(false);
  busy = input(false);

  submitted = output<WarrantyPayload>();
  cancelled = output<void>();

  private providerEl = viewChild<ElementRef<HTMLInputElement>>('providerInput');
  private notesEl = viewChild<ElementRef<HTMLInputElement>>('notesInput');
  private dateEl = viewChild<ElementRef<HTMLInputElement>>('dateInput');

  // Seeded on a primitive key derived from the seed's *content* (the app-wide `seedKey` convention),
  // so opening the editor on a different warranty re-seeds, while a re-render of the same warranty —
  // even through a fresh but equal object — never overwrites what the user is typing.
  private seedKey = computed(() => {
    const s = this.initial();
    return s ? JSON.stringify([s.provider, s.expiresOn, s.notes]) : '';
  });
  protected provider = seededLinkedSignal(this.seedKey, () => this.initial()?.provider ?? '');
  protected notes = seededLinkedSignal(this.seedKey, () => this.initial()?.notes ?? '');
  /** Notes are hidden until asked for — except in edit mode, where an existing note must be visible. */
  protected notesShown = seededLinkedSignal(this.seedKey, () => !!this.initial()?.notes);
  /**
   * Editing an existing warranty always seeds a *made* choice (the row already committed to a date
   * or to open-ended), so `unset` is reachable only in create mode.
   */
  protected expiryChoice = seededLinkedSignal<string, ExpiryChoice>(this.seedKey, () => {
    const seed = this.initial();
    if (!seed) return { kind: 'unset' };
    return seed.expiresOn ? { kind: 'date', iso: seed.expiresOn.slice(0, 10) } : { kind: 'none' };
  });

  // Read at construction and refreshed whenever the chip is clicked (before its menu renders) or
  // the native picker opens, so a tab left open across midnight can't offer presets counted from
  // yesterday.
  private todayIso = signal(toIsoDateString(new Date()) as string);
  /** Set when the browser has no `showPicker()`; the native control is then shown in place. */
  protected pickerFallback = signal(false);

  /** Expiry is the *only* gate: provider and notes are both legitimately empty. */
  protected canSubmit = computed(
    () => this.expiryChoice().kind !== 'unset' && !this.busy() && !this.disabled(),
  );

  protected chipLabel = computed(() => {
    const choice = this.expiryChoice();
    if (choice.kind === 'unset') return 'Expires';
    if (choice.kind === 'none') return 'No expiry';
    return formatDate(choice.iso, 'mediumDate', this.locale);
  });

  /** The native input's value — only a picked date shows there. */
  protected pickedIso = computed(() => {
    const choice = this.expiryChoice();
    return choice.kind === 'date' ? choice.iso : '';
  });

  /**
   * The four one-tap lengths, counted from the item's acquired-on date when known (a warranty runs
   * from purchase, not from the day someone got round to recording it) and from today otherwise.
   */
  protected presetOptions = computed(() => {
    const base = this.acquiredOn()?.slice(0, 10) || this.todayIso();
    return EXPIRY_PRESETS.map((years) => {
      const iso = addYearsIso(base, years);
      return { years, iso, label: presetLabel(years, iso, this.locale) };
    });
  });

  /**
   * Clear the entry fields **and the expiry chip** — the deliberate opposite of the note composer's
   * sticky date. Called by the section after a create.
   */
  clearText(): void {
    this.provider.set('');
    this.notes.set('');
    this.notesShown.set(false);
    this.expiryChoice.set({ kind: 'unset' });
    // The in-place native control was a fallback for *that* pick; give the chip (and with it "No
    // expiry" and the presets) back for the next entry.
    this.pickerFallback.set(false);
    this.focusProvider();
  }

  focusProvider(): void {
    this.providerEl()?.nativeElement.focus();
  }

  protected onProviderInput(event: Event): void {
    this.provider.set((event.target as HTMLInputElement).value);
  }

  protected onNotesInput(event: Event): void {
    this.notes.set((event.target as HTMLInputElement).value);
  }

  protected revealNotes(): void {
    this.notesShown.set(true);
    // The input doesn't exist until the @if re-renders, so focus after that render, not on a bare
    // microtask (which zoneless CD makes no ordering promise about).
    afterNextRender(() => this.notesEl()?.nativeElement.focus(), { injector: this.injector });
  }

  protected onKeydown(event: KeyboardEvent): void {
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

  protected chooseDate(iso: string): void {
    this.expiryChoice.set({ kind: 'date', iso });
  }

  protected chooseNoExpiry(): void {
    this.expiryChoice.set({ kind: 'none' });
  }

  protected refreshToday(): void {
    this.todayIso.set(toIsoDateString(new Date()) as string);
  }

  protected openPicker(): void {
    this.refreshToday();
    const el = this.dateEl()?.nativeElement;
    if (!el) return;
    if (typeof el.showPicker === 'function') {
      try {
        el.showPicker();
        return;
      } catch {
        // `showPicker()` needs user activation, and this call arrives from a menu item whose
        // overlay is closing — if the browser judges the gesture spent, fall through to the
        // in-place control rather than leaving the chip inert.
      }
    }
    // No showPicker (older Safari, jsdom), or it was refused: reveal the native control instead.
    this.pickerFallback.set(true);
    el.focus();
  }

  protected onDatePicked(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    // Clearing the native control is a retraction, not a claim of "no expiry" — back to unset.
    this.expiryChoice.set(value ? { kind: 'date', iso: value } : { kind: 'unset' });
  }

  protected submit(): void {
    if (!this.canSubmit()) return;
    const choice = this.expiryChoice();
    this.submitted.emit({
      provider: normalize(this.provider(), MAX_PROVIDER),
      expiresOn: choice.kind === 'date' ? choice.iso : null,
      notes: normalize(this.notes(), MAX_NOTES),
    });
  }
}

/** Trim, cap, and collapse an empty string to null — the client half of the server's normalisation. */
function normalize(value: string, max: number): string | null {
  const trimmed = value.trim().slice(0, max);
  return trimmed.length > 0 ? trimmed : null;
}
