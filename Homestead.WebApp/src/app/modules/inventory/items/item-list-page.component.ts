import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
} from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, of, catchError, firstValueFrom, forkJoin } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucidePlus,
  lucidePackage,
  lucideCircleAlert,
  lucideListFilter,
  lucideArrowUp,
  lucideArrowDown,
  lucideCheck,
  lucideChevronRight,
  lucideX,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmPopoverImports } from '@/shared/components/spartan/popover';
import { HlmSeparatorImports } from '@/shared/components/spartan/separator';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { InventoryItemApiService } from '@/api/services/inventory-item-api.service';
import { InventoryAreaApiService } from '@/api/services/inventory-area-api.service';
import { ItemTableComponent, formatPath, type ItemSortId, type ItemSortState } from './item-table.component';
import { CreateItemDialogComponent } from './create-item-dialog/create-item-dialog.component';
import { EditItemSheetComponent } from './edit-item-sheet/edit-item-sheet.component';
import { ItemQuickPanelComponent } from './item-quick-panel/item-quick-panel.component';
import { itemStatusLabel, isTerminalStatus } from '../shared/item-status-meta';
import { replace } from '@/shared/json-patch';
import { handleQueryError, handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { ItemResponse } from '@/api/generated/models/item-response';
import type { LocationSegmentKind } from '@/api/generated/models/location-segment-kind';
import type { AreaResponse } from '@/api/generated/models/area-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';

interface ItemSortFieldOption {
  readonly id: ItemSortId;
  readonly label: string;
  readonly defaultDesc: boolean;
}

/** 'all' | 'unplaced' | an area id. */
type AreaFilter = string;

/** Server-fetched payload for a container drill-down: the container itself plus its direct children. */
interface DrillData {
  container: ItemResponse | null;
  children: ItemResponse[];
}

const EMPTY_DRILL: DrillData = { container: null, children: [] };

function compareNullable<T>(av: T | null, bv: T | null, dir: 1 | -1, lt: (a: T, b: T) => number): number {
  if (av === bv) return 0;
  if (av === null) return 1;
  if (bv === null) return -1;
  return lt(av, bv) * dir;
}

@Component({
  selector: 'app-item-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmEmptyImports,
    HlmAlertDialogImports,
    HlmCheckboxImports,
    HlmLabel,
    HlmPopoverImports,
    HlmSeparatorImports,
    HlmDropdownMenuImports,
    ItemTableComponent,
    CreateItemDialogComponent,
    EditItemSheetComponent,
    ItemQuickPanelComponent,
  ],
  providers: [provideIcons({
    lucidePlus,
    lucidePackage,
    lucideCircleAlert,
    lucideListFilter,
    lucideArrowUp,
    lucideArrowDown,
    lucideCheck,
    lucideChevronRight,
    lucideX,
  })],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <div class="flex min-h-0 flex-1 flex-col gap-4 p-4 md:p-6">
      @if (loading() && !data()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      }

      @if (error()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon">
              <ng-icon name="lucideCircleAlert" />
            </div>
            <div hlmEmptyTitle>Failed to load items</div>
            <div hlmEmptyDescription>There was an error loading your items. Please try again.</div>
          </div>
        </div>
      }

      @if (data() !== null) {
        @if (allItems().length === 0) {
          <div hlmEmpty>
            <div hlmEmptyHeader>
              <div hlmEmptyMedia variant="icon">
                <ng-icon name="lucidePackage" />
              </div>
              <div hlmEmptyTitle>No items yet</div>
              <div hlmEmptyDescription>Add your first item to start tracking what you own and where it lives.</div>
            </div>
            <div hlmEmptyContent>
              <app-create-item-dialog (created)="onChanged()">
                <ng-icon hlm name="lucidePlus" size="sm" />
                New item
              </app-create-item-dialog>
            </div>
          </div>
        } @else {
          <div class="flex items-center justify-between gap-2">
            <div class="flex items-center gap-2">
              <div hlmPopover>
                <button hlmPopoverTrigger hlmBtn variant="outline" size="sm">
                  <ng-icon hlm name="lucideListFilter" size="sm" class="text-muted-foreground" />
                  {{ filterLabel() }}
                </button>
                <div hlmPopoverContent *hlmPopoverPortal class="w-[260px] p-0" align="start">
                  <div class="flex items-center justify-between px-3 py-2">
                    <span class="text-sm font-medium">Filters</span>
                    @if (showInactive() || areaFilter() !== 'all' || containerFilter()) {
                      <button
                        hlmBtn
                        variant="ghost"
                        size="sm"
                        class="h-auto px-2 py-0.5 text-xs"
                        (click)="resetFilters()"
                      >Reset</button>
                    }
                  </div>
                  <span hlmSeparator></span>
                  <div class="p-2">
                    <label class="flex items-center gap-2 px-1 py-1.5 text-sm cursor-pointer" hlmLabel>
                      <hlm-checkbox
                        [checked]="showInactive()"
                        (checkedChange)="showInactive.set($event === true)"
                        [aria-label]="'Show inactive items'"
                      />
                      <span>Show inactive ({{ inactiveCount() }})</span>
                    </label>
                  </div>
                  <span hlmSeparator></span>
                  <div class="max-h-[240px] overflow-y-auto p-1">
                    <span class="block px-2 py-1 text-xs font-medium text-muted-foreground">Area</span>
                    <button
                      type="button"
                      class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                      (click)="selectAreaFilter('all')"
                    >
                      <span>All areas</span>
                      @if (areaFilter() === 'all' && !containerFilter()) { <ng-icon hlm name="lucideCheck" size="sm" /> }
                    </button>
                    <button
                      type="button"
                      class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                      (click)="selectAreaFilter('unplaced')"
                    >
                      <span class="italic text-muted-foreground">Unplaced</span>
                      @if (areaFilter() === 'unplaced' && !containerFilter()) { <ng-icon hlm name="lucideCheck" size="sm" /> }
                    </button>
                    @for (area of areas(); track area.id) {
                      <button
                        type="button"
                        class="flex w-full items-center justify-between gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                        (click)="selectAreaFilter(area.id)"
                      >
                        <span class="truncate">{{ area.name }}</span>
                        @if (areaFilter() === area.id && !containerFilter()) { <ng-icon hlm name="lucideCheck" size="sm" /> }
                      </button>
                    }
                  </div>
                </div>
              </div>

              <button hlmBtn variant="outline" size="sm" [hlmDropdownMenuTrigger]="sortMenu">
                <ng-icon hlm [name]="sortState().desc ? 'lucideArrowDown' : 'lucideArrowUp'" size="sm" />
                {{ sortFieldLabel() }}
              </button>
              <ng-template #sortMenu>
                <hlm-dropdown-menu class="w-44">
                  <span hlmDropdownMenuLabel>Sort by</span>
                  <hlm-dropdown-menu-group>
                    @for (opt of sortFields; track opt.id) {
                      <button
                        hlmDropdownMenuRadio
                        [checked]="sortState().id === opt.id"
                        (triggered)="onSortByChange(opt.id)"
                      >
                        <hlm-dropdown-menu-radio-indicator />
                        {{ opt.label }}
                      </button>
                    }
                  </hlm-dropdown-menu-group>
                  <hlm-dropdown-menu-separator />
                  <span hlmDropdownMenuLabel>Direction</span>
                  <hlm-dropdown-menu-group>
                    <button hlmDropdownMenuRadio [checked]="!sortState().desc" (triggered)="onSortDirChange(false)">
                      <hlm-dropdown-menu-radio-indicator />
                      Ascending
                    </button>
                    <button hlmDropdownMenuRadio [checked]="sortState().desc" (triggered)="onSortDirChange(true)">
                      <hlm-dropdown-menu-radio-indicator />
                      Descending
                    </button>
                  </hlm-dropdown-menu-group>
                </hlm-dropdown-menu>
              </ng-template>
            </div>
            <app-create-item-dialog triggerSize="sm" (created)="onChanged()">
              <ng-icon hlm name="lucidePlus" size="sm" />
              New item
            </app-create-item-dialog>
          </div>

          @if (containerFilter()) {
            <div class="flex flex-wrap items-center gap-1 text-sm text-muted-foreground">
              @for (seg of breadcrumb(); track seg.id; let last = $last) {
                @if (last) {
                  <span class="font-medium text-foreground">{{ seg.name }}</span>
                } @else {
                  <button
                    type="button"
                    class="hover:text-foreground hover:underline"
                    (click)="onBreadcrumbSegment(seg)"
                  >{{ seg.name }}</button>
                  <ng-icon hlm name="lucideChevronRight" size="xs" class="shrink-0" />
                }
              } @empty {
                <span class="italic">Container contents</span>
              }
              <button
                hlmBtn
                variant="ghost"
                size="icon-sm"
                class="ml-1 size-6"
                aria-label="Exit container view"
                (click)="clearContainerFilter()"
              >
                <ng-icon hlm name="lucideX" size="sm" />
              </button>
            </div>
          }

          @if (drillLoading() && sortedVisibleItems().length === 0) {
            <div class="flex justify-center py-16">
              <hlm-spinner />
            </div>
          } @else if (sortedVisibleItems().length === 0) {
            <div hlmEmpty>
              <div hlmEmptyHeader>
                <div hlmEmptyMedia variant="icon">
                  <ng-icon name="lucidePackage" />
                </div>
                <div hlmEmptyTitle>No items match</div>
                <div hlmEmptyDescription>No items match the current filters.</div>
              </div>
            </div>
          } @else {
            <app-item-table
              [items]="sortedVisibleItems()"
              [sortState]="sortState()"
              (sortChange)="sortState.set($event)"
              (open)="onOpen($event)"
              (edit)="onEdit($event)"
              (viewContents)="onViewContents($event)"
              (archiveRestore)="onArchiveRestore($event)"
              (delete)="onDeleteRequest($event)"
            />
          }
        }
      }
    </div>

    <app-item-quick-panel
      [item]="quickPanelItem()"
      [state]="quickPanelState()"
      (changed)="onChanged()"
      (closed)="onQuickPanelClosed()"
      (viewContents)="onQuickPanelViewContents($event)"
    />

    <app-edit-item-sheet
      [item]="selectedItem()"
      [state]="sheetState()"
      (saved)="onSheetSaved($event)"
      (closed)="onSheetClosed()"
    />

    <hlm-alert-dialog [state]="deleteState()" (stateChanged)="deleteState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Delete {{ pendingDelete()?.name ?? 'item' }}?</h2>
          <p hlmAlertDialogDescription>
            This permanently removes the item. Items with contents cannot be deleted — archive them instead.
          </p>
        </div>
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
export class ItemListPageComponent {
  private itemApi = inject(InventoryItemApiService);
  private areaApi = inject(InventoryAreaApiService);
  private errorHandler = inject(ErrorHandlerService);

  readonly sortFields: readonly ItemSortFieldOption[] = [
    { id: 'name', label: 'Name', defaultDesc: false },
    { id: 'status', label: 'Status', defaultDesc: false },
    { id: 'location', label: 'Location', defaultDesc: false },
    { id: 'acquiredOn', label: 'Acquired', defaultDesc: true },
    { id: 'updatedAt', label: 'Updated', defaultDesc: true },
  ];

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  drillLoading = signal(false);
  showInactive = signal(false);
  areaFilter = signal<AreaFilter>('all');
  /** When set, the list is scoped to the direct children of this container (drill-down). */
  containerFilter = signal<string | null>(null);

  sortState = signal<ItemSortState>({ id: 'name', desc: false });

  selectedItem = signal<ItemResponse | null>(null);
  sheetState = signal<BrnDialogState>('closed');

  quickPanelItem = signal<ItemResponse | null>(null);
  quickPanelState = signal<BrnDialogState>('closed');

  pendingDelete = signal<ItemResponse | null>(null);
  deleteState = signal<BrnDialogState>('closed');
  deleting = signal(false);
  archiving = signal(false);

  data = toSignal<ItemResponse[] | null, ItemResponse[] | null>(
    toObservable(this.refreshTrigger).pipe(
      switchMap(() => {
        this.loading.set(true);
        this.error.set(false);
        return this.itemApi.getItems({
          Page: 1,
          PageSize: 200,
          IncludeInactive: true,
        }).pipe(
          map(r => {
            this.loading.set(false);
            return (r.items ?? []) as ItemResponse[] | null;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  areas = toSignal(
    toObservable(this.refreshTrigger).pipe(
      switchMap(() =>
        this.areaApi.getAreas({ Page: 1, PageSize: 200 }).pipe(
          map(r => r.items ?? []),
          catchError(() => of<AreaResponse[]>([])),
        ),
      ),
    ),
    { initialValue: [] as AreaResponse[] },
  );

  allItems = computed(() => this.data() ?? []);

  // Drill-down is fetched server-side (the container by id + its direct children via the
  // ParentItemId filter), so contents are complete rather than reconstructed from the capped
  // all-items page. Re-runs on refresh and whenever the drilled container changes.
  private drillKey = computed(() => ({ id: this.containerFilter(), tick: this.refreshTrigger() }));

  private drillData = toSignal(
    toObservable(this.drillKey).pipe(
      switchMap(({ id }) => {
        if (!id) {
          this.drillLoading.set(false);
          return of(EMPTY_DRILL);
        }
        this.drillLoading.set(true);
        return forkJoin({
          container: this.itemApi.getItem({ id }).pipe(catchError(() => of<ItemResponse | null>(null))),
          children: this.itemApi
            .getItems({ Page: 1, PageSize: 200, IncludeInactive: true, ParentItemId: id })
            .pipe(map(r => (r.items ?? []) as ItemResponse[]), catchError(() => of<ItemResponse[]>([]))),
        }).pipe(
          map(result => {
            this.drillLoading.set(false);
            return result;
          }),
          catchError(() => {
            this.drillLoading.set(false);
            return of(EMPTY_DRILL);
          }),
        );
      }),
    ),
    { initialValue: EMPTY_DRILL },
  );

  /** The container the list is drilled into (server-fetched), or null when not drilled in. */
  containerItem = computed(() => (this.containerFilter() ? this.drillData().container : null));

  /** Items in the current scope, before the inactive (terminal) filter. */
  private scopedItems = computed(() => {
    // Drill-down wins: the container's direct children, fetched server-side (not capped by the
    // all-items page).
    if (this.containerFilter()) return this.drillData().children;

    // Area filter uses RESOLVED area, so a nested item shows under the area it walks up to.
    const all = this.allItems();
    const af = this.areaFilter();
    if (af === 'all') return all;
    if (af === 'unplaced') return all.filter(i => !i.resolvedAreaId);
    return all.filter(i => i.resolvedAreaId === af);
  });

  inactiveCount = computed(() => this.scopedItems().filter(i => isTerminalStatus(i.status)).length);

  visibleItems = computed(() => {
    const scoped = this.scopedItems();
    return this.showInactive() ? scoped : scoped.filter(i => !isTerminalStatus(i.status));
  });

  /** Breadcrumb to the drilled-in container: its resolved path plus its own (current) name. */
  breadcrumb = computed<{ id: string; name: string; kind: LocationSegmentKind }[]>(() => {
    const container = this.containerItem();
    if (!container) return [];
    const path = (container.path ?? []).map(s => ({ id: s.id, name: s.name, kind: s.kind }));
    return [...path, { id: container.id, name: container.name, kind: 'Container' as LocationSegmentKind }];
  });

  sortedVisibleItems = computed(() => {
    const items = [...this.visibleItems()];
    const { id, desc } = this.sortState();
    const dir: 1 | -1 = desc ? -1 : 1;
    items.sort((a, b) => {
      switch (id) {
        case 'name':
          return a.name.localeCompare(b.name) * dir;
        case 'status':
          return itemStatusLabel(a.status).localeCompare(itemStatusLabel(b.status)) * dir;
        case 'location':
          return formatPath(a).localeCompare(formatPath(b)) * dir;
        case 'acquiredOn':
          return compareNullable(a.acquiredOn ?? null, b.acquiredOn ?? null, dir, (x, y) => (x < y ? -1 : 1));
        case 'updatedAt':
          return compareNullable(a.updatedAt ?? null, b.updatedAt ?? null, dir, (x, y) => (x < y ? -1 : 1));
        default:
          return 0;
      }
    });
    return items;
  });

  sortFieldLabel = computed(() =>
    this.sortFields.find(f => f.id === this.sortState().id)?.label ?? 'Sort');

  filterLabel = computed(() => {
    if (this.containerFilter()) return `In: ${this.containerItem()?.name ?? 'container'}`;
    const af = this.areaFilter();
    if (af === 'unplaced') return 'Filter: Unplaced';
    if (af !== 'all') {
      const name = this.areas().find(a => a.id === af)?.name;
      return name ? `Filter: ${name}` : 'Filter';
    }
    return this.showInactive() ? 'Filter: Inactive' : 'Filter';
  });

  /** Picking an area drops any container drill-down (they are mutually exclusive scopes). */
  selectAreaFilter(af: AreaFilter): void {
    this.containerFilter.set(null);
    this.areaFilter.set(af);
  }

  clearContainerFilter(): void {
    this.containerFilter.set(null);
    this.areaFilter.set('all');
  }

  onBreadcrumbSegment(seg: { id: string; kind: LocationSegmentKind }): void {
    if (seg.kind === 'Area') {
      this.containerFilter.set(null);
      this.areaFilter.set(seg.id);
    } else {
      this.containerFilter.set(seg.id);
    }
  }

  resetFilters(): void {
    this.showInactive.set(false);
    this.areaFilter.set('all');
    this.containerFilter.set(null);
  }

  onSortByChange(id: ItemSortId): void {
    const field = this.sortFields.find(f => f.id === id);
    this.sortState.set({ id, desc: field?.defaultDesc ?? false });
  }

  onSortDirChange(desc: boolean): void {
    this.sortState.update(prev => ({ id: prev.id, desc }));
  }

  onChanged(): void {
    this.refreshTrigger.update(v => v + 1);
  }

  onOpen(item: ItemResponse): void {
    this.quickPanelItem.set(item);
    this.quickPanelState.set('open');
  }

  onQuickPanelClosed(): void {
    this.quickPanelState.set('closed');
  }

  onQuickPanelViewContents(item: ItemResponse): void {
    this.onQuickPanelClosed();
    this.onViewContents(item);
  }

  onEdit(item: ItemResponse): void {
    this.selectedItem.set(item);
    this.sheetState.set('open');
  }

  onViewContents(item: ItemResponse): void {
    this.containerFilter.set(item.id);
  }

  onSheetSaved(_: ItemResponse): void {
    this.sheetState.set('closed');
    this.onChanged();
  }

  onSheetClosed(): void {
    this.sheetState.set('closed');
    this.selectedItem.set(null);
  }

  onArchiveRestore(item: ItemResponse): void {
    const terminal = isTerminalStatus(item.status);
    const newStatus = terminal ? 'Active' : 'Archived';
    this.archiving.set(true);
    this.itemApi.patchItem({
      id: item.id,
      body: [replace('/status', newStatus)],
    }).pipe(
      handleMutationError(this.errorHandler, this.archiving, 'Failed to update item.'),
    ).subscribe(() => {
      this.archiving.set(false);
      toast.success(terminal ? 'Item restored.' : 'Item archived.');
      this.onChanged();
    });
  }

  onDeleteRequest(item: ItemResponse): void {
    this.pendingDelete.set(item);
    this.deleteState.set('open');
  }

  confirmDelete(ctx: { close: () => void }): void {
    const item = this.pendingDelete();
    if (!item) return;
    this.deleting.set(true);
    const result$ = this.itemApi.deleteItem({ id: item.id }).pipe(
      handleMutationError(this.errorHandler, this.deleting, 'Failed to delete item.'),
    );
    firstValueFrom(result$).then(() => {
      this.deleting.set(false);
      ctx.close();
      this.pendingDelete.set(null);
      this.onChanged();
      toast.success('Item deleted.');
    }).catch(() => {
      this.deleting.set(false);
    });
  }
}
