import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
  computed,
  effect,
  viewChild,
  DOCUMENT,
} from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus } from '@ng-icons/lucide';
// Reach past the helm wrapper: BrnCombobox lets us reset the internal selection (bulk mode), BrnPopover
// exposes updatePosition()/close() — the host scroll containers aren't cdkScrollable, so we re-anchor the
// panel on scroll ourselves (mirrors app-payee-picker), and close it after an inline create.
import { BrnCombobox } from '@spartan-ng/brain/combobox';
import { BrnPopover } from '@spartan-ng/brain/popover';
import { HlmComboboxImports } from '@/shared/components/spartan/combobox';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { FinancesCategoryApiService } from '@/api/services/finances-category-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { CategoryResponse } from '@/api/generated/models/category-response';
import type { AccountType } from '@/api/generated/models/account-type';
import type { FlowDirection } from '@/api/generated/models/flow-direction';
import type { LineCategory } from '../../accounts/statement-import-review-cells';

/**
 * What a category pick resolves to. Category subsumes direction: a typed category *is* an Expense/Income,
 * and the two explicit Uncategorised entries carry a direction with no category. The host applies all three
 * fields at once (direction drives the amount sign + checksum; categoryId is what commits).
 */
export interface LineCategorySelection {
  direction: FlowDirection;
  categoryId: string | null;
  category: LineCategory | null;
}

/**
 * A single-select Category combobox for one statement-import line. Thin controlled wrapper over Spartan's
 * `hlm-combobox` (popup mode) — the parent owns the applied value, this only surfaces choices and emits a
 * pick. Item `[value]`s are string tokens (never raw guids) so the same category can appear in both its
 * direction group and the live Suggested shortcut without a value collision:
 *   `unc:Expense` / `unc:Income` — the explicit direction-only Uncategorised entries
 *   `cat:<guid>`                 — a real category, keyed once in its direction group
 *   `sug:<guid>`                 — the Suggested shortcut for the current payee's default (distinct token)
 * The resting `[value]` is canonical (`cat:` / `unc:`, never `sug:`) and bound one-way, so a transient
 * `sug:`→`cat:` correction can't trip ExpressionChangedAfterItHasBeenChecked under zoneless CD.
 */
@Component({
  selector: 'app-line-category-combobox',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmComboboxImports, NgIcon, HlmSpinner],
  providers: [provideIcons({ lucidePlus })],
  template: `
    <!-- Single-select popup combobox. closeOnSelect is the BrnCombobox config default (true) and isn't a
         rebindable input on HlmCombobox, so picking an item closes the panel + resets the search for free. -->
    <hlm-combobox
      [value]="value()"
      (valueChange)="onPick($event)"
      [(search)]="search"
      [isItemEqualToValue]="eq"
      [itemToString]="toLabel"
    >
      <hlm-combobox-trigger class="w-full min-w-[11rem]" [attr.aria-label]="ariaLabel()">
        <span class="min-w-0 flex-1 truncate text-start" [class.text-muted-foreground]="isPlaceholderLabel()">
          {{ triggerLabel() }}
        </span>
      </hlm-combobox-trigger>
      <hlm-combobox-content *hlmComboboxPortal>
        <hlm-combobox-input placeholder="Search categories…" />
        <hlm-combobox-empty>No categories found.</hlm-combobox-empty>
        <div hlmComboboxList>
          <!-- Suggested: the current payee's default, direction-filtered. Auto-hides when empty. -->
          <div hlmComboboxGroup>
            <span hlmComboboxLabel>Suggested</span>
            @if (suggested(); as s) {
              <hlm-combobox-item [value]="'sug:' + s.id">{{ s.name }}</hlm-combobox-item>
            }
          </div>
          <hlm-combobox-item value="unc:Expense">Uncategorised · Expense</hlm-combobox-item>
          <hlm-combobox-item value="unc:Income">Uncategorised · Income</hlm-combobox-item>
          <div hlmComboboxGroup>
            <span hlmComboboxLabel>Expense</span>
            @for (c of expenseCats(); track c.id) {
              <hlm-combobox-item [value]="'cat:' + c.id">{{ c.path }}</hlm-combobox-item>
            }
          </div>
          <div hlmComboboxGroup>
            <span hlmComboboxLabel>Income</span>
            @for (c of incomeCats(); track c.id) {
              <hlm-combobox-item [value]="'cat:' + c.id">{{ c.path }}</hlm-combobox-item>
            }
          </div>
          @if (showCreateRow()) {
            <button
              type="button"
              class="flex w-full cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-accent"
              [disabled]="creating()"
              (click)="onCreate()"
            >
              <span class="flex size-4 shrink-0 items-center justify-center">
                @if (creating()) {
                  <hlm-spinner class="size-3" />
                } @else {
                  <ng-icon name="lucidePlus" class="text-[length:--spacing(4)]" />
                }
              </span>
              <span class="truncate">Create {{ direction() }} category "{{ search().trim() }}"</span>
            </button>
          }
        </div>
      </hlm-combobox-content>
    </hlm-combobox>
  `,
})
export class LineCategoryComboboxComponent {
  /** The line's current direction. Source of truth stays the parse; a category pick *sets* it, never edits it alone. */
  direction = input<FlowDirection>('Expense');
  /** The applied category (what commits), or null for Uncategorised. Parent-owned — bound one-way into [value]. */
  category = input<LineCategory | null>(null);
  /** The applied payee's raw (un-filtered) default category — the live Suggested source. */
  payeeDefaultCategory = input<LineCategory | null>(null);
  /** Every category, both directions (loaded once by the page). Never fetched per row. */
  categories = input<CategoryResponse[]>([]);
  ariaLabel = input<string>('Select category');
  placeholder = input<string>('Uncategorised');
  allowCreate = input(true);
  /** Bulk mode: never rest on a value (no resting checkmark), always show the placeholder, emit on each pick. */
  resetAfterSelect = input(false);

  categoryChange = output<LineCategorySelection>();
  /**
   * A category just created inline. The host merges this into the shared `categories` list so the new
   * category is immediately reusable on OTHER rows (avoids a confusing 409 when the same name is re-created).
   */
  categoryCreated = output<CategoryResponse>();

  readonly search = signal('');
  readonly creating = signal(false);

  private readonly categoryApi = inject(FinancesCategoryApiService);
  private readonly errorHandler = inject(ErrorHandlerService);
  private readonly document = inject(DOCUMENT);

  // Host directives on the <hlm-combobox> element — queryable from here.
  private readonly combobox = viewChild(BrnCombobox);
  private readonly popover = viewChild(BrnPopover);

  constructor() {
    // The host scroll container (statement data panel) is a plain overflow-y-auto div, not cdkScrollable, so
    // CDK's reposition strategy never sees its scroll and the panel would drift. While open, re-anchor to the
    // trigger on any scroll (capture phase — scroll doesn't bubble), coalesced to one updatePosition/frame.
    let rafId = 0;
    const reposition = () => {
      if (rafId) return;
      rafId = requestAnimationFrame(() => {
        rafId = 0;
        this.popover()?.updatePosition();
      });
    };
    effect((onCleanup) => {
      if (!this.combobox()?.isExpanded()) return;
      const doc = this.document;
      doc.addEventListener('scroll', reposition, { capture: true, passive: true });
      onCleanup(() => {
        doc.removeEventListener('scroll', reposition, { capture: true });
        if (rafId) { cancelAnimationFrame(rafId); rafId = 0; }
      });
    });
  }

  // Resting token: canonical (never `sug:`), or undefined in bulk mode so nothing rests selected.
  readonly value = computed<string | undefined>(() => {
    if (this.resetAfterSelect()) return undefined;
    const c = this.category();
    return c ? `cat:${c.id}` : `unc:${this.direction()}`;
  });

  readonly triggerLabel = computed(() => {
    if (this.resetAfterSelect()) return this.placeholder();
    const c = this.category();
    return c ? c.name : `Uncategorised · ${this.direction()}`;
  });

  readonly isPlaceholderLabel = computed(() => this.resetAfterSelect() || !this.category());

  // The Suggested shortcut: the payee default filtered to the line's current direction (hidden otherwise).
  readonly suggested = computed<LineCategory | null>(() => {
    const d = this.payeeDefaultCategory();
    return d && d.type === this.direction() ? d : null;
  });

  // The two direction groups over all categories — system Uncategorised buckets excluded (the explicit
  // `unc:` entries replace them).
  readonly expenseCats = computed(() =>
    this.categories().filter((c) => !c.isSystem && c.type === 'Expense'));
  readonly incomeCats = computed(() =>
    this.categories().filter((c) => !c.isSystem && c.type === 'Income'));

  // Offer the create row when the typed name doesn't already exist in the current direction (case-insensitive).
  readonly showCreateRow = computed(() => {
    if (!this.allowCreate()) return false;
    const term = this.search().trim();
    if (!term) return false;
    const list = this.direction() === 'Expense' ? this.expenseCats() : this.incomeCats();
    const lower = term.toLowerCase();
    return !list.some((c) => c.name.toLowerCase() === lower);
  });

  // String-token equality (the resting value and every item [value] are tokens).
  readonly eq = (a: string, b: string | null | undefined): boolean => a === b;

  // Item label for the built-in contains filter (and any label display) — decode the token to searchable text.
  readonly toLabel = (token: string): string => {
    if (token === 'unc:Expense') return 'Uncategorised · Expense';
    if (token === 'unc:Income') return 'Uncategorised · Income';
    const id = this.tokenId(token);
    if (id) {
      const c = this.categories().find((x) => x.id === id);
      if (c) return c.path;
    }
    return token;
  };

  onPick(token: string | null | undefined): void {
    const sel = this.decode(token);
    if (!sel) return; // null/undefined → no deselect in single-select
    // Bulk mode: don't retain the pick, so no item stays checked when the shared control is reopened.
    if (this.resetAfterSelect()) this.combobox()?.resetValue();
    this.categoryChange.emit(sel);
  }

  onCreate(): void {
    const name = this.search().trim();
    if (!name || this.creating()) return;
    const type = this.direction() as AccountType;
    this.creating.set(true);
    this.categoryApi.createCategory({ body: { name, type, parentId: null } }).subscribe({
      next: (c) => {
        this.creating.set(false);
        this.search.set('');
        this.popover()?.close();
        this.combobox()?.resetValue();
        // Surface the new category first so the host adds it to the shared list, then apply it to this line.
        this.categoryCreated.emit(c);
        this.categoryChange.emit({
          direction: c.type as FlowDirection,
          categoryId: c.id,
          category: { id: c.id, name: c.name, type: c.type },
        });
      },
      error: (err: unknown) => {
        this.creating.set(false);
        this.errorHandler.handleError(err, 'Failed to create category.');
      },
    });
  }

  // Decode a token into a selection — category subsumes direction. A `cat:`/`sug:` guid is resolved against
  // the (category-only) catalog, so `c.type as FlowDirection` is always Expense/Income.
  private decode(token: string | null | undefined): LineCategorySelection | null {
    if (token === 'unc:Expense') return { direction: 'Expense', categoryId: null, category: null };
    if (token === 'unc:Income') return { direction: 'Income', categoryId: null, category: null };
    const id = this.tokenId(token);
    if (!id) return null;
    const c = this.categories().find((x) => x.id === id);
    if (!c) return null;
    return {
      direction: c.type as FlowDirection,
      categoryId: c.id,
      category: { id: c.id, name: c.name, type: c.type },
    };
  }

  private tokenId(token: string | null | undefined): string | null {
    if (typeof token !== 'string') return null;
    return token.startsWith('cat:') || token.startsWith('sug:') ? token.slice(4) : null;
  }
}
