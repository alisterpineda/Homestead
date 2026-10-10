import {
  Component,
  ChangeDetectionStrategy,
  effect,
  input,
  output,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmBadgeImports } from '@/shared/components/spartan/badge';
import {
  type CellContext,
  type ColumnDef,
  type Row,
  type SortingState,
  createSortedRowModel,
  flexRenderComponent,
  FlexRenderDirective,
  injectFlexRenderContext,
  injectTable,
  rowSortingFeature,
  sortFn_alphanumeric,
  sortFn_text,
  tableFeatures,
} from '@tanstack/angular-table';
import { SortHeaderButton } from '../../documents/list/document-table/sort-header-button';
import { CommodityRowActionsComponent } from './commodity-row-actions.component';
import { formatRelativeAge } from '@/shared/utils/relative-age';
import { escapeHtml } from '@/shared/utils';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

const commodityTableFeatures = tableFeatures({
  rowSortingFeature,
  sortedRowModel: createSortedRowModel(),
  sortFns: { alphanumeric: sortFn_alphanumeric, text: sortFn_text },
});
type CommodityTableFeatures = typeof commodityTableFeatures;

export type CommoditySortBy =
  | 'symbol'
  | 'kind'
  | 'exchange'
  | 'name'
  | 'displayPrecision'
  | 'priceCount'
  | 'latestPriceAt';

export type CommoditySortDir = 'asc' | 'desc';

interface CommodityTableMeta {
  onEdit: (commodity: CommodityResponse) => void;
  onDelete: (commodity: CommodityResponse) => void;
}

type CommodityRow = Row<CommodityTableFeatures, CommodityResponse>;

function bySymbol(a: CommodityRow, b: CommodityRow, primary: number): number {
  return primary !== 0 ? primary : a.original.symbol.localeCompare(b.original.symbol);
}

@Component({
  selector: 'app-commodity-name-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <a
      [routerLink]="['/finances/commodities', commodity.id]"
      class="block min-w-0 truncate text-sm font-medium"
      (click)="$event.stopPropagation()"
    >{{ commodity.symbol }}</a>
  `,
})
export class CommodityNameCell {
  private readonly ctx = injectFlexRenderContext<CellContext<CommodityTableFeatures, CommodityResponse, unknown>>();
  protected get commodity(): CommodityResponse { return this.ctx.row.original; }
}

@Component({
  selector: 'app-commodity-kind-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmBadgeImports],
  template: `
    <span hlmBadge [variant]="commodity.kind === 'Security' ? 'secondary' : 'outline'">
      {{ commodity.kind }}
    </span>
  `,
})
export class CommodityKindCell {
  private readonly ctx = injectFlexRenderContext<CellContext<CommodityTableFeatures, CommodityResponse, unknown>>();
  protected get commodity(): CommodityResponse { return this.ctx.row.original; }
}

@Component({
  selector: 'app-commodity-exchange-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmBadgeImports],
  template: `
    @if (commodity.exchange) {
      <span hlmBadge variant="outline">{{ commodity.exchange }}</span>
    } @else {
      <span class="text-muted-foreground">—</span>
    }
  `,
})
export class CommodityExchangeCell {
  private readonly ctx = injectFlexRenderContext<CellContext<CommodityTableFeatures, CommodityResponse, unknown>>();
  protected get commodity(): CommodityResponse { return this.ctx.row.original; }
}

@Component({
  selector: 'app-commodity-latest-price-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="text-muted-foreground">{{ formatted }}</span>`,
})
export class CommodityLatestPriceCell {
  private readonly ctx = injectFlexRenderContext<CellContext<CommodityTableFeatures, CommodityResponse, unknown>>();
  protected get formatted(): string {
    const c = this.ctx.row.original;
    if (c.kind === 'Currency') return '—';
    return c.latestPriceAt ? formatRelativeAge(c.latestPriceAt) : '—';
  }
}

@Component({
  selector: 'app-commodity-actions-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommodityRowActionsComponent],
  template: `
    @if (!commodity.isSystemManaged) {
      <app-commodity-row-actions
        [commodity]="commodity"
        (edit)="onEdit($event)"
        (delete)="onDelete($event)"
      />
    }
  `,
})
export class CommodityActionsCell {
  private readonly ctx = injectFlexRenderContext<CellContext<CommodityTableFeatures, CommodityResponse, unknown>>();
  private get meta(): CommodityTableMeta { return this.ctx.table.options.meta as CommodityTableMeta; }
  protected get commodity(): CommodityResponse { return this.ctx.row.original; }
  protected onEdit(c: CommodityResponse): void { this.meta.onEdit(c); }
  protected onDelete(c: CommodityResponse): void { this.meta.onDelete(c); }
}

@Component({
  selector: 'app-commodity-table',
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
                  <th
                    hlmTh
                    [class]="getThClass(header.column.id)"
                    [attr.colSpan]="header.colSpan"
                  >
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
                class="cursor-pointer"
                (click)="rowClick.emit(row.original)"
              >
                <!-- No columnVisibilityFeature here, so all cells render; switch to row.getVisibleCells() if columns become hideable. -->
                @for (cell of row.getAllCells(); track cell.id) {
                  <td
                    hlmTd
                    [class]="getTdClass(cell.column.id)"
                  >
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
export class CommodityTableComponent {
  commodities = input.required<CommodityResponse[]>();
  sortBy = input<CommoditySortBy | null | undefined>(null);
  sortDir = input<CommoditySortDir | null | undefined>(null);

  rowClick = output<CommodityResponse>();
  edit = output<CommodityResponse>();
  delete = output<CommodityResponse>();
  sortChange = output<{ sortBy: CommoditySortBy; sortDir: CommoditySortDir }>();

  private readonly _sorting = signal<SortingState>([{ id: 'symbol', desc: false }]);

  private readonly _meta: CommodityTableMeta = {
    onEdit: (c) => this.edit.emit(c),
    onDelete: (c) => this.delete.emit(c),
  };

  private readonly _columns: ColumnDef<CommodityTableFeatures, CommodityResponse>[] = [
    {
      accessorKey: 'symbol',
      id: 'symbol',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Symbol' } }),
      cell: () => flexRenderComponent(CommodityNameCell),
    },
    {
      accessorKey: 'kind',
      id: 'kind',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Kind' } }),
      cell: () => flexRenderComponent(CommodityKindCell),
      sortFn: (a, b) => bySymbol(a, b, a.original.kind.localeCompare(b.original.kind)),
    },
    {
      id: 'exchange',
      accessorFn: (row) => row.exchange ?? undefined,
      sortUndefined: 'last',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Exchange' } }),
      cell: () => flexRenderComponent(CommodityExchangeCell),
      sortFn: (a, b) => bySymbol(a, b, (a.original.exchange ?? '').localeCompare(b.original.exchange ?? '')),
    },
    {
      accessorKey: 'name',
      id: 'name',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Name' } }),
      cell: (info) => `<span class="text-muted-foreground">${escapeHtml(info.getValue<string>())}</span>`,
      sortFn: (a, b) => bySymbol(a, b, a.original.name.localeCompare(b.original.name)),
    },
    {
      accessorKey: 'displayPrecision',
      id: 'displayPrecision',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Display precision' } }),
      cell: (info) => `<span class="tabular-nums">${info.getValue<number>()}</span>`,
      sortFn: (a, b) => bySymbol(a, b, a.original.displayPrecision - b.original.displayPrecision),
    },
    {
      id: 'priceCount',
      accessorFn: (row) => row.kind === 'Currency' ? undefined : row.priceCount,
      sortUndefined: 'last',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: '# prices' } }),
      cell: (info) => {
        const value = info.getValue<number | undefined>();
        return value === undefined
          ? '<span class="text-muted-foreground">—</span>'
          : `<span class="tabular-nums">${value}</span>`;
      },
      sortFn: (a, b) => bySymbol(a, b, a.original.priceCount - b.original.priceCount),
    },
    {
      id: 'latestPriceAt',
      accessorFn: (row) => row.kind === 'Currency' || !row.latestPriceAt ? undefined : row.latestPriceAt,
      sortUndefined: 'last',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Latest price' } }),
      cell: () => flexRenderComponent(CommodityLatestPriceCell),
      sortFn: (a, b) => bySymbol(a, b, (a.original.latestPriceAt ?? '').localeCompare(b.original.latestPriceAt ?? '')),
    },
    {
      id: 'actions',
      header: '',
      enableSorting: false,
      cell: () => flexRenderComponent(CommodityActionsCell),
    },
  ];

  protected readonly table = injectTable(() => ({
    features: commodityTableFeatures,
    data: this.commodities(),
    columns: this._columns,
    getRowId: (row) => row.id,
    enableSorting: true,
    enableSortingRemoval: false,
    onSortingChange: (updater) => {
      const next = updater instanceof Function ? updater(this._sorting()) : updater;
      this._sorting.set(next);
      if (next.length > 0) {
        const sort = next[0];
        this.sortChange.emit({
          sortBy: sort.id as CommoditySortBy,
          sortDir: sort.desc ? 'desc' : 'asc',
        });
      }
    },
    state: {
      sorting: this._sorting(),
    },
    meta: this._meta,
  }));

  private readonly TH_CLASSES: Record<string, string> = {
    displayPrecision: 'text-right',
    priceCount: 'text-right',
    actions: 'w-12',
  };

  private readonly TD_CLASSES: Record<string, string> = {
    symbol: 'overflow-hidden font-medium',
    kind: 'overflow-hidden',
    exchange: 'overflow-hidden',
    name: 'overflow-hidden',
    displayPrecision: 'text-right tabular-nums overflow-hidden',
    priceCount: 'text-right tabular-nums overflow-hidden',
    latestPriceAt: 'overflow-hidden',
    actions: 'w-12',
  };

  protected getThClass(columnId: string): string {
    return this.TH_CLASSES[columnId] ?? '';
  }

  protected getTdClass(columnId: string): string {
    return this.TD_CLASSES[columnId] ?? '';
  }

  constructor() {
    effect(() => {
      const sortBy = this.sortBy();
      const sortDir = this.sortDir();
      const id = sortBy ?? 'symbol';
      const desc = sortBy ? sortDir === 'desc' : false;
      const cur = this._sorting()[0];
      if (!cur || cur.id !== id || cur.desc !== desc) {
        this._sorting.set([{ id, desc }]);
      }
    });
  }
}
