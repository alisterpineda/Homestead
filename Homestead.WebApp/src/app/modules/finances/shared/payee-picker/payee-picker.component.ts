import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
  computed,
} from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, debounceTime, of, map, catchError, distinctUntilChanged } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronDown, lucideCheck, lucidePlus } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { SearchInputComponent } from '@/shared/components/search-input/search-input.component';
import { PickerTriggerDirective } from '@/shared/directives/picker-trigger.directive';
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

@Component({
  selector: 'app-payee-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmPopoverImports,
    SearchInputComponent,
    PickerTriggerDirective,
    HlmSpinner,
  ],
  providers: [provideIcons({ lucideChevronDown, lucideCheck, lucidePlus })],
  template: `
    <div hlmPopover [state]="popoverState()" (stateChanged)="popoverState.set($event)">
      <button
        hlmPopoverTrigger
        type="button"
        [disabled]="disabled()"
        [attr.aria-label]="ariaLabel() || 'Select payee'"
        appPickerTrigger
      >
        @if (value(); as sel) {
          <span class="min-w-0 truncate">{{ sel.name }}</span>
        } @else {
          <span class="min-w-0 truncate text-muted-foreground">{{ placeholder() }}</span>
        }
        <ng-icon hlm name="lucideChevronDown" size="sm" class="shrink-0 text-muted-foreground" />
      </button>
      <div hlmPopoverContent *hlmPopoverPortal class="w-[320px] p-0" align="start">
        <div class="p-2">
          <app-search-input [value]="searchTerm()" (valueChange)="onSearch($event)" placeholder="Search or add a payee..." ariaLabel="Search payees" />
        </div>
        <div class="max-h-[280px] overflow-y-auto p-1">
          <button
            type="button"
            class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer min-w-0"
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
                class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer min-w-0"
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
                class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer min-w-0"
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
                  class="flex w-full items-center gap-2 rounded-sm px-2 py-1 text-xs text-muted-foreground hover:bg-accent cursor-pointer min-w-0"
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
   * Emits the full selected payee (so the flow sheet can read defaultCategoryId/defaultCategoryType for
   * client-side autofill), or null when cleared.
   */
  valueChange = output<PayeeResponse | null>();

  popoverState = signal<'open' | 'closed'>('closed');
  searchTerm = signal('');
  loading = signal(false);
  creating = signal(false);

  private payeeApi = inject(FinancesPayeeApiService);
  private errorHandler = inject(ErrorHandlerService);

  private readonly pageSize = 50;

  private response = toSignal(
    toObservable(this.searchTerm).pipe(
      debounceTime(200),
      distinctUntilChanged(),
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
  }
}
