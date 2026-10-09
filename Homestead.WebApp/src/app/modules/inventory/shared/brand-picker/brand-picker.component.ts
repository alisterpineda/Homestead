import {
  Component,
  ChangeDetectionStrategy,
  effect,
  inject,
  input,
  output,
  signal,
  computed,
  untracked,
} from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, debounceTime, of, map, catchError, distinctUntilChanged } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronDown, lucideCheck, lucidePlus } from '@ng-icons/lucide';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { SearchInputComponent } from '@/shared/components/search-input/search-input.component';
import { PickerTriggerDirective } from '@/shared/directives/picker-trigger.directive';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { InventoryBrandApiService } from '@/api/services/inventory-brand-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { BrandResponse } from '@/api/generated/models/brand-response';

/**
 * The minimum a caller must supply to seed the picker's current value: identity only. Callers that
 * hold a full <see cref="BrandResponse"/> (list/search results) satisfy this structurally; item forms
 * that only know the attributed brand's id/name can seed it without the count/timestamps.
 */
export type BrandPickerValue = Pick<BrandResponse, 'id' | 'name'>;

@Component({
  selector: 'app-brand-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
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
        [attr.aria-label]="ariaLabel() || 'Select brand'"
        appPickerTrigger
      >
        @if (value(); as sel) {
          <span class="min-w-0 truncate">{{ sel.name }}</span>
        } @else {
          <span class="min-w-0 truncate text-muted-foreground">{{ placeholder() }}</span>
        }
        <ng-icon name="lucideChevronDown" class="text-[length:--spacing(4)] shrink-0 text-muted-foreground" />
      </button>
      <div hlmPopoverContent *hlmPopoverPortal class="w-[320px] p-0" align="start">
        <div class="p-2">
          <app-search-input [value]="searchTerm()" (valueChange)="onSearch($event)" placeholder="Search or add a brand..." ariaLabel="Search brands" />
        </div>
        <div class="max-h-[280px] overflow-y-auto p-1">
          @if (allowCreate()) {
            <button
              type="button"
              class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer min-w-0"
              (click)="onClear()"
            >
              <span class="size-4 shrink-0 flex items-center justify-center">
                @if (value() === null) {
                  <ng-icon name="lucideCheck" class="text-[length:--spacing(4)]" />
                }
              </span>
              <span class="text-muted-foreground">No brand</span>
            </button>
          }

          @if (loading()) {
            <div class="flex justify-center py-6">
              <hlm-spinner />
            </div>
          } @else {
            @for (b of results(); track b.id) {
              <button
                type="button"
                class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer min-w-0"
                (click)="onSelect(b)"
              >
                <span class="size-4 shrink-0 flex items-center justify-center">
                  @if (b.id === value()?.id) {
                    <ng-icon name="lucideCheck" class="text-[length:--spacing(4)]" />
                  }
                </span>
                <span class="truncate">{{ b.name }}</span>
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
                    <ng-icon name="lucidePlus" class="text-[length:--spacing(4)]" />
                  }
                </span>
                <span class="truncate">Create "{{ searchTerm().trim() }}"</span>
              </button>
            } @else if (results().length === 0) {
              <p class="py-4 text-center text-sm text-muted-foreground">No brands found.</p>
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
export class BrandPickerComponent {
  /** The currently-selected brand (identity only), or null when unattributed. Seeded by the parent form. */
  value = input<BrandPickerValue | null>(null);
  disabled = input(false);
  placeholder = input('Select brand');
  ariaLabel = input<string | null>(null);

  /**
   * When false, hides the "Create 'X'" and "No brand" rows — the picker becomes a pure chooser over
   * existing brands. The merge dialog opts out (you merge into an existing brand, never create/clear).
   * Defaults to true so the item forms' find-or-create behaviour is unchanged.
   */
  allowCreate = input(true);
  /** A brand id to omit from results — the merge dialog excludes the source so you can't merge into it. */
  excludeId = input<string | null>(null);
  /** Seeds the search box on first render (e.g. the rename-409 handoff pre-searches the attempted name). */
  initialSearch = input('');

  /** Emits the selected brand (identity only), or null when cleared. */
  valueChange = output<BrandPickerValue | null>();

  popoverState = signal<'open' | 'closed'>('closed');
  searchTerm = signal('');
  loading = signal(false);
  creating = signal(false);

  private brandApi = inject(InventoryBrandApiService);
  private errorHandler = inject(ErrorHandlerService);

  private readonly pageSize = 50;

  constructor() {
    // Seed the search box from initialSearch once, on first render. The value is set once by the
    // parent (the merge handoff) and never changes, so this runs a single time; user typing updates
    // searchTerm independently (untracked write, initialSearch is the only tracked dep).
    effect(() => {
      const seed = this.initialSearch();
      if (seed) untracked(() => this.searchTerm.set(seed));
    });
  }

  private response = toSignal(
    toObservable(this.searchTerm).pipe(
      debounceTime(200),
      distinctUntilChanged(),
      switchMap(term => {
        this.loading.set(true);
        return this.brandApi.getBrands({
          Search: term.trim() || undefined,
          Page: 1,
          PageSize: this.pageSize,
        }).pipe(
          map(r => ({ items: r.items ?? [], total: Number(r.totalCount ?? 0) })),
          catchError(() => of({ items: [] as BrandResponse[], total: 0 })),
        );
      }),
      map(r => {
        this.loading.set(false);
        return r;
      }),
    ),
    { initialValue: { items: [] as BrandResponse[], total: 0 } },
  );

  results = computed(() => {
    const exclude = this.excludeId();
    const items = this.response().items;
    return exclude ? items.filter(b => b.id !== exclude) : items;
  });
  truncated = computed(() => this.response().total > this.pageSize);

  // Offer the create row when the typed name doesn't already exist (case-insensitive) — the backend
  // is idempotent, but this keeps the UI honest and avoids an obvious no-op create. Suppressed when
  // allowCreate is false (the merge dialog's chooser).
  showCreateRow = computed(() => {
    if (!this.allowCreate()) return false;
    const text = this.searchTerm().trim();
    if (!text || this.loading()) return false;
    const lower = text.toLowerCase();
    return !this.results().some(b => b.name.toLowerCase() === lower);
  });

  onSelect(brand: BrandPickerValue): void {
    this.valueChange.emit({ id: brand.id, name: brand.name });
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
    this.brandApi.createBrand({ body: { name } }).subscribe({
      next: brand => {
        this.creating.set(false);
        this.onSelect(brand);
      },
      error: (err: unknown) => {
        this.creating.set(false);
        this.errorHandler.handleError(err, 'Failed to create brand.');
      },
    });
  }

  onSearch(value: string): void {
    this.searchTerm.set(value);
    // Flip the spinner on immediately. loading() is otherwise only set inside the debounced switchMap,
    // so during the ~200ms pre-debounce window results() is stale — long enough for the "Create 'X'"
    // row (gated on !loading) to flash for a brand that already exists. Eager loading suppresses that.
    this.loading.set(true);
  }
}
