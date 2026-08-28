import {
  Component,
  ChangeDetectionStrategy,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  LOCALE_ID,
} from '@angular/core';
import { formatDate } from '@angular/common';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTriangleAlert } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmTableImports } from '@/shared/components/spartan/table';
import {
  type CellContext,
  type ColumnDef,
  type VisibilityState,
  createAngularTable,
  flexRenderComponent,
  FlexRenderDirective,
  getCoreRowModel,
  injectFlexRenderContext,
} from '@tanstack/angular-table';
import { SortHeaderButton } from '../../documents/list/document-table/sort-header-button';
import { InventoryRowActionsComponent } from './inventory-row-actions.component';
import { ItemStatusBadgeComponent } from '../shared/item-status-badge.component';
import { isTerminalStatus, LOW_STOCK_BADGE_CLASS } from '../shared/item-status-meta';
import { formatQty } from '../consumables/consumable-shared';
import { UnitPipe } from '../consumables/unit.pipe';
import { escapeHtml } from '@/shared/utils';
import { TagBadgeComponent } from '@/shared/components/tag-badge/tag-badge.component';
import type { InventoryRow, InventorySortId, InventorySortState } from './inventory-row';
import {
  durableLocationPath,
  stockEntryLocationPath,
  locationPathText,
  renderCrumbs,
  CRUMB_CLASSES,
  PATH_HOST_CLASSES,
  PATH_PREFIX_CLASSES,
  PATH_SEPARATOR,
  PATH_SEPARATOR_CLASSES,
  type LocationPathView,
} from '../shared/location-path';

export type { InventorySortId, InventorySortState } from './inventory-row';

/**
 * The Location cell as a plain HTML string, not <app-location-path>: the row itself is the click
 * target (it opens the quick panel), and a string cell keeps thousands of rows light. The markup
 * mirrors the component span for span, because the branch rule (durableLocationPath /
 * stockEntryLocationPath), the collapse-plus-layout rule (renderCrumbs, CRUMB_CLASSES) and the
 * line literals (PATH_HOST_CLASSES, PATH_PREFIX_CLASSES, PATH_SEPARATOR*) are shared with it — ancestors are muted here exactly as they are on every other surface. The column
 * is narrow by construction, so the middle collapses; the tooltip is always the FULL path.
 */
function renderLocationCell(view: LocationPathView): string {
  const title = escapeHtml(locationPathText(view));
  // A chain rooted at an unplaced container has no area segment — flag that visually. Italic marks
  // a synthetic crumb, and the only one a multi-crumb view can carry is that Unplaced root; plain
  // ancestors and the ellipsis are muted, never italic.
  const syntheticRoot = view.crumbs[0].muted;
  const inner = renderCrumbs(view.crumbs)
    .map(c => {
      const muted = c.muted ? ' text-muted-foreground' : '';
      const italic = syntheticRoot && c.role === 'root' ? ' italic' : '';
      return `<span class="${CRUMB_CLASSES[c.role]}${muted}${italic}">${escapeHtml(c.label)}</span>`;
    })
    .join(`<span class="${PATH_SEPARATOR_CLASSES}" aria-hidden="true">${PATH_SEPARATOR}</span>`);
  const prefix = view.prefix
    ? `<span class="${PATH_PREFIX_CLASSES}">${escapeHtml(view.prefix)}&nbsp;</span>`
    : '';
  return `<span class="${PATH_HOST_CLASSES} text-sm" title="${title}">${prefix}${inner}</span>`;
}

interface InventoryTableMeta {
  onEdit: (row: InventoryRow) => void;
  onViewContents: (row: InventoryRow) => void;
  onViewDetails: (row: InventoryRow) => void;
  onArchiveRestore: (row: InventoryRow) => void;
  onDelete: (row: InventoryRow) => void;
}

@Component({
  selector: 'app-inventory-name-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TagBadgeComponent],
  template: `
    <div class="min-w-0">
      <span class="block truncate text-sm font-medium">{{ row.name }}</span>
      @if (row.durable?.serial) {
        <span class="block truncate text-xs text-muted-foreground">{{ row.durable?.serial }}</span>
      }
      @if (row.tags.length) {
        <div class="mt-1 flex flex-wrap gap-1">
          @for (t of row.tags; track t.id) {
            <app-tag-badge [path]="t.path" [color]="t.effectiveColor" [compact]="true" />
          }
        </div>
      }
    </div>
  `,
})
export class InventoryNameCell {
  private readonly ctx = injectFlexRenderContext<CellContext<InventoryRow, unknown>>();
  protected get row(): InventoryRow { return this.ctx.row.original; }
}

@Component({
  selector: 'app-inventory-status-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ItemStatusBadgeComponent],
  template: `
    <div class="flex min-w-0 items-center gap-2">
      <!-- Dense list: keep a status chip on every row, including Active (quiet secondary tone). -->
      <app-item-status-badge [status]="row.status" [showDefault]="true" />
      @if (row.durable?.lentTo) {
        <!-- Explicit cap: in the auto-layout table the cell grows to fit nowrap content, so
             truncate only engages against a fixed max-width. -->
        <span class="max-w-48 truncate text-xs text-muted-foreground">{{ row.durable?.lentTo }}</span>
      }
    </div>
  `,
})
export class InventoryStatusCell {
  private readonly ctx = injectFlexRenderContext<CellContext<InventoryRow, unknown>>();
  protected get row(): InventoryRow { return this.ctx.row.original; }
}

@Component({
  selector: 'app-inventory-onhand-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, UnitPipe],
  providers: [provideIcons({ lucideTriangleAlert })],
  template: `
    @if (row.kind === 'consumable') {
      <div class="flex items-center gap-2">
        <span class="text-sm tabular-nums">{{ qty }}</span>
        <span class="text-xs text-muted-foreground">{{ row.unit | unit }}</span>
        @if (row.onHandHint) {
          <span class="text-xs text-muted-foreground">{{ row.onHandHint }}</span>
        }
        @if (row.isLow) {
          <span [class]="lowStockBadgeClass">
            <ng-icon hlm name="lucideTriangleAlert" size="xs" />
            Low
          </span>
        }
      </div>
    } @else {
      <span class="text-sm text-muted-foreground">—</span>
    }
  `,
})
export class InventoryOnHandCell {
  private readonly ctx = injectFlexRenderContext<CellContext<InventoryRow, unknown>>();
  protected readonly lowStockBadgeClass = LOW_STOCK_BADGE_CLASS;
  protected get row(): InventoryRow { return this.ctx.row.original; }
  protected get qty(): string { return formatQty(this.row.onHand ?? 0); }
}

@Component({
  selector: 'app-inventory-date-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="text-muted-foreground">{{ formatted }}</span>`,
})
export class InventoryDateCell {
  private readonly locale = inject(LOCALE_ID);
  private readonly ctx = injectFlexRenderContext<CellContext<InventoryRow, string | null | undefined>>();
  protected get formatted(): string {
    const value = this.ctx.getValue();
    return value ? formatDate(value, 'mediumDate', this.locale) : '—';
  }
}

@Component({
  selector: 'app-inventory-actions-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [InventoryRowActionsComponent],
  template: `
    <app-inventory-row-actions
      [row]="row"
      (edit)="meta.onEdit($event)"
      (viewContents)="meta.onViewContents($event)"
      (viewDetails)="meta.onViewDetails($event)"
      (archiveRestore)="meta.onArchiveRestore($event)"
      (delete)="meta.onDelete($event)"
    />
  `,
})
export class InventoryActionsCell {
  private readonly ctx = injectFlexRenderContext<CellContext<InventoryRow, unknown>>();
  protected get meta(): InventoryTableMeta { return this.ctx.table.options.meta as InventoryTableMeta; }
  protected get row(): InventoryRow { return this.ctx.row.original; }
}

@Component({
  selector: 'app-inventory-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmTableImports, FlexRenderDirective],
  template: `
    <div class="overflow-clip rounded-md border">
      <div hlmTableContainer>
        <table hlmTable class="w-full">
          <thead hlmTHead class="bg-background">
            @for (headerGroup of table.getHeaderGroups(); track headerGroup.id) {
              <tr hlmTr>
                @for (header of headerGroup.headers; track header.id) {
                  <th hlmTh [class]="getThClass(header.column.id)" [attr.colSpan]="header.colSpan">
                    @if (!header.isPlaceholder) {
                      <ng-container
                        *flexRender="header.column.columnDef.header; props: header.getContext(); let headerText"
                      >
                        <div [innerHTML]="headerText"></div>
                      </ng-container>
                    }
                  </th>
                }
              </tr>
            }
          </thead>
          <tbody hlmTBody>
            @for (row of table.getRowModel().rows; track row.id) {
              <tr
                hlmTr
                [class]="getRowClass(row.original)"
                tabindex="0"
                (click)="open.emit(row.original)"
                (keydown.enter)="open.emit(row.original)"
                (keydown.space)="onRowSpace($event, row.original)"
              >
                @for (cell of row.getVisibleCells(); track cell.id) {
                  <td hlmTd [class]="getTdClass(cell.column.id)">
                    <ng-container *flexRender="cell.column.columnDef.cell; props: cell.getContext(); let cellText">
                      <div [innerHTML]="cellText"></div>
                    </ng-container>
                  </td>
                }
              </tr>
            }
          </tbody>
        </table>
      </div>
    </div>
  `,
})
export class InventoryTableComponent {
  rows = input.required<InventoryRow[]>();
  sortState = input<InventorySortState>({ id: 'name', desc: false });
  /** Per-column visibility (TanStack {@link VisibilityState}); default `{}` keeps every column shown. */
  columnVisibility = input<VisibilityState>({});

  open = output<InventoryRow>();
  edit = output<InventoryRow>();
  viewContents = output<InventoryRow>();
  viewDetails = output<InventoryRow>();
  archiveRestore = output<InventoryRow>();
  delete = output<InventoryRow>();
  sortChange = output<InventorySortState>();

  private readonly _meta: InventoryTableMeta = {
    onEdit: (r) => this.edit.emit(r),
    onViewContents: (r) => this.viewContents.emit(r),
    onViewDetails: (r) => this.viewDetails.emit(r),
    onArchiveRestore: (r) => this.archiveRestore.emit(r),
    onDelete: (r) => this.delete.emit(r),
  };

  // Mirror the columnVisibility input into TanStack's controlled state. Input → signal via an effect
  // (not read directly in the table options) so a parent change re-applies; onColumnVisibilityChange
  // keeps internal toggles (none today) from desyncing.
  private readonly _columnVisibility = signal<VisibilityState>({});

  constructor() {
    effect(() => this._columnVisibility.set(this.columnVisibility()));
  }

  private readonly _columns = computed<ColumnDef<InventoryRow>[]>(() => [
    {
      accessorKey: 'name',
      id: 'name',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Name' } }),
      cell: () => flexRenderComponent(InventoryNameCell),
    },
    {
      id: 'status',
      accessorFn: (row) => row.status,
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Status' } }),
      cell: () => flexRenderComponent(InventoryStatusCell),
    },
    {
      id: 'location',
      accessorFn: (row) => row.locationText,
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Location' } }),
      cell: (info) => {
        const row = info.row.original;
        const durable = row.durable;
        if (durable) {
          const view = durableLocationPath(durable);
          if (view.crumbs.length === 1 && view.crumbs[0].muted && !view.prefix) {
            return '<span class="text-sm italic text-muted-foreground">Unplaced</span>';
          }
          return renderLocationCell(view);
        }
        // A consumable held in exactly one place reads like a durable's path; spread stock is a
        // count, and stock whose entry resolved to nothing keeps the flat label below.
        const single = row.consumable?.stockLocations;
        if (single?.length === 1 && single[0].path.length > 0) {
          return renderLocationCell(stockEntryLocationPath(single[0]));
        }
        const text = row.locationText;
        if (text === '—' || text === 'Unplaced') {
          return `<span class="text-sm italic text-muted-foreground">${text}</span>`;
        }
        const escaped = escapeHtml(text);
        return `<span class="block truncate text-sm" title="${escaped}">${escaped}</span>`;
      },
    },
    {
      id: 'onHand',
      accessorFn: (row) => row.onHand ?? undefined,
      sortUndefined: 'last',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'On hand' } }),
      cell: () => flexRenderComponent(InventoryOnHandCell),
    },
    {
      id: 'updatedAt',
      accessorFn: (row) => row.updatedAt,
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Updated' } }),
      cell: () => flexRenderComponent(InventoryDateCell),
    },
    {
      id: 'actions',
      header: '',
      enableSorting: false,
      cell: () => flexRenderComponent(InventoryActionsCell),
    },
  ]);

  protected readonly table = createAngularTable<InventoryRow>(() => ({
    data: this.rows(),
    columns: this._columns(),
    getRowId: (row) => row.id,
    enableSorting: true,
    enableMultiSort: false,
    manualSorting: true,
    getCoreRowModel: getCoreRowModel(),
    onSortingChange: (updater) => {
      const current = [this.sortState()];
      const next = updater instanceof Function ? updater(current) : updater;
      const first = next[0] ?? { id: 'name', desc: false };
      const prev = this.sortState();
      if (prev.id === first.id && prev.desc === first.desc) return;
      this.sortChange.emit({ id: first.id as InventorySortId, desc: first.desc });
    },
    onColumnVisibilityChange: (updater) => {
      const next = updater instanceof Function ? updater(this._columnVisibility()) : updater;
      this._columnVisibility.set(next);
    },
    state: {
      sorting: [this.sortState()],
      columnVisibility: this._columnVisibility(),
    },
    meta: this._meta,
  }));

  private readonly TH_CLASSES: Record<string, string> = {
    actions: 'w-12',
  };

  private readonly TD_CLASSES: Record<string, string> = {
    name: 'overflow-hidden',
    location: 'overflow-hidden',
    updatedAt: 'overflow-hidden',
    actions: 'w-12',
  };

  protected getThClass(columnId: string): string {
    return this.TH_CLASSES[columnId] ?? '';
  }

  protected getTdClass(columnId: string): string {
    return this.TD_CLASSES[columnId] ?? '';
  }

  protected getRowClass(row: InventoryRow): string {
    const base = 'cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-inset';
    return isTerminalStatus(row.status) ? `${base} italic text-muted-foreground` : base;
  }

  protected onRowSpace(event: Event, row: InventoryRow): void {
    event.preventDefault(); // keep Space from scrolling the page
    this.open.emit(row);
  }
}
