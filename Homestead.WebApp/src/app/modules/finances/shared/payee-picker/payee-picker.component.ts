import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
  computed,
  linkedSignal,
  viewChild,
  ElementRef,
  effect,
  afterNextRender,
  DOCUMENT,
} from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, debounceTime, of, map, catchError, filter } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronDown, lucideCheck, lucidePlus, lucideX } from '@ng-icons/lucide';
// Reach past the helm wrapper for the overlay's updatePosition() — HlmPopover doesn't re-expose it, and the
// picker's host scroll containers aren't cdkScrollable, so we re-anchor the panel on scroll ourselves.
import { BrnPopover } from '@spartan-ng/brain/popover';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmInputGroupImports } from '@/shared/components/spartan/input-group';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { FinancesPayeeApiService } from '@/api/services/finances-payee-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { PayeeResponse } from '@/api/generated/models/payee-response';

/**
 * The minimum a caller must supply to seed the picker's current value: identity only. Callers that
 * hold a full {@link PayeeResponse} (list/search results) satisfy this structurally; flow forms that
 * only know the attributed payee's id/name can seed it without the category/count/timestamps.
 */
export type PayeePickerValue = Pick<PayeeResponse, 'id' | 'name'>;

/**
 * Flat, visual-order model of the *selectable* rows, so the keyboard highlight (`activeIndex`) and
 * `aria-activedescendant` map 1:1 to what renders. Non-selectable chrome (spinner, "No payees found",
 * "Refine your search") is deliberately NOT part of this list.
 */
type PickerRow =
  | { kind: 'none'; id: string }
  | { kind: 'payee'; id: string; payee: PayeeResponse }
  | { kind: 'create'; id: string }
  | { kind: 'dym'; id: string; payee: PayeeResponse };

// Per-instance seed for listbox/option ids — a11y wiring needs stable, unique ids across concurrent pickers.
let payeePickerSeq = 0;

@Component({
  selector: 'app-payee-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmPopoverImports, HlmInputGroupImports, HlmSpinner],
  providers: [provideIcons({ lucideChevronDown, lucideCheck, lucidePlus, lucideX })],
  template: `
    <div
      hlmPopover
      [state]="popoverState()"
      (stateChanged)="onStateChanged($event)"
      [attachTo]="anchor"
      [autoFocus]="false"
      align="start"
      [sideOffset]="4"
    >
      <!-- Input IS the search box: an input-first combobox, not a trigger button. Closed → shows the
           committed payee's name; open → shows the live query (see displayText). -->
      <hlm-input-group #anchor>
        <input
          #inputEl
          hlmInputGroupInput
          type="text"
          role="combobox"
          autocomplete="off"
          aria-autocomplete="list"
          aria-haspopup="listbox"
          [attr.aria-expanded]="popoverState() === 'open'"
          [attr.aria-controls]="listboxId"
          [attr.aria-activedescendant]="activeDescendant()"
          [attr.aria-label]="ariaLabel() || 'Select payee'"
          [placeholder]="placeholder()"
          [value]="displayText()"
          [disabled]="disabled()"
          (focus)="onFocus($event)"
          (input)="onSearch(asValue($event))"
          (click)="onInputClick()"
          (keydown)="onKeydown($event)"
        />
        <hlm-input-group-addon align="inline-end">
          @if (value()) {
            <button
              hlmInputGroupButton
              size="icon-xs"
              type="button"
              tabindex="-1"
              [disabled]="disabled()"
              aria-label="Clear payee"
              (mousedown)="$event.preventDefault()"
              (click)="onClear()"
            >
              <ng-icon hlm name="lucideX" size="sm" class="text-muted-foreground" />
            </button>
          }
          <button
            hlmInputGroupButton
            size="icon-xs"
            type="button"
            tabindex="-1"
            [disabled]="disabled()"
            aria-label="Toggle payee list"
            (mousedown)="$event.preventDefault()"
            (click)="onChevron()"
          >
            <ng-icon
              hlm
              name="lucideChevronDown"
              size="sm"
              class="text-muted-foreground transition-transform"
              [class.rotate-180]="popoverState() === 'open'"
            />
          </button>
        </hlm-input-group-addon>
      </hlm-input-group>

      <div
        hlmPopoverContent
        *hlmPopoverPortal
        class="w-[320px] p-0"
        role="listbox"
        [id]="listboxId"
        [attr.aria-label]="ariaLabel() || 'Payees'"
        (mousedown)="$event.preventDefault()"
      >
        <div class="max-h-[280px] overflow-y-auto p-1">
          <!-- No payee: always visible (even while results load), so clearing is one keystroke away. -->
          <button
            type="button"
            role="option"
            [id]="noneOptionId"
            [attr.aria-selected]="value() === null"
            class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer min-w-0"
            [class.bg-accent]="activeDescendant() === noneOptionId"
            (click)="onClear()"
          >
            <span class="size-4 shrink-0 flex items-center justify-center">
              @if (value() === null) {
                <ng-icon hlm name="lucideCheck" size="sm" />
              }
            </span>
            <span class="text-muted-foreground">No payee</span>
          </button>

          @if (loading()) {
            <div class="flex justify-center py-6">
              <hlm-spinner />
            </div>
          } @else {
            @for (p of results(); track p.id) {
              <button
                type="button"
                role="option"
                [id]="payeeOptionId(p)"
                [attr.aria-selected]="p.id === value()?.id"
                class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer min-w-0"
                [class.bg-accent]="activeDescendant() === payeeOptionId(p)"
                (click)="onSelect(p)"
              >
                <span class="size-4 shrink-0 flex items-center justify-center">
                  @if (p.id === value()?.id) {
                    <ng-icon hlm name="lucideCheck" size="sm" />
                  }
                </span>
                <span class="truncate">{{ p.name }}</span>
              </button>
            }
            @if (showCreateRow()) {
              <button
                type="button"
                role="option"
                [id]="createOptionId"
                [attr.aria-selected]="false"
                class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer min-w-0"
                [class.bg-accent]="activeDescendant() === createOptionId"
                [disabled]="creating()"
                (click)="onCreate()"
              >
                <span class="size-4 shrink-0 flex items-center justify-center">
                  @if (creating()) {
                    <hlm-spinner class="size-3" />
                  } @else {
                    <ng-icon hlm name="lucidePlus" size="sm" />
                  }
                </span>
                <span class="truncate">Create "{{ searchTerm().trim() }}"</span>
              </button>
              <!-- Dedupe nudge: a near-duplicate already exists (case-insensitive substring). Surface it so
                   AI-named lookalikes ("Blue Bottle" vs "Blue Bottle Coffee") get caught before minting. -->
              @if (didYouMean(); as dym) {
                <button
                  type="button"
                  role="option"
                  [id]="dymOptionId"
                  [attr.aria-selected]="false"
                  class="flex w-full items-center gap-2 rounded-sm px-2 py-1 text-xs text-muted-foreground hover:bg-accent cursor-pointer min-w-0"
                  [class.bg-accent]="activeDescendant() === dymOptionId"
                  (click)="onSelect(dym)"
                >
                  <span class="size-4 shrink-0"></span>
                  <span class="truncate">Did you mean <strong class="font-medium text-foreground">{{ dym.name }}</strong>?</span>
                </button>
              }
            } @else if (results().length === 0) {
              <p class="py-4 text-center text-sm text-muted-foreground">No payees found.</p>
            }
            @if (truncated()) {
              <p class="py-2 text-center text-xs text-muted-foreground">Refine your search to see more.</p>
            }
          }
        </div>
      </div>
    </div>
  `,
})
export class PayeePickerComponent {
  /** The currently-selected payee (identity only), or null when unattributed. Seeded by the parent form. */
  value = input<PayeePickerValue | null>(null);
  disabled = input(false);
  placeholder = input('Select payee');
  ariaLabel = input<string | null>(null);

  /**
   * Opt-in: mount the picker already open, so a host can swap it in as an inline "override" of some other
   * affordance (the statement-import AI-suggestion cell). Acts once, on first render. Off for every normal
   * host, which keeps the closed-until-clicked behavior.
   */
  autoOpen = input(false);
  /** The query to seed the search box with when {@link autoOpen} opens it (e.g. an AI-suggested name). */
  initialQuery = input<string | null>(null);

  /**
   * Emits the full selected payee (so the flow sheet can read defaultCategoryId/defaultCategoryType for
   * client-side autofill), or null when cleared.
   */
  valueChange = output<PayeeResponse | null>();

  /**
   * Fires whenever the panel closes (including a dismiss without a pick). Lets a host distinguish "the
   * reviewer backed out" from "the reviewer chose something" — the statement-import cell uses it to restore
   * its suggestion chip when an override is abandoned.
   */
  closed = output<void>();

  popoverState = signal<'open' | 'closed'>('closed');
  searchTerm = signal('');
  loading = signal(false);
  creating = signal(false);

  private inputEl = viewChild.required<ElementRef<HTMLInputElement>>('inputEl');
  // The BrnPopover host directive on the root <div hlmPopover> — queried so we can reposition its overlay.
  private popover = viewChild(BrnPopover);

  private document = inject(DOCUMENT);
  private payeeApi = inject(FinancesPayeeApiService);
  private errorHandler = inject(ErrorHandlerService);

  constructor() {
    // The host scroll containers (statement data panel, flow sheet, merge dialog) are plain overflow-y-auto
    // divs — not cdkScrollable — so CDK's reposition strategy never sees their scroll and the panel would
    // float detached. While open, re-anchor the panel to the input on any scroll (capture phase, because
    // scroll events don't bubble) so it follows the field instead of drifting off. Coalesced to one
    // updatePosition per frame so rapid scroll doesn't thrash layout (getBoundingClientRect + reflow).
    let rafId = 0;
    const reposition = () => {
      if (rafId) return;
      rafId = requestAnimationFrame(() => {
        rafId = 0;
        this.popover()?.updatePosition();
      });
    };
    effect((onCleanup) => {
      if (this.popoverState() !== 'open') return;
      const doc = this.document;
      doc.addEventListener('scroll', reposition, { capture: true, passive: true });
      onCleanup(() => {
        doc.removeEventListener('scroll', reposition, { capture: true });
        if (rafId) { cancelAnimationFrame(rafId); rafId = 0; }
      });
    });

    // Override entry point: a host that mounts us with [autoOpen] wants the picker live and pre-seeded the
    // instant it appears. Runs once after first render (the input can't change afterward without a remount).
    afterNextRender(() => {
      if (!this.autoOpen() || this.disabled()) return;
      const seed = this.initialQuery()?.trim() || undefined;
      this.openPicker(seed);
      const el = this.inputEl().nativeElement;
      el.focus();
      // openPicker sets searchTerm (→ the [value] binding), but that only reaches the DOM on the next change
      // detection — so at this point the field is still empty. Reflect the seed imperatively before select(),
      // otherwise it selects nothing and the first keystroke wouldn't replace the suggested name. The binding
      // re-affirms the same value on the next CD, so there's no desync.
      if (seed) {
        el.value = seed;
        el.select();
      }
    });
  }

  private readonly pageSize = 50;

  // Stable a11y ids for the listbox and each option (unique per picker instance).
  readonly listboxId = `payee-picker-listbox-${payeePickerSeq++}`;
  protected readonly noneOptionId = `${this.listboxId}-none`;
  protected readonly createOptionId = `${this.listboxId}-create`;
  protected readonly dymOptionId = `${this.listboxId}-dym`;
  protected payeeOptionId(p: PayeeResponse): string {
    return `${this.listboxId}-opt-${p.id}`;
  }

  // Dual-mode input text: the committed payee's name when closed, the live query while open.
  displayText = computed(() =>
    this.popoverState() === 'open' ? this.searchTerm() : (this.value()?.name ?? ''));

  // Gate the debounced fetch on the picker being open: while closed, the source is null and nothing
  // downstream fires — so resetting searchTerm='' on close (and the initial render) never fetches.
  private searchSource = computed(() =>
    this.popoverState() === 'open' ? this.searchTerm() : null);

  private response = toSignal(
    toObservable(this.searchSource).pipe(
      filter((t): t is string => t !== null),
      debounceTime(200),
      // No distinctUntilChanged here: toObservable already emits only when searchSource *changes*, and a
      // persistent distinct would treat reopening with the same term (e.g. the empty query) as a duplicate
      // and suppress the fetch — stranding the eager spinner openPicker turns on (nothing would flip
      // loading back off). A rare redundant fetch for an unchanged term is a fine price for never sticking.
      switchMap(term => {
        this.loading.set(true);
        // Archived payees are excluded for free (the server hides them unless IncludeArchived=true).
        return this.payeeApi.getPayees({
          Search: term.trim() || undefined,
          Page: 1,
          PageSize: this.pageSize,
        }).pipe(
          map(r => ({ items: r.items ?? [], total: Number(r.totalCount ?? 0) })),
          catchError(() => of({ items: [] as PayeeResponse[], total: 0 })),
        );
      }),
      map(r => {
        this.loading.set(false);
        return r;
      }),
    ),
    { initialValue: { items: [] as PayeeResponse[], total: 0 } },
  );

  results = computed(() => this.response().items);
  truncated = computed(() => this.response().total > this.pageSize);

  // Offer the create row when the typed name doesn't already exist (case-insensitive) — the backend
  // 409s on a duplicate, so this keeps the UI honest and avoids an obvious no-op create.
  showCreateRow = computed(() => {
    const text = this.searchTerm().trim();
    if (!text || this.loading()) return false;
    const lower = text.toLowerCase();
    return !this.results().some(p => p.name.toLowerCase() === lower);
  });

  // A near-duplicate of the typed name (case-insensitive substring either way, but not an exact match) —
  // the "did you mean …?" nudge shown beneath the create row.
  didYouMean = computed(() => {
    if (!this.showCreateRow()) return null;
    const lower = this.searchTerm().trim().toLowerCase();
    if (!lower) return null;
    return this.results().find(p => {
      const name = p.name.toLowerCase();
      return name !== lower && (name.includes(lower) || lower.includes(name));
    }) ?? null;
  });

  // The selectable rows in visual order. Reference changes whenever the row *set* changes, which is what
  // resets the highlight (activeIndex) below.
  private rows = computed<PickerRow[]>(() => {
    const out: PickerRow[] = [{ kind: 'none', id: this.noneOptionId }];
    for (const p of this.results()) out.push({ kind: 'payee', id: this.payeeOptionId(p), payee: p });
    if (this.showCreateRow()) {
      out.push({ kind: 'create', id: this.createOptionId });
      const dym = this.didYouMean();
      if (dym) out.push({ kind: 'dym', id: this.dymOptionId, payee: dym });
    }
    return out;
  });

  // Keyboard highlight index into rows(); -1 = nothing highlighted. Re-seeds to -1 whenever the row set
  // OR the open/closed state changes — so a highlight can't survive a close and reappear (then get
  // Enter-selected, or leave a dangling aria-activedescendant) on the next open.
  protected activeIndex = linkedSignal({
    source: () => [this.popoverState(), this.rows()] as const,
    computation: () => -1,
  });

  protected activeDescendant = computed(() => {
    const i = this.activeIndex();
    return i >= 0 ? (this.rows()[i]?.id ?? null) : null;
  });

  // Focus only *selects* the committed name (so the first keystroke replaces it) — it must NOT open the
  // picker. The overlay refocuses this input when it closes; opening on that restore-focus would reopen the
  // picker the instant an outside click dismisses it. Opening is driven by click/tap, typing, and ArrowDown.
  protected onFocus(event: FocusEvent): void {
    if (this.value() && this.popoverState() === 'closed') (event.target as HTMLInputElement).select();
  }

  // A tap/click opens (focus has already run and selected any committed name). Closed → open only.
  protected onInputClick(): void {
    if (this.popoverState() === 'closed') this.openPicker();
  }

  protected onChevron(): void {
    if (this.disabled()) return;
    // mousedown is prevented on the button, so focus never left the input; toggle explicitly.
    if (this.popoverState() === 'open') {
      this.closePicker();
      return;
    }
    // Focus the input for typing (fires onFocus→openPicker when it was unfocused), then open explicitly —
    // re-focusing an already-focused input dispatches no focus event, so we can't rely on onFocus here.
    this.inputEl().nativeElement.focus();
    this.openPicker();
  }

  // Opens seeded with `seed` when given (the override path), else with the committed name.
  private openPicker(seed?: string): void {
    if (this.disabled()) return;
    this.searchTerm.set(seed ?? this.value()?.name ?? '');
    // Eager loading suppresses a create-row flash during the pre-debounce window (see onSearch).
    this.loading.set(true);
    this.popoverState.set('open');
  }

  private closePicker(): void {
    this.popoverState.set('closed');
    this.searchTerm.set('');
    this.closed.emit();
  }

  // Outside-pointer dismissal (and any overlay-driven state change) lands here.
  protected onStateChanged(state: 'open' | 'closed'): void {
    this.popoverState.set(state);
    if (state === 'closed') {
      this.searchTerm.set('');
      this.closed.emit();
    }
  }

  protected onKeydown(event: KeyboardEvent): void {
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (this.popoverState() === 'closed') this.openPicker();
        else this.moveActive(1);
        return;
      case 'ArrowUp':
        if (this.popoverState() === 'closed') return;
        event.preventDefault();
        this.moveActive(-1);
        return;
      case 'Enter':
        if (this.popoverState() === 'closed') return;
        event.preventDefault(); // don't submit an enclosing form
        this.activateActive();
        return;
      case 'Escape':
        if (this.popoverState() === 'open') {
          // Swallow so an enclosing dialog's Escape doesn't also fire.
          event.preventDefault();
          event.stopPropagation();
          this.closePicker();
        }
        return;
      case 'Tab':
        // Let focus advance; just close the panel (no preventDefault).
        this.closePicker();
        return;
    }
  }

  private moveActive(delta: number): void {
    const max = this.rows().length - 1;
    if (max < 0) return;
    const cur = this.activeIndex();
    const next = cur < 0 ? (delta > 0 ? 0 : max) : Math.min(max, Math.max(0, cur + delta));
    this.activeIndex.set(next);
  }

  private activateActive(): void {
    const i = this.activeIndex();
    const row = i >= 0 ? this.rows()[i] : undefined;
    if (row) {
      switch (row.kind) {
        case 'none': this.onClear(); return;
        case 'payee': this.onSelect(row.payee); return;
        case 'create': this.onCreate(); return;
        case 'dym': this.onSelect(row.payee); return;
      }
    }
    // Nothing highlighted: prefer Enter-to-create, else commit the sole result.
    if (this.showCreateRow()) { this.onCreate(); return; }
    const items = this.results();
    if (items.length === 1) this.onSelect(items[0]);
  }

  onSelect(payee: PayeeResponse): void {
    this.valueChange.emit(payee);
    this.searchTerm.set('');
    this.popoverState.set('closed');
  }

  onClear(): void {
    this.valueChange.emit(null);
    this.searchTerm.set('');
    this.popoverState.set('closed');
  }

  onCreate(): void {
    const name = this.searchTerm().trim();
    if (!name || this.creating()) return;

    this.creating.set(true);
    this.payeeApi.createPayee({ body: { name } }).subscribe({
      next: payee => {
        this.creating.set(false);
        this.onSelect(payee);
      },
      error: (err: unknown) => {
        this.creating.set(false);
        this.errorHandler.handleError(err, 'Failed to create payee.');
      },
    });
  }

  onSearch(value: string): void {
    this.searchTerm.set(value);
    // Flip the spinner on immediately. loading() is otherwise only set inside the debounced switchMap,
    // so during the ~200ms pre-debounce window results() is stale — long enough for the "Create 'X'"
    // row (gated on !loading) to flash for a payee that already exists. Eager loading suppresses that.
    this.loading.set(true);
    if (this.popoverState() === 'closed') this.popoverState.set('open');
  }

  protected asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }
}
