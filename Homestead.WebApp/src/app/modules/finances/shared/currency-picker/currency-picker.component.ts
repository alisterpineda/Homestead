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
import { lucideChevronDown, lucideSearch, lucideCheck } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

@Component({
  selector: 'app-currency-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmPopoverImports,
    HlmInput,
    HlmSpinner,
  ],
  providers: [provideIcons({ lucideChevronDown, lucideSearch, lucideCheck })],
  template: `
    <div hlmPopover [state]="popoverState()" (stateChanged)="popoverState.set($event)">
      <button
        hlmPopoverTrigger
        type="button"
        [disabled]="disabled()"
        [attr.aria-label]="ariaLabel() || 'Select currency'"
        class="flex h-9 w-full items-center justify-between rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-xs transition-colors disabled:cursor-not-allowed disabled:opacity-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        @if (selected(); as sel) {
          <span class="min-w-0 truncate">{{ sel.symbol }} — {{ sel.name }}</span>
        } @else {
          <span class="min-w-0 truncate text-muted-foreground">{{ placeholder() }}</span>
        }
        <ng-icon hlm name="lucideChevronDown" size="sm" class="shrink-0 text-muted-foreground" />
      </button>
      <div hlmPopoverContent *hlmPopoverPortal class="w-[320px] p-0" align="start">
        <div class="relative p-2">
          <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-4 top-1/2 -translate-y-1/2 text-muted-foreground" />
          <input
            hlmInput
            class="h-8 w-full pl-8"
            placeholder="Search currencies..."
            [value]="searchTerm()"
            (input)="searchTerm.set(asValue($event))"
            aria-label="Search currencies"
          />
        </div>
        <div class="max-h-[280px] overflow-y-auto p-1">
          @if (loading()) {
            <div class="flex justify-center py-6">
              <hlm-spinner />
            </div>
          } @else if (results().length === 0) {
            <p class="py-4 text-center text-sm text-muted-foreground">No currencies found.</p>
          } @else {
            @for (c of results(); track c.id) {
              <button
                type="button"
                class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent cursor-pointer min-w-0"
                (click)="onSelect(c)"
              >
                <span class="size-4 shrink-0 flex items-center justify-center">
                  @if (c.id === value()) {
                    <ng-icon hlm name="lucideCheck" size="sm" />
                  }
                </span>
                <span class="font-medium">{{ c.symbol }}</span>
                <span class="truncate text-muted-foreground">{{ c.name }}</span>
              </button>
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
export class CurrencyPickerComponent {
  value = input<string | null>(null);
  disabled = input(false);
  placeholder = input('Select currency');
  ariaLabel = input<string | null>(null);
  selectedCommodity = input<CommodityResponse | null>(null);

  valueChange = output<string>();
  commodityChange = output<CommodityResponse>();

  popoverState = signal<'open' | 'closed'>('closed');
  searchTerm = signal('');
  loading = signal(false);

  private commodityApi = inject(FinancesCommodityApiService);

  private readonly pageSize = 50;

  private response = toSignal(
    toObservable(this.searchTerm).pipe(
      debounceTime(200),
      distinctUntilChanged(),
      switchMap(term => {
        this.loading.set(true);
        return this.commodityApi.getCommodities({
          Kind: 'Currency',
          Search: term || undefined,
          Page: 1,
          PageSize: this.pageSize,
        }).pipe(
          map(r => ({ items: r.items ?? [], total: Number(r.totalCount ?? 0) })),
          catchError(() => of({ items: [] as CommodityResponse[], total: 0 })),
        );
      }),
      map(r => {
        this.loading.set(false);
        return r;
      }),
    ),
    { initialValue: { items: [] as CommodityResponse[], total: 0 } },
  );

  results = computed(() => this.response().items);
  truncated = computed(() => this.response().total > this.pageSize);
  selected = computed(() => {
    const sel = this.selectedCommodity();
    if (sel && sel.id === this.value()) return sel;
    return this.results().find(c => c.id === this.value()) ?? null;
  });

  onSelect(commodity: CommodityResponse): void {
    this.valueChange.emit(commodity.id);
    this.commodityChange.emit(commodity);
    this.popoverState.set('closed');
  }

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }
}
