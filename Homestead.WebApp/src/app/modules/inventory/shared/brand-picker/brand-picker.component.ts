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
import { lucideChevronDown, lucideSearch, lucideCheck, lucidePlus } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { InventoryBrandApiService } from '@/api/services/inventory-brand-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { BrandResponse } from '@/api/generated/models/brand-response';

@Component({
  selector: 'app-brand-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmPopoverImports,
    HlmInput,
    HlmSpinner,
  ],
  providers: [provideIcons({ lucideChevronDown, lucideSearch, lucideCheck, lucidePlus })],
  template: `
    <div hlmPopover [state]="popoverState()" (stateChanged)="popoverState.set($event)">
      <button
        hlmPopoverTrigger
        type="button"
        [disabled]="disabled()"
        [attr.aria-label]="ariaLabel() || 'Select brand'"
        class="flex h-9 w-full items-center justify-between rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-xs transition-colors disabled:cursor-not-allowed disabled:opacity-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        @if (value(); as sel) {
          <span class="truncate">{{ sel.name }}</span>
        } @else {
          <span class="truncate text-muted-foreground">{{ placeholder() }}</span>
        }
        <ng-icon hlm name="lucideChevronDown" size="sm" class="text-muted-foreground" />
      </button>
      <div hlmPopoverContent *hlmPopoverPortal class="w-[320px] p-0" align="start">
        <div class="relative p-2">
          <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-4 top-1/2 -translate-y-1/2 text-muted-foreground" />
          <input
            hlmInput
            class="h-8 w-full pl-8"
            placeholder="Search or add a brand..."
            [value]="searchTerm()"
            (input)="onSearch($event)"
            aria-label="Search brands"
          />
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
            <span class="text-muted-foreground">No brand</span>
          </button>

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
                    <ng-icon hlm name="lucideCheck" size="sm" />
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
                    <ng-icon hlm name="lucidePlus" size="sm" />
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
  /** The currently-selected brand, or null when unattributed. Seeded by the parent form. */
  value = input<BrandResponse | null>(null);
  disabled = input(false);
  placeholder = input('Select brand');
  ariaLabel = input<string | null>(null);

  /** Emits the selected brand ({ id, name }), or null when cleared. */
  valueChange = output<BrandResponse | null>();

  popoverState = signal<'open' | 'closed'>('closed');
  searchTerm = signal('');
  loading = signal(false);
  creating = signal(false);

  private brandApi = inject(InventoryBrandApiService);
  private errorHandler = inject(ErrorHandlerService);

  private readonly pageSize = 50;

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

  results = computed(() => this.response().items);
  truncated = computed(() => this.response().total > this.pageSize);

  // Offer the create row when the typed name doesn't already exist (case-insensitive) — the backend
  // is idempotent, but this keeps the UI honest and avoids an obvious no-op create.
  showCreateRow = computed(() => {
    const text = this.searchTerm().trim();
    if (!text || this.loading()) return false;
    const lower = text.toLowerCase();
    return !this.results().some(b => b.name.toLowerCase() === lower);
  });

  onSelect(brand: BrandResponse): void {
    this.valueChange.emit(brand);
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

  onSearch(event: Event): void {
    this.searchTerm.set(this.asValue(event));
    // Flip the spinner on immediately. loading() is otherwise only set inside the debounced switchMap,
    // so during the ~200ms pre-debounce window results() is stale — long enough for the "Create 'X'"
    // row (gated on !loading) to flash for a brand that already exists. Eager loading suppresses that.
    this.loading.set(true);
  }

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }
}
