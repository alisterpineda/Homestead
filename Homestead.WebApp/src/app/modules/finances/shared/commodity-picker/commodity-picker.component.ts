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
import { combineLatest, switchMap, debounceTime, of, map, tap, catchError, distinctUntilChanged, filter, forkJoin } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronDown, lucideSearch, lucideCheck } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { FinancesCommodityApiService } from '@/api/services/finances-commodity-api.service';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import type { CommodityKind } from '@/api/generated/models/commodity-kind';

@Component({
  selector: 'app-commodity-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, HlmPopoverImports, HlmInput, HlmSpinner],
  providers: [provideIcons({ lucideChevronDown, lucideSearch, lucideCheck })],
  template: `
    <div hlmPopover [state]="popoverState()" (stateChanged)="popoverState.set($event)">
      <button
        hlmPopoverTrigger
        type="button"
        [disabled]="disabled()"
        [attr.aria-label]="ariaLabel() || 'Select commodity'"
        class="flex h-9 w-full items-center justify-between rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-xs transition-colors disabled:cursor-not-allowed disabled:opacity-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        @if (selected(); as sel) {
          <span class="min-w-0 truncate">
            <span class="font-medium">{{ sel.symbol }}</span>
            @if (sel.exchange) {
              <span class="text-muted-foreground"> · {{ sel.exchange }}</span>
            }
            <span class="text-muted-foreground"> — {{ sel.name }}</span>
          </span>
        } @else {
          <span class="min-w-0 truncate text-muted-foreground">{{ placeholder() }}</span>
        }
        <ng-icon hlm name="lucideChevronDown" size="sm" class="shrink-0 text-muted-foreground" />
      </button>
      <div hlmPopoverContent *hlmPopoverPortal class="w-[360px] p-0" align="start">
        <div class="relative p-2">
          <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-4 top-1/2 -translate-y-1/2 text-muted-foreground" />
          <input
            hlmInput
            class="h-8 w-full pl-8"
            [placeholder]="searchPlaceholder()"
            [value]="searchTerm()"
            (input)="searchTerm.set(asValue($event))"
            [attr.aria-label]="searchPlaceholder()"
          />
        </div>
        <div class="max-h-[280px] overflow-y-auto p-1">
          @if (loading()) {
            <div class="flex justify-center py-6">
              <hlm-spinner />
            </div>
          } @else if (filtered().length === 0) {
            <p class="py-4 text-center text-sm text-muted-foreground">No commodities found.</p>
          } @else {
            @for (c of filtered(); track c.id) {
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
                <span class="font-medium shrink-0">{{ c.symbol }}</span>
                @if (c.exchange) {
                  <span class="text-muted-foreground shrink-0">· {{ c.exchange }}</span>
                }
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
export class CommodityPickerComponent {
  value = input<string | null>(null);
  disabled = input(false);
  placeholder = input('Select commodity');
  ariaLabel = input<string | null>(null);
  selectedCommodity = input<CommodityResponse | null>(null);
  kind = input<CommodityKind | null>(null);
  excludeIds = input<readonly string[]>([]);

  commodityChange = output<CommodityResponse>();

  popoverState = signal<'open' | 'closed'>('closed');
  searchTerm = signal('');
  loading = signal(false);

  private commodityApi = inject(FinancesCommodityApiService);

  private readonly pageSize = 50;

  // Defer the first fetch until the popover is opened so unopened pickers
  // (e.g. inside a closed sheet) don't issue HTTP at construction time.
  private response = toSignal(
    combineLatest([
      toObservable(this.popoverState).pipe(filter(s => s === 'open')),
      toObservable(this.searchTerm).pipe(debounceTime(200), distinctUntilChanged()),
      toObservable(this.kind).pipe(distinctUntilChanged()),
    ]).pipe(
      tap(() => this.loading.set(true)),
      switchMap(([, term, k]) => {
        const search = term || undefined;
        if (k !== null) {
          return this.commodityApi.getCommodities({
            Kind: k,
            Search: search,
            Page: 1,
            PageSize: this.pageSize,
          }).pipe(
            map(r => ({ items: r.items ?? [], total: Number(r.totalCount ?? 0) })),
            catchError(() => of({ items: [] as CommodityResponse[], total: 0 })),
          );
        }
        return forkJoin({
          curr: this.commodityApi.getCommodities({ Kind: 'Currency', Search: search, Page: 1, PageSize: this.pageSize }),
          sec: this.commodityApi.getCommodities({ Kind: 'Security', Search: search, Page: 1, PageSize: this.pageSize }),
        }).pipe(
          map(({ curr, sec }) => ({
            items: [...(curr.items ?? []), ...(sec.items ?? [])],
            total: Number(curr.totalCount ?? 0) + Number(sec.totalCount ?? 0),
          })),
          catchError(() => of({ items: [] as CommodityResponse[], total: 0 })),
        );
      }),
      tap(() => this.loading.set(false)),
    ),
    { initialValue: { items: [] as CommodityResponse[], total: 0 } },
  );

  filtered = computed(() => {
    const exclude = new Set(this.excludeIds());
    return this.response().items.filter(c => !exclude.has(c.id));
  });

  truncated = computed(() => this.response().total > this.response().items.length);

  selected = computed(() => {
    const sel = this.selectedCommodity();
    if (sel && sel.id === this.value()) return sel;
    return this.response().items.find(c => c.id === this.value()) ?? null;
  });

  searchPlaceholder = computed(() => {
    const k = this.kind();
    if (k === 'Currency') return 'Search currencies...';
    if (k === 'Security') return 'Search securities...';
    return 'Search commodities...';
  });

  onSelect(commodity: CommodityResponse): void {
    this.commodityChange.emit(commodity);
    this.popoverState.set('closed');
  }

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }
}
