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
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
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
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { InventoryConsumableApiService } from '@/api/services/inventory-consumable-api.service';
import { ItemStatusBadgeComponent } from '../shared/item-status-badge.component';
import { stockEntryLocationPath } from '../shared/location-path';
import { LocationPathComponent } from '../shared/location-path.component';
import { TagBadgeComponent } from '@/shared/components/tag-badge/tag-badge.component';
import { isTerminalStatus, LOW_STOCK_BADGE_CLASS } from '../shared/item-status-meta';
import { EditConsumableSheetComponent } from './edit-consumable-sheet/edit-consumable-sheet.component';
import {
  StockOpSheetComponent,
  type StockOpMode,
  type StockOpLocation,
} from './stock-op-sheet/stock-op-sheet.component';
import { TrackDurableSheetComponent } from './track-durable-sheet/track-durable-sheet.component';
import { CollapseDurablesDialogComponent } from './collapse-durables-dialog/collapse-durables-dialog.component';
import { ItemActivityFeedComponent } from '../shared/activity/item-activity-feed.component';
import { formatQty, movementDirection, isWholeCount } from './consumable-shared';
import { UnitPipe } from './unit.pipe';
import { seededLinkedSignal } from '@/shared/utils/seeded-linked-signal';
import { formatRelativeAge } from '@/shared/utils/relative-age';
import { replace } from '@/shared/json-patch';
import { trimToNull } from '@/shared/utils/trim-to-null';
import { handleDetailLoadError } from '@/shared/utils/rxjs-error.utils';
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
    RouterLink,
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmInput,
    HlmLabel,
    HlmSpinner,
    HlmEmptyImports,
    HlmTableImports,
    HlmAlertDialogImports,
    HlmDropdownMenuImports,
    ItemStatusBadgeComponent,
    LocationPathComponent,
    TagBadgeComponent,
    EditConsumableSheetComponent,
    StockOpSheetComponent,
    TrackDurableSheetComponent,
    CollapseDurablesDialogComponent,
    ItemActivityFeedComponent,
    UnitPipe,
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
      @if (consumable(); as s) {
        @if (loadError()) {
          <div class="flex items-center justify-between gap-3 rounded-md border border-dashed border-destructive/50 p-3 text-sm text-destructive">
            <span>Couldn't refresh. Showing the last loaded version.</span>
            <button type="button" hlmBtn variant="outline" size="sm" (click)="retry()">Retry</button>
          </div>
        }
        <!-- Header -->
        <div class="flex flex-wrap items-start justify-between gap-4">
          <div class="space-y-1">
            <div class="flex items-center gap-3">
              <h1 class="text-2xl font-semibold">{{ s.name }}</h1>
              <app-item-status-badge [status]="s.status" [note]="s.statusNote" />
              @if (s.isLow) {
                <span [class]="lowStockBadgeClass">
                  <ng-icon hlm name="lucideTriangleAlert" size="xs" />
                  Low
                </span>
              }
            </div>
            <p class="text-3xl font-bold tabular-nums">
              {{ formatQty(s.totalOnHand) }}
              <span class="text-base font-normal text-muted-foreground">{{ s.unit | unit }} on hand</span>
            </p>
            <p class="text-xs text-muted-foreground">
              @if (s.restockThreshold != null) {
                Restock at or below {{ formatQty(s.restockThreshold) }} {{ s.unit | unit }} &middot;
              }
              Updated {{ updatedRelative() }}
            </p>
            @if (s.brandId) {
              <p class="text-sm text-muted-foreground">
                Brand:
                <a
                  routerLink="/inventory/items"
                  [queryParams]="{ brand: s.brandId }"
                  class="text-foreground hover:underline"
                >{{ s.brandName }}</a>
              </p>
            }
            @if (s.notes) {
              <!-- No Details card on this page (unlike the durable page), so the description stays in
                   the header with a small label; same weight as the "Brand:" prefix above. -->
              <div class="max-w-prose text-sm text-muted-foreground">
                <p>Description</p>
                <p class="whitespace-pre-wrap text-foreground">{{ s.notes }}</p>
              </div>
            }
            @if (s.tags?.length) {
              <div class="flex flex-wrap gap-1 pt-1">
                @for (t of s.tags ?? []; track t.id) {
                  <a [routerLink]="['/inventory/items']" [queryParams]="{ tagIds: t.id }">
                    <app-tag-badge [path]="t.path" [color]="t.effectiveColor" [compact]="true" />
                  </a>
                }
              </div>
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
              <div hlmTableContainer>
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
                        <!-- max-w-0 + w-full lets the truncating path clip inside this auto-layout
                             table; the Quantity and action columns keep their intrinsic widths. -->
                        <td hlmTd class="w-full max-w-0">
                          <app-location-path [path]="stockEntryLocationPath(entry)" [collapse]="false" />
                        </td>
                        <td hlmTd class="text-right tabular-nums">{{ formatQty(entry.quantity) }} {{ s.unit | unit }}</td>
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
            </div>
          }
        </section>

        <!-- Movement ledger -->
        <section class="space-y-2">
          <h2 class="text-sm font-medium text-muted-foreground">Movements</h2>
          @if (movements().length === 0) {
            <div class="rounded-md border border-dashed p-6 text-center text-sm text-muted-foreground">
              No movements recorded yet.
            </div>
          } @else {
            <div class="overflow-clip rounded-md border">
              <div hlmTableContainer>
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
                          @if (movementMeta(m); as meta) {
                            <!-- title: the row truncates, and this line is the only surface showing a
                                 movement's source — hover must still reveal the full text. -->
                            <span class="block truncate text-xs text-muted-foreground" [title]="meta">{{ meta }}</span>
                          }
                        </td>
                        <td hlmTd>
                          <span class="text-xs text-muted-foreground">{{ kindLabel(m) }}</span>
                          @if (m.kind === 'Conversion' && m.relatedItemName) {
                            <span class="block truncate text-xs text-muted-foreground">{{ m.relatedItemName }}</span>
                          }
                        </td>
                        <td hlmTd class="whitespace-nowrap text-right tabular-nums" [class]="amountClass(m)">
                          {{ amountPrefix(m) }}{{ formatQty(m.quantity) }} {{ s.unit | unit }}
                          @if (m.unitCost != null) {
                            <span class="block text-xs font-normal text-muted-foreground">
                              {{ formatQty(m.unitCost) }}{{ m.unitCostCurrencySymbol ? ' ' + m.unitCostCurrencySymbol : '' }}/{{ s.unit | unit }}
                            </span>
                          }
                        </td>
                        <td hlmTd class="whitespace-nowrap text-muted-foreground">{{ m.createdByDisplayName ?? '—' }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
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

        <!-- Activity — the shared feed, last on the page. Movements keeps its own pager above. -->
        <app-item-activity-feed [itemId]="s.id" [refreshTick]="refreshTrigger()" />
      } @else if (notFound()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCircleAlert" /></div>
            <div hlmEmptyTitle>Consumable not found</div>
            <div hlmEmptyDescription>It may have been deleted.</div>
          </div>
        </div>
      } @else if (loadError()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCircleAlert" /></div>
            <div hlmEmptyTitle>Couldn't load consumable</div>
            <div hlmEmptyDescription>Something went wrong. Check your connection and try again.</div>
          </div>
          <div hlmEmptyContent>
            <button type="button" hlmBtn variant="outline" (click)="retry()">Retry</button>
          </div>
        </div>
      } @else if (loading()) {
        <div class="flex justify-center py-16"><hlm-spinner /></div>
      }
    </div>

    @if (consumable(); as s) {
      <app-stock-op-sheet
        [consumableId]="s.id"
        [unit]="s.unit | unit"
        [mode]="stockOpMode()"
        [fromLocation]="stockOpLocation()"
        [state]="stockOpState()"
        (updated)="onStockUpdated()"
        (closed)="stockOpState.set('closed')"
      />

      <app-track-durable-sheet
        [consumableId]="s.id"
        [consumableName]="s.name"
        [unit]="s.unit | unit"
        [fromLocation]="trackLocation()"
        [state]="trackState()"
        (created)="onTracked()"
        (closed)="trackState.set('closed')"
      />

      <app-collapse-durables-dialog
        [consumableId]="s.id"
        [consumableName]="s.name"
        [unit]="s.unit | unit"
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

    <hlm-alert-dialog [state]="archiveState()" (stateChanged)="archiveState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Archive {{ consumable()?.name ?? 'consumable' }}?</h2>
          <p hlmAlertDialogDescription>
            The consumable and its stock history are kept, but it leaves the default list. You can restore it later.
          </p>
        </div>
        <div class="grid gap-2">
          <label hlmLabel for="archive-note">Note (optional)</label>
          <input
            hlmInput
            id="archive-note"
            class="w-full"
            maxlength="256"
            placeholder="e.g. Used up"
            [value]="archiveNote()"
            (input)="archiveNote.set($any($event.target).value)"
          />
        </div>
        @if (archiveError()) {
          <p class="text-sm text-destructive">{{ archiveError() }}</p>
        }
        <div hlmAlertDialogFooter>
          <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
          <button hlmAlertDialogAction (click)="confirmArchive(ctx)" [disabled]="archiving()">
            {{ archiving() ? 'Archiving...' : 'Archive' }}
          </button>
        </div>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>

    <hlm-alert-dialog [state]="deleteState()" (stateChanged)="deleteState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Delete {{ consumable()?.name ?? 'consumable' }}?</h2>
          <p hlmAlertDialogDescription>
            This permanently removes the consumable. Consumables with stock history or notes cannot be deleted — archive them instead.
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
  protected readonly stockEntryLocationPath = stockEntryLocationPath;
  protected readonly isWholeCount = isWholeCount;
  protected readonly lowStockBadgeClass = LOW_STOCK_BADGE_CLASS;

  // Optional payload from the `/inventory/items/:id` dispatcher, which already fetched the record. When
  // present we render from it and skip the initial GET; absent (direct/standalone use) we fetch by id.
  preloaded = input<ConsumableDetailResponse | null>(null);

  private consumableId = toSignal(this.route.paramMap.pipe(map((p) => p.get('id'))), { initialValue: null });

  refreshTrigger = signal(0);
  loading = signal(false);
  notFound = signal(false);
  // A non-404 load/refetch failure — retryable, and never allowed to replace already-rendered data.
  loadError = signal(false);
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
        this.loadError.set(false);
        return this.consumableApi.getConsumable({ id }).pipe(
          map((d) => {
            this.loading.set(false);
            return d;
          }),
          catchError((err) => handleDetailLoadError(err, { notFound: this.notFound, loadError: this.loadError }, this.loading)),
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
  archiveState = signal<BrnDialogState>('closed');
  archiving = signal(false);
  archiveError = signal('');
  /** Optional reason typed into the archive confirm dialog; reset each time it opens. */
  archiveNote = signal('');

  private toOpLocation(entry: StockEntryResponse): StockOpLocation {
    return {
      areaId: entry.areaId ?? null,
      containerItemId: entry.containerItemId ?? null,
      label: entry.locationLabel ?? '(unknown)',
      quantity: entry.quantity,
    };
  }

  /** Retry a failed load/refetch — same mechanism the mutation handlers use to refresh. */
  retry(): void {
    this.refreshTrigger.update((v) => v + 1);
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

  /** Restore is immediate; archive asks first. */
  onArchiveRestore(consumable: ConsumableResponse): void {
    if (isTerminalStatus(consumable.status)) {
      this.setStatus(consumable, 'Active');
      return;
    }
    this.archiveNote.set('');
    this.archiveError.set('');
    this.archiveState.set('open');
  }

  confirmArchive(ctx: { close: () => void }): void {
    const s = this.consumable();
    if (!s) return;
    this.setStatus(s, 'Archived', ctx);
  }

  private setStatus(consumable: ConsumableResponse, newStatus: 'Active' | 'Archived', ctx?: { close: () => void }): void {
    this.archiving.set(true);
    this.archiveError.set('');
    // Archiving may carry an optional reason; restoring clears the note server-side.
    const body = newStatus === 'Archived'
      ? [replace('/status', newStatus), replace('/statusNote', trimToNull(this.archiveNote()))]
      : [replace('/status', newStatus)];
    this.consumableApi.patchConsumable({ id: consumable.id, body }).pipe(
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.archiving.set(false);
        ctx?.close();
        toast.success(newStatus === 'Active' ? 'Consumable restored.' : 'Consumable archived.');
        this.refreshTrigger.update((v) => v + 1);
      },
      // With the confirm dialog open, the rejection reason is shown inline where the user is looking;
      // a restore has no dialog, so it toasts like other mutations.
      error: (err: unknown) => {
        this.archiving.set(false);
        const message = this.errorHandler.getErrorMessage(err, 'Failed to update consumable.');
        if (ctx) this.archiveError.set(message);
        else if (!this.errorHandler.isServerOrNetworkError(err)) toast.error(message);
      },
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

  /** The row's meta line: the note, plus a `from {source}` token when a receive recorded one. */
  protected movementMeta(m: StockMovementResponse): string | null {
    const parts = [m.note, m.source ? `from ${m.source}` : null].filter(Boolean);
    return parts.length ? parts.join(' · ') : null;
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
