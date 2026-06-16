import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  signal,
  computed,
  untracked,
  DestroyRef,
  LOCALE_ID,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { formatDate } from '@angular/common';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, of, catchError } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucidePencil,
  lucidePlus,
  lucideCircleAlert,
  lucideTriangleAlert,
  lucideArrowRight,
  lucideMinus,
  lucideChevronLeft,
  lucideChevronRight,
  lucideBoxes,
  lucideEllipsisVertical,
  lucideArchive,
  lucideArchiveRestore,
  lucideTrash2,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { InventoryConsumableApiService } from '@/api/services/inventory-consumable-api.service';
import { ItemStatusBadgeComponent } from '../shared/item-status-badge.component';
import { isTerminalStatus } from '../shared/item-status-meta';
import { EditConsumableSheetComponent } from './edit-consumable-sheet/edit-consumable-sheet.component';
import {
  StockOpSheetComponent,
  type StockOpMode,
  type StockOpLocation,
} from './stock-op-sheet/stock-op-sheet.component';
import { TrackDurableSheetComponent } from './track-durable-sheet/track-durable-sheet.component';
import { CollapseDurablesDialogComponent } from './collapse-durables-dialog/collapse-durables-dialog.component';
import { formatQty, movementDirection, isWholeCount } from './consumable-shared';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { formatRelativeAge } from '@/shared/utils/relative-age';
import { replace } from '@/shared/json-patch';
import { handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { toast } from '@spartan-ng/brain/sonner';
import type { ConsumableResponse } from '@/api/generated/models/consumable-response';
import type { ConsumableDetailResponse } from '@/api/generated/models/consumable-detail-response';
import type { StockEntryResponse } from '@/api/generated/models/stock-entry-response';
import type { StockMovementResponse } from '@/api/generated/models/stock-movement-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';

@Component({
  selector: 'app-consumable-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmEmptyImports,
    HlmTableImports,
    HlmAlertDialogImports,
    HlmDropdownMenuImports,
    ItemStatusBadgeComponent,
    EditConsumableSheetComponent,
    StockOpSheetComponent,
    TrackDurableSheetComponent,
    CollapseDurablesDialogComponent,
  ],
  providers: [provideIcons({
    lucidePencil,
    lucidePlus,
    lucideCircleAlert,
    lucideTriangleAlert,
    lucideArrowRight,
    lucideMinus,
    lucideChevronLeft,
    lucideChevronRight,
    lucideBoxes,
    lucideEllipsisVertical,
    lucideArchive,
    lucideArchiveRestore,
    lucideTrash2,
  })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <div class="flex min-h-0 flex-1 flex-col gap-6 p-4 md:p-6">
      @if (loading() && !detail()) {
        <div class="flex justify-center py-16"><hlm-spinner /></div>
      } @else if (notFound()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCircleAlert" /></div>
            <div hlmEmptyTitle>Consumable not found</div>
            <div hlmEmptyDescription>It may have been deleted.</div>
          </div>
        </div>
      } @else if (consumable(); as s) {
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
            <p class="text-xs text-muted-foreground">
              @if (s.restockThreshold != null) {
                Restock at or below {{ formatQty(s.restockThreshold) }} {{ s.unit }} &middot;
              }
              Updated {{ updatedRelative() }}
            </p>
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
                Collapse durables
              </button>
            }
            <button hlmBtn size="sm" (click)="openReceive()">
              <ng-icon hlm name="lucidePlus" size="sm" />
              Receive
            </button>
            <button hlmBtn variant="outline" size="icon-sm" [hlmDropdownMenuTrigger]="actionsMenu" aria-label="Consumable actions">
              <ng-icon hlm name="lucideEllipsisVertical" size="sm" />
            </button>
            <ng-template #actionsMenu>
              <hlm-dropdown-menu class="w-44">
                <hlm-dropdown-menu-group>
                  <button hlmDropdownMenuItem (triggered)="onArchiveRestore(s)" [disabled]="archiving()">
                    <ng-icon hlm [name]="terminal() ? 'lucideArchiveRestore' : 'lucideArchive'" size="sm" />
                    {{ terminal() ? 'Restore' : 'Archive' }}
                  </button>
                  <hlm-dropdown-menu-separator />
                  <button hlmDropdownMenuItem variant="destructive" (triggered)="onDeleteRequest()">
                    <ng-icon hlm name="lucideTrash2" size="sm" />
                    Delete
                  </button>
                </hlm-dropdown-menu-group>
              </hlm-dropdown-menu>
            </ng-template>
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

    @if (consumable(); as s) {
      <app-stock-op-sheet
        [consumableId]="s.id"
        [unit]="s.unit"
        [mode]="stockOpMode()"
        [fromLocation]="stockOpLocation()"
        [state]="stockOpState()"
        (updated)="onStockUpdated()"
        (closed)="stockOpState.set('closed')"
      />

      <app-track-durable-sheet
        [consumableId]="s.id"
        [consumableName]="s.name"
        [unit]="s.unit"
        [fromLocation]="trackLocation()"
        [state]="trackState()"
        (created)="onTracked()"
        (closed)="trackState.set('closed')"
      />

      <app-collapse-durables-dialog
        [consumableId]="s.id"
        [consumableName]="s.name"
        [unit]="s.unit"
        [state]="collapseState()"
        (collapsed)="onCollapsed()"
        (closed)="collapseState.set('closed')"
      />
    }

    <app-edit-consumable-sheet
      [consumable]="editConsumable()"
      [state]="editState()"
      (saved)="onEditSaved()"
      (closed)="editState.set('closed')"
    />

    <hlm-alert-dialog [state]="deleteState()" (stateChanged)="deleteState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Delete {{ consumable()?.name ?? 'consumable' }}?</h2>
          <p hlmAlertDialogDescription>
            This permanently removes the consumable. Consumables with stock history cannot be deleted — archive them instead.
          </p>
        </div>
        @if (deleteError()) {
          <p class="text-sm text-destructive">{{ deleteError() }}</p>
        }
        <div hlmAlertDialogFooter>
          <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
          <button
            hlmAlertDialogAction
            variant="destructive"
            (click)="confirmDelete(ctx)"
            [disabled]="deleting()"
          >
            {{ deleting() ? 'Deleting...' : 'Delete' }}
          </button>
        </div>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class ConsumableDetailPageComponent {
  private consumableApi = inject(InventoryConsumableApiService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private errorHandler = inject(ErrorHandlerService);
  private destroyRef = inject(DestroyRef);
  private locale = inject(LOCALE_ID);

  protected readonly formatQty = formatQty;
  protected readonly isWholeCount = isWholeCount;

  // Optional payload from the `/inventory/items/:id` dispatcher, which already fetched the record. When
  // present we render from it and skip the initial GET; absent (direct/standalone use) we fetch by id.
  preloaded = input<ConsumableDetailResponse | null>(null);

  private consumableId = toSignal(this.route.paramMap.pipe(map((p) => p.get('id'))), { initialValue: null });

  refreshTrigger = signal(0);
  loading = signal(false);
  notFound = signal(false);
  // Reset to page 1 when the routed consumable changes, so a same-component reuse (standalone, or a
  // future change that keeps this child mounted across :id) can't carry a stale page into the new
  // consumable's movements query. Mirrors the durable page's activity-page handling.
  page = seededLinkedSignal(this.consumableId, () => 1);

  private detailKey = computed(() => ({ id: this.consumableId(), tick: this.refreshTrigger() }));

  private fetchedDetail = toSignal<ConsumableDetailResponse | null>(
    toObservable(this.detailKey).pipe(
      switchMap(({ id, tick }) => {
        if (!id) return of(null);
        // Initial render of a dispatcher-preloaded item: reuse the preload, skip the duplicate GET.
        // A refresh (tick > 0) or a missing preload always hits the API.
        if (tick === 0 && untracked(() => this.preloaded())?.consumable?.id === id) return of(null);
        this.loading.set(true);
        this.notFound.set(false);
        return this.consumableApi.getConsumable({ id }).pipe(
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

  // Prefer freshly-fetched data once it matches the routed id; otherwise fall back to the preload.
  detail = computed<ConsumableDetailResponse | null>(() => {
    const id = this.consumableId();
    const fetched = this.fetchedDetail();
    if (fetched && fetched.consumable?.id === id) return fetched;
    const pre = this.preloaded();
    return pre && pre.consumable?.id === id ? pre : null;
  });

  consumable = computed<ConsumableResponse | null>(() => this.detail()?.consumable ?? null);
  locations = computed<StockEntryResponse[]>(() => this.detail()?.locations ?? []);

  protected terminal = computed(() => {
    const s = this.consumable();
    return s ? isTerminalStatus(s.status) : false;
  });

  protected updatedRelative = computed(() => {
    const updatedAt = this.consumable()?.updatedAt;
    return updatedAt ? formatRelativeAge(updatedAt) : '';
  });

  private movementsKey = computed(() => ({
    id: this.consumableId(),
    page: this.page(),
    tick: this.refreshTrigger(),
  }));

  private movementsPage = toSignal(
    toObservable(this.movementsKey).pipe(
      switchMap(({ id, page }) => {
        if (!id) return of(null);
        return this.consumableApi.getConsumableMovements({ id, Page: page, PageSize: 25 }).pipe(
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

  // Collapse-durables dialog state.
  collapseState = signal<BrnDialogState>('closed');

  // Edit sheet state.
  editConsumable = signal<ConsumableResponse | null>(null);
  editState = signal<BrnDialogState>('closed');

  // Lifecycle (archive/restore + delete) overflow-menu state.
  deleteState = signal<BrnDialogState>('closed');
  deleting = signal(false);
  deleteError = signal('');
  archiving = signal(false);

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

  openEdit(consumable: ConsumableResponse): void {
    this.editConsumable.set(consumable);
    this.editState.set('open');
  }

  onEditSaved(): void {
    this.editState.set('closed');
    this.refreshTrigger.update((v) => v + 1);
  }

  onArchiveRestore(consumable: ConsumableResponse): void {
    const terminal = isTerminalStatus(consumable.status);
    const newStatus = terminal ? 'Active' : 'Archived';
    this.archiving.set(true);
    this.consumableApi.patchConsumable({ id: consumable.id, body: [replace('/status', newStatus)] }).pipe(
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.archiving, 'Failed to update consumable.'),
    ).subscribe(() => {
      this.archiving.set(false);
      toast.success(terminal ? 'Consumable restored.' : 'Consumable archived.');
      this.refreshTrigger.update((v) => v + 1);
    });
  }

  onDeleteRequest(): void {
    this.deleteError.set('');
    this.deleteState.set('open');
  }

  confirmDelete(ctx: { close: () => void }): void {
    const s = this.consumable();
    if (!s) return;
    this.deleting.set(true);
    this.deleteError.set('');
    this.consumableApi.deleteConsumable({ id: s.id }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.deleting.set(false);
        ctx.close();
        toast.success('Consumable deleted.');
        void this.router.navigate(['/inventory/items']);
      },
      // Inline, not a toast: the open dialog overlay would obscure a toast, and the rejection reason
      // ("has stock history — archive instead") is what the user needs to read here.
      error: (err: unknown) => {
        this.deleting.set(false);
        this.deleteError.set(this.errorHandler.getErrorMessage(err, 'Failed to delete consumable.'));
      },
    });
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
