import {
  Component,
  ChangeDetectionStrategy,
  effect,
  input,
  output,
  signal,
} from '@angular/core';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmBadgeImports } from '@/shared/components/spartan/badge';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePencil, lucideTrash2, lucideEllipsisVertical } from '@ng-icons/lucide';
import {
  type CellContext,
  type ColumnDef,
  type RowSelectionState,
  type SortingState,
  flexRenderComponent,
  FlexRenderDirective,
  injectFlexRenderContext,
  injectTable,
  rowSelectionFeature,
  rowSortingFeature,
  tableFeatures,
} from '@tanstack/angular-table';
import { SortHeaderButton } from '../../documents/list/document-table/sort-header-button';
import { TableHeadSelection, TableRowSelection } from '@/shared/components/table/selection-column';
import type { CommodityPriceResponse } from '@/api/generated/models/commodity-price-response';
import type { CommodityPriceSource } from '@/api/generated/models/commodity-price-source';
import type { CommodityPriceSortBy } from '@/api/generated/models/commodity-price-sort-by';
import type { SortDirection } from '@/api/generated/models/sort-direction';

const commodityPricesTableFeatures = tableFeatures({
  rowSortingFeature,
  rowSelectionFeature,
});
type CommodityPricesTableFeatures = typeof commodityPricesTableFeatures;

const COLUMN_ID_TO_SORT_KEY: Record<string, CommodityPriceSortBy> = {
  priceAt: 'PriceAt',
  quote: 'Quote',
  rate: 'Rate',
  source: 'Source',
};

const SORT_KEY_TO_COLUMN_ID: Record<CommodityPriceSortBy, string> = {
  PriceAt: 'priceAt',
  Quote: 'quote',
  Rate: 'rate',
  Source: 'source',
};

interface PricesTableMeta {
  formatPriceAt: (iso: string) => string;
  quoteSymbol: (id: string) => string;
  formatRate: (price: CommodityPriceResponse) => string;
  isSourceDisabled: (source: CommodityPriceSource) => boolean;
  onEdit: (price: CommodityPriceResponse) => void;
  onDelete: (price: CommodityPriceResponse) => void;
}

@Component({
  selector: 'app-commodity-price-when-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="tabular-nums">{{ formatted }}</span>`,
})
export class CommodityPriceWhenCell {
  private readonly ctx = injectFlexRenderContext<CellContext<CommodityPricesTableFeatures, CommodityPriceResponse, unknown>>();
  protected get formatted(): string {
    const meta = this.ctx.table.options.meta as PricesTableMeta;
    return meta.formatPriceAt(this.ctx.row.original.priceAt);
  }
}

@Component({
  selector: 'app-commodity-price-quote-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span>{{ symbol }}</span>`,
})
export class CommodityPriceQuoteCell {
  private readonly ctx = injectFlexRenderContext<CellContext<CommodityPricesTableFeatures, CommodityPriceResponse, unknown>>();
  protected get symbol(): string {
    const meta = this.ctx.table.options.meta as PricesTableMeta;
    return meta.quoteSymbol(this.ctx.row.original.quoteCurrencyId);
  }
}

@Component({
  selector: 'app-commodity-price-rate-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="tabular-nums">{{ formatted }}</span>`,
})
export class CommodityPriceRateCell {
  private readonly ctx = injectFlexRenderContext<CellContext<CommodityPricesTableFeatures, CommodityPriceResponse, unknown>>();
  protected get formatted(): string {
    const meta = this.ctx.table.options.meta as PricesTableMeta;
    return meta.formatRate(this.ctx.row.original);
  }
}

@Component({
  selector: 'app-commodity-price-source-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmBadgeImports],
  template: `
    <span class="inline-flex items-center gap-1.5" [class.opacity-60]="isDisabled">
      @switch (price.source) {
        @case ('Manual') {
          <span hlmBadge variant="outline">Manual</span>
        }
        @case ('Transaction') {
          <span hlmBadge variant="secondary" title="Edited via the source transaction.">Transaction</span>
        }
        @case ('BankOfCanada') {
          <span hlmBadge variant="secondary" title="Imported from the Bank of Canada.">Bank of Canada</span>
        }
        @case ('AlphaVantage') {
          <span hlmBadge variant="secondary" title="Imported from Alpha Vantage.">Alpha Vantage</span>
        }
        @default {
          <span hlmBadge variant="secondary">{{ price.source }}</span>
        }
      }
      @if (isDisabled) {
        <span
          hlmBadge
          variant="outline"
          class="text-muted-foreground"
          title="This source is disabled. Prices from it are not contributing to net worth."
        >Not contributing</span>
      }
    </span>
  `,
})
export class CommodityPriceSourceCell {
  private readonly ctx = injectFlexRenderContext<CellContext<CommodityPricesTableFeatures, CommodityPriceResponse, unknown>>();
  protected get price(): CommodityPriceResponse { return this.ctx.row.original; }
  protected get isDisabled(): boolean {
    const meta = this.ctx.table.options.meta as PricesTableMeta;
    return meta.isSourceDisabled(this.price.source);
  }
}

@Component({
  selector: 'app-commodity-price-actions-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmButton, HlmDropdownMenuImports, NgIcon],
  providers: [provideIcons({ lucidePencil, lucideTrash2, lucideEllipsisVertical })],
  template: `
    @if (price.source !== 'Transaction') {
      <button
        hlmBtn
        variant="ghost"
        size="icon-sm"
        [hlmDropdownMenuTrigger]="menu"
        aria-label="Price actions"
      >
        <ng-icon name="lucideEllipsisVertical" class="text-[length:--spacing(4)]" />
      </button>
      <ng-template #menu>
        <hlm-dropdown-menu class="w-40">
          <hlm-dropdown-menu-group>
            @if (price.source === 'Manual') {
              <button hlmDropdownMenuItem (triggered)="onEdit()">
                <ng-icon name="lucidePencil" class="text-[length:--spacing(4)]" />
                Edit
              </button>
              <hlm-dropdown-menu-separator />
            }
            <button hlmDropdownMenuItem variant="destructive" (triggered)="onDelete()">
              <ng-icon name="lucideTrash2" class="text-[length:--spacing(4)]" />
              Delete
            </button>
          </hlm-dropdown-menu-group>
        </hlm-dropdown-menu>
      </ng-template>
    }
  `,
})
export class CommodityPriceActionsCell {
  private readonly ctx = injectFlexRenderContext<CellContext<CommodityPricesTableFeatures, CommodityPriceResponse, unknown>>();
  private get meta(): PricesTableMeta { return this.ctx.table.options.meta as PricesTableMeta; }
  protected get price(): CommodityPriceResponse { return this.ctx.row.original; }
  protected onEdit(): void { this.meta.onEdit(this.price); }
  protected onDelete(): void { this.meta.onDelete(this.price); }
}

@Component({
  selector: 'app-commodity-prices-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmTableImports, FlexRenderDirective],
  template: `
    <div class="overflow-clip rounded-md border border-border">
      <div hlmTableContainer>
        <table hlmTable class="w-full">
          <thead hlmTHead>
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
              <tr hlmTr>
                <!-- No columnVisibilityFeature here, so all cells render; switch to row.getVisibleCells() if columns become hideable. -->
                @for (cell of row.getAllCells(); track cell.id) {
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
export class CommodityPricesTableComponent {
  prices = input.required<CommodityPriceResponse[]>();
  sortBy = input<CommodityPriceSortBy | null | undefined>(null);
  sortDir = input<SortDirection | null | undefined>(null);
  selectedIds = input<Set<string>>();
  disabledSources = input<Set<CommodityPriceSource>>(new Set());
  formatPriceAt = input.required<(iso: string) => string>();
  quoteSymbol = input.required<(id: string) => string>();
  formatRate = input.required<(price: CommodityPriceResponse) => string>();

  edit = output<CommodityPriceResponse>();
  delete = output<CommodityPriceResponse>();
  sortChange = output<{ sortBy: CommodityPriceSortBy; sortDir: SortDirection }>();
  selectionChange = output<Set<string>>();

  private readonly _sorting = signal<SortingState>([{ id: 'priceAt', desc: true }]);
  private readonly _rowSelection = signal<RowSelectionState>({});

  private readonly _meta: PricesTableMeta = {
    formatPriceAt: (iso) => this.formatPriceAt()(iso),
    quoteSymbol: (id) => this.quoteSymbol()(id),
    formatRate: (p) => this.formatRate()(p),
    isSourceDisabled: (src) => this.disabledSources().has(src),
    onEdit: (p) => this.edit.emit(p),
    onDelete: (p) => this.delete.emit(p),
  };

  private readonly _columns: ColumnDef<CommodityPricesTableFeatures, CommodityPriceResponse>[] = [
    {
      id: 'select',
      header: () => flexRenderComponent(TableHeadSelection),
      cell: () => flexRenderComponent(TableRowSelection),
      enableSorting: false,
    },
    {
      id: 'priceAt',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'When' } }),
      cell: () => flexRenderComponent(CommodityPriceWhenCell),
    },
    {
      id: 'quote',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Quote' } }),
      cell: () => flexRenderComponent(CommodityPriceQuoteCell),
    },
    {
      id: 'rate',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Rate' } }),
      cell: () => flexRenderComponent(CommodityPriceRateCell),
    },
    {
      id: 'source',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Source' } }),
      cell: () => flexRenderComponent(CommodityPriceSourceCell),
    },
    {
      id: 'actions',
      header: '',
      enableSorting: false,
      cell: () => flexRenderComponent(CommodityPriceActionsCell),
    },
  ];

  protected readonly table = injectTable(() => ({
    features: commodityPricesTableFeatures,
    data: this.prices(),
    columns: this._columns,
    getRowId: (row) => row.id,
    manualSorting: true,
    enableSortingRemoval: false,
    enableRowSelection: (row) => row.original.source !== 'Transaction',
    onSortingChange: (updater) => {
      const next = updater instanceof Function ? updater(this._sorting()) : updater;
      this._sorting.set(next);
      if (next.length > 0) {
        const sort = next[0];
        const sortKey = COLUMN_ID_TO_SORT_KEY[sort.id];
        if (sortKey) {
          this.sortChange.emit({ sortBy: sortKey, sortDir: sort.desc ? 'Desc' : 'Asc' });
        }
      }
    },
    onRowSelectionChange: (updater) => {
      const next = updater instanceof Function ? updater(this._rowSelection()) : updater;
      this._rowSelection.set(next);
      this.selectionChange.emit(new Set(Object.keys(next).filter(k => next[k])));
    },
    state: {
      sorting: this._sorting(),
      rowSelection: this._rowSelection(),
    },
    meta: this._meta,
  }));

  private readonly TH_CLASSES: Record<string, string> = {
    select: 'w-10',
    rate: 'text-right',
    actions: 'w-12',
  };

  private readonly TD_CLASSES: Record<string, string> = {
    select: 'w-10',
    priceAt: 'tabular-nums',
    rate: 'text-right tabular-nums',
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
      const sortBy = this.sortBy() ?? 'PriceAt';
      const sortDir = this.sortDir() ?? 'Desc';
      const id = SORT_KEY_TO_COLUMN_ID[sortBy];
      const desc = sortDir === 'Desc';
      const cur = this._sorting()[0];
      if (!cur || cur.id !== id || cur.desc !== desc) {
        this._sorting.set([{ id, desc }]);
      }
    });

    effect(() => {
      const ids = this.selectedIds();
      if (ids) {
        const selection: RowSelectionState = {};
        ids.forEach(id => { selection[id] = true; });
        this._rowSelection.set(selection);
      } else {
        this._rowSelection.set({});
      }
    });
  }
}
