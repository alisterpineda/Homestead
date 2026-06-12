import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  LOCALE_ID,
} from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { formatDate } from '@angular/common';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, of, catchError } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft,
  lucidePencil,
  lucidePlus,
  lucideCircleAlert,
  lucideTriangleAlert,
  lucideArrowRight,
  lucideMinus,
  lucideChevronLeft,
  lucideChevronRight,
  lucideBoxes,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { InventorySupplyApiService } from '@/api/services/inventory-supply-api.service';
import { ItemStatusBadgeComponent } from '../shared/item-status-badge.component';
import { EditSupplySheetComponent } from './edit-supply-sheet/edit-supply-sheet.component';
import {
  StockOpSheetComponent,
  type StockOpMode,
  type StockOpLocation,
} from './stock-op-sheet/stock-op-sheet.component';
import { TrackItemSheetComponent } from './track-item-sheet/track-item-sheet.component';
import { CollapseItemsDialogComponent } from './collapse-items-dialog/collapse-items-dialog.component';
import { formatQty, movementDirection, isWholeCount } from './supply-shared';
import type { SupplyResponse } from '@/api/generated/models/supply-response';
import type { SupplyDetailResponse } from '@/api/generated/models/supply-detail-response';
import type { StockEntryResponse } from '@/api/generated/models/stock-entry-response';
import type { StockMovementResponse } from '@/api/generated/models/stock-movement-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

@Component({
  selector: 'app-supply-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmEmptyImports,
    HlmTableImports,
    ItemStatusBadgeComponent,
    EditSupplySheetComponent,
    StockOpSheetComponent,
    TrackItemSheetComponent,
    CollapseItemsDialogComponent,
  ],
  providers: [provideIcons({
    lucideArrowLeft,
    lucidePencil,
    lucidePlus,
    lucideCircleAlert,
    lucideTriangleAlert,
    lucideArrowRight,
    lucideMinus,
    lucideChevronLeft,
    lucideChevronRight,
    lucideBoxes,
  })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <div class="flex min-h-0 flex-1 flex-col gap-6 p-4 md:p-6">
      <a routerLink="/inventory/items" class="flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
        <ng-icon hlm name="lucideArrowLeft" size="sm" />
        All items
      </a>

      @if (loading() && !detail()) {
        <div class="flex justify-center py-16"><hlm-spinner /></div>
      } @else if (notFound()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCircleAlert" /></div>
            <div hlmEmptyTitle>Supply not found</div>
            <div hlmEmptyDescription>It may have been deleted.</div>
          </div>
        </div>
      } @else if (supply(); as s) {
        <!-- Header -->
        <div class="flex flex-wrap items-start justify-between gap-4">
          <div class="space-y-1">
            <div class="flex items-center gap-3">
              <h1 class="text-2xl font-semibold">{{ s.name }}</h1>
              <app-item-status-badge [status]="s.status" />
              @if (s.isLow) {
                <span class="inline-flex h-5 items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-700 dark:bg-amber-950 dark:text-amber-300">
                  <ng-icon hlm name="lucideTriangleAlert" size="xs" />
                  Low
                </span>
              }
            </div>
            <p class="text-3xl font-bold tabular-nums">
              {{ formatQty(s.totalOnHand) }}
              <span class="text-base font-normal text-muted-foreground">{{ s.unit }} on hand</span>
            </p>
            @if (s.restockThreshold != null) {
              <p class="text-xs text-muted-foreground">Restock at or below {{ formatQty(s.restockThreshold) }} {{ s.unit }}</p>
            }
            @if (s.notes) {
              <p class="max-w-prose text-sm text-muted-foreground">{{ s.notes }}</p>
            }
          </div>
          <div class="flex items-center gap-2">
            <button hlmBtn variant="outline" size="sm" (click)="openEdit(s)">
              <ng-icon hlm name="lucidePencil" size="sm" />
              Edit
            </button>
            @if (s.status === 'Active') {
              <button hlmBtn variant="outline" size="sm" (click)="openCollapse()">
                <ng-icon hlm name="lucideBoxes" size="sm" />
                Collapse items
              </button>
            }
            <button hlmBtn size="sm" (click)="openReceive()">
              <ng-icon hlm name="lucidePlus" size="sm" />
              Receive
            </button>
          </div>
        </div>

        <!-- Per-location breakdown -->
        <section class="space-y-2">
          <h2 class="text-sm font-medium text-muted-foreground">By location</h2>
          @if (locations().length === 0) {
            <div class="rounded-md border border-dashed p-6 text-center text-sm text-muted-foreground">
              No stock on hand. Use <span class="font-medium text-foreground">Receive</span> to add some.
            </div>
          } @else {
            <div class="overflow-clip rounded-md border">
              <table hlmTable class="w-full">
                <thead hlmTHead class="bg-background">
                  <tr hlmTr>
                    <th hlmTh>Location</th>
                    <th hlmTh class="text-right">Quantity</th>
                    <th hlmTh class="w-px"></th>
                  </tr>
                </thead>
                <tbody hlmTBody>
                  @for (entry of locations(); track entry.id) {
                    <tr hlmTr>
                      <td hlmTd>{{ entry.locationLabel ?? '(unknown)' }}</td>
                      <td hlmTd class="text-right tabular-nums">{{ formatQty(entry.quantity) }} {{ s.unit }}</td>
                      <td hlmTd class="whitespace-nowrap text-right">
                        <button hlmBtn variant="ghost" size="sm" (click)="openConsume(entry)">Use</button>
                        <button hlmBtn variant="ghost" size="sm" (click)="openTransfer(entry)">Move</button>
                        <button hlmBtn variant="ghost" size="sm" (click)="openAdjust(entry)">Correct</button>
                        @if (s.status === 'Active' && isWholeCount(entry.quantity)) {
                          <button hlmBtn variant="ghost" size="sm" (click)="openTrackOne(entry)">Track one</button>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </section>

        <!-- Movement ledger -->
        <section class="space-y-2">
          <h2 class="text-sm font-medium text-muted-foreground">History</h2>
          @if (movements().length === 0) {
            <div class="rounded-md border border-dashed p-6 text-center text-sm text-muted-foreground">
              No movements recorded yet.
            </div>
          } @else {
            <div class="overflow-clip rounded-md border">
              <table hlmTable class="w-full">
                <thead hlmTHead class="bg-background">
                  <tr hlmTr>
                    <th hlmTh>When</th>
                    <th hlmTh>Change</th>
                    <th hlmTh>Movement</th>
                    <th hlmTh class="text-right">Amount</th>
                    <th hlmTh>By</th>
                  </tr>
                </thead>
                <tbody hlmTBody>
                  @for (m of movements(); track m.id) {
                    <tr hlmTr>
                      <td hlmTd class="whitespace-nowrap text-muted-foreground">{{ occurred(m) }}</td>
                      <td hlmTd>
                        <span class="inline-flex items-center gap-1.5 text-sm">
                          @if (direction(m) === 'inflow') {
                            <ng-icon hlm name="lucideArrowRight" size="xs" class="text-emerald-600" />
                            <span>{{ m.toLabel ?? '—' }}</span>
                          } @else if (direction(m) === 'outflow') {
                            <span>{{ m.fromLabel ?? '—' }}</span>
                            <ng-icon hlm name="lucideArrowRight" size="xs" class="text-destructive" />
                          } @else if (direction(m) === 'transfer') {
                            <span>{{ m.fromLabel ?? '—' }}</span>
                            <ng-icon hlm name="lucideArrowRight" size="xs" class="text-muted-foreground" />
                            <span>{{ m.toLabel ?? '—' }}</span>
                          } @else {
                            <ng-icon hlm name="lucideMinus" size="xs" class="text-muted-foreground" />
                          }
                        </span>
                        @if (m.note) {
                          <span class="block truncate text-xs text-muted-foreground">{{ m.note }}</span>
                        }
                      </td>
                      <td hlmTd>
                        <span class="text-xs text-muted-foreground">{{ kindLabel(m) }}</span>
                        @if (m.kind === 'Conversion' && m.relatedItemName) {
                          <span class="block truncate text-xs text-muted-foreground">{{ m.relatedItemName }}</span>
                        }
                      </td>
                      <td hlmTd class="whitespace-nowrap text-right tabular-nums" [class]="amountClass(m)">
                        {{ amountPrefix(m) }}{{ formatQty(m.quantity) }} {{ s.unit }}
                        @if (m.unitCost != null) {
                          <span class="block text-xs font-normal text-muted-foreground">
                            {{ formatQty(m.unitCost) }}{{ m.unitCostCurrencySymbol ? ' ' + m.unitCostCurrencySymbol : '' }}/{{ s.unit }}
                          </span>
                        }
                      </td>
                      <td hlmTd class="whitespace-nowrap text-muted-foreground">{{ m.createdByDisplayName ?? '—' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>

            @if (totalPages() > 1) {
              <div class="flex items-center justify-end gap-2">
                <span class="text-xs text-muted-foreground">Page {{ page() }} of {{ totalPages() }}</span>
                <button hlmBtn variant="outline" size="icon-sm" [disabled]="page() <= 1" (click)="prevPage()" aria-label="Previous page">
                  <ng-icon hlm name="lucideChevronLeft" size="sm" />
                </button>
                <button hlmBtn variant="outline" size="icon-sm" [disabled]="page() >= totalPages()" (click)="nextPage()" aria-label="Next page">
                  <ng-icon hlm name="lucideChevronRight" size="sm" />
                </button>
              </div>
            }
          }
        </section>
      }
    </div>

    @if (supply(); as s) {
      <app-stock-op-sheet
        [supplyId]="s.id"
        [unit]="s.unit"
        [mode]="stockOpMode()"
        [fromLocation]="stockOpLocation()"
        [state]="stockOpState()"
        (updated)="onStockUpdated()"
        (closed)="stockOpState.set('closed')"
      />

      <app-track-item-sheet
        [supplyId]="s.id"
        [supplyName]="s.name"
        [unit]="s.unit"
        [fromLocation]="trackLocation()"
        [state]="trackState()"
        (created)="onTracked()"
        (closed)="trackState.set('closed')"
      />

      <app-collapse-items-dialog
        [supplyId]="s.id"
        [supplyName]="s.name"
        [unit]="s.unit"
        [state]="collapseState()"
        (collapsed)="onCollapsed()"
        (closed)="collapseState.set('closed')"
      />
    }

    <app-edit-supply-sheet
      [supply]="editSupply()"
      [state]="editState()"
      (saved)="onEditSaved()"
      (closed)="editState.set('closed')"
    />
  `,
})
export class SupplyDetailPageComponent {
  private supplyApi = inject(InventorySupplyApiService);
  private route = inject(ActivatedRoute);
  private locale = inject(LOCALE_ID);

  protected readonly formatQty = formatQty;
  protected readonly isWholeCount = isWholeCount;

  private supplyId = toSignal(this.route.paramMap.pipe(map((p) => p.get('id'))), { initialValue: null });

  refreshTrigger = signal(0);
  loading = signal(false);
  notFound = signal(false);
  page = signal(1);

  private detailKey = computed(() => ({ id: this.supplyId(), tick: this.refreshTrigger() }));

  detail = toSignal<SupplyDetailResponse | null>(
    toObservable(this.detailKey).pipe(
      switchMap(({ id }) => {
        if (!id) return of(null);
        this.loading.set(true);
        this.notFound.set(false);
        return this.supplyApi.getSupply({ id }).pipe(
          map((d) => {
            this.loading.set(false);
            return d;
          }),
          catchError(() => {
            this.loading.set(false);
            this.notFound.set(true);
            return of(null);
          }),
        );
      }),
    ),
    { initialValue: null },
  );

  supply = computed<SupplyResponse | null>(() => this.detail()?.supply ?? null);
  locations = computed<StockEntryResponse[]>(() => this.detail()?.locations ?? []);

  private movementsKey = computed(() => ({
    id: this.supplyId(),
    page: this.page(),
    tick: this.refreshTrigger(),
  }));

  private movementsPage = toSignal(
    toObservable(this.movementsKey).pipe(
      switchMap(({ id, page }) => {
        if (!id) return of(null);
        return this.supplyApi.getSupplyMovements({ id, Page: page, PageSize: 25 }).pipe(
          catchError(() => of(null)),
        );
      }),
    ),
    { initialValue: null },
  );

  movements = computed<StockMovementResponse[]>(() => this.movementsPage()?.items ?? []);
  totalPages = computed(() => this.movementsPage()?.totalPages ?? 1);

  // Stock-op sheet state.
  stockOpMode = signal<StockOpMode>('receive');
  stockOpLocation = signal<StockOpLocation | null>(null);
  stockOpState = signal<BrnDialogState>('closed');

  // Track-one sheet state.
  trackLocation = signal<StockOpLocation | null>(null);
  trackState = signal<BrnDialogState>('closed');

  // Collapse-items dialog state.
  collapseState = signal<BrnDialogState>('closed');

  // Edit sheet state.
  editSupply = signal<SupplyResponse | null>(null);
  editState = signal<BrnDialogState>('closed');

  private toOpLocation(entry: StockEntryResponse): StockOpLocation {
    return {
      areaId: entry.areaId ?? null,
      containerItemId: entry.containerItemId ?? null,
      label: entry.locationLabel ?? '(unknown)',
      quantity: entry.quantity,
    };
  }

  openReceive(): void {
    this.stockOpMode.set('receive');
    this.stockOpLocation.set(null);
    this.stockOpState.set('open');
  }

  openConsume(entry: StockEntryResponse): void {
    this.stockOpMode.set('consume');
    this.stockOpLocation.set(this.toOpLocation(entry));
    this.stockOpState.set('open');
  }

  openTransfer(entry: StockEntryResponse): void {
    this.stockOpMode.set('transfer');
    this.stockOpLocation.set(this.toOpLocation(entry));
    this.stockOpState.set('open');
  }

  openAdjust(entry: StockEntryResponse): void {
    this.stockOpMode.set('adjust');
    this.stockOpLocation.set(this.toOpLocation(entry));
    this.stockOpState.set('open');
  }

  onStockUpdated(): void {
    this.stockOpState.set('closed');
    this.refreshTrigger.update((v) => v + 1);
  }

  openTrackOne(entry: StockEntryResponse): void {
    this.trackLocation.set(this.toOpLocation(entry));
    this.trackState.set('open');
  }

  // Refreshes the detail and the movements table (which now carries the new Conversion outflow).
  onTracked(): void {
    this.trackState.set('closed');
    this.refreshTrigger.update((v) => v + 1);
  }

  openCollapse(): void {
    this.collapseState.set('open');
  }

  onCollapsed(): void {
    this.collapseState.set('closed');
    this.refreshTrigger.update((v) => v + 1);
  }

  openEdit(supply: SupplyResponse): void {
    this.editSupply.set(supply);
    this.editState.set('open');
  }

  onEditSaved(): void {
    this.editState.set('closed');
    this.refreshTrigger.update((v) => v + 1);
  }

  prevPage(): void {
    this.page.update((p) => Math.max(1, p - 1));
  }

  nextPage(): void {
    this.page.update((p) => Math.min(this.totalPages(), p + 1));
  }

  protected direction = movementDirection;

  protected occurred(m: StockMovementResponse): string {
    return formatDate(m.occurredAt, 'mediumDate', this.locale);
  }

  // A human action label derived from kind + structural direction (the ledger still stores only the
  // Kind discriminator; this is presentation). Flow reads as Received / Used / Transferred.
  protected kindLabel(m: StockMovementResponse): string {
    if (m.kind !== 'Flow') return m.kind;
    switch (movementDirection(m)) {
      case 'inflow': return 'Received';
      case 'outflow': return 'Used';
      case 'transfer': return 'Transferred';
      default: return 'Flow';
    }
  }

  protected amountPrefix(m: StockMovementResponse): string {
    const dir = movementDirection(m);
    if (dir === 'inflow') return '+';
    if (dir === 'outflow') return '−';
    return '';
  }

  protected amountClass(m: StockMovementResponse): string {
    const dir = movementDirection(m);
    if (dir === 'inflow') return 'text-emerald-600 font-medium';
    if (dir === 'outflow') return 'text-destructive font-medium';
    return '';
  }
}
