import {
  Component,
  ChangeDetectionStrategy,
  computed,
  input,
  output,
} from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTriangleAlert } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmTableImports } from '@/shared/components/spartan/table';
import {
  type CellContext,
  type ColumnDef,
  createAngularTable,
  flexRenderComponent,
  FlexRenderDirective,
  getCoreRowModel,
  injectFlexRenderContext,
} from '@tanstack/angular-table';
import { SortHeaderButton } from '../../documents/list/document-table/sort-header-button';
import { SupplyRowActionsComponent } from './supply-row-actions.component';
import { ItemStatusBadgeComponent } from '../shared/item-status-badge.component';
import { isTerminalStatus } from '../shared/item-status-meta';
import { formatQty } from './supply-shared';
import { escapeHtml } from '@/shared/utils';
import type { SupplyResponse } from '@/api/generated/models/supply-response';

export type SupplySortId = 'name' | 'unit' | 'onHand' | 'status';
export interface SupplySortState {
  readonly id: SupplySortId;
  readonly desc: boolean;
}

interface SupplyTableMeta {
  onOpen: (s: SupplyResponse) => void;
  onEdit: (s: SupplyResponse) => void;
  onArchiveRestore: (s: SupplyResponse) => void;
  onDelete: (s: SupplyResponse) => void;
}

@Component({
  selector: 'app-supply-name-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="block truncate text-sm font-medium">{{ supply.name }}</span>`,
})
export class SupplyNameCell {
  private readonly ctx = injectFlexRenderContext<CellContext<SupplyResponse, unknown>>();
  protected get supply(): SupplyResponse { return this.ctx.row.original; }
}

@Component({
  selector: 'app-supply-onhand-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports],
  providers: [provideIcons({ lucideTriangleAlert })],
  template: `
    <div class="flex items-center gap-2">
      <span class="text-sm tabular-nums">{{ qty }}</span>
      <span class="text-xs text-muted-foreground">{{ supply.unit }}</span>
      @if (supply.isLow) {
        <span
          class="inline-flex h-5 w-fit items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium whitespace-nowrap text-amber-700 dark:bg-amber-950 dark:text-amber-300"
        >
          <ng-icon hlm name="lucideTriangleAlert" size="xs" />
          Low
        </span>
      }
    </div>
  `,
})
export class SupplyOnHandCell {
  private readonly ctx = injectFlexRenderContext<CellContext<SupplyResponse, unknown>>();
  protected get supply(): SupplyResponse { return this.ctx.row.original; }
  protected get qty(): string { return formatQty(this.supply.totalOnHand); }
}

@Component({
  selector: 'app-supply-status-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ItemStatusBadgeComponent],
  template: `<app-item-status-badge [status]="supply.status" />`,
})
export class SupplyStatusCell {
  private readonly ctx = injectFlexRenderContext<CellContext<SupplyResponse, unknown>>();
  protected get supply(): SupplyResponse { return this.ctx.row.original; }
}

@Component({
  selector: 'app-supply-actions-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SupplyRowActionsComponent],
  template: `
    <app-supply-row-actions
      [supply]="supply"
      (edit)="meta.onEdit($event)"
      (archiveRestore)="meta.onArchiveRestore($event)"
      (delete)="meta.onDelete($event)"
    />
  `,
})
export class SupplyActionsCell {
  private readonly ctx = injectFlexRenderContext<CellContext<SupplyResponse, unknown>>();
  protected get meta(): SupplyTableMeta { return this.ctx.table.options.meta as SupplyTableMeta; }
  protected get supply(): SupplyResponse { return this.ctx.row.original; }
}

@Component({
  selector: 'app-supply-table',
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
export class SupplyTableComponent {
  items = input.required<SupplyResponse[]>();
  sortState = input<SupplySortState>({ id: 'name', desc: false });

  open = output<SupplyResponse>();
  edit = output<SupplyResponse>();
  archiveRestore = output<SupplyResponse>();
  delete = output<SupplyResponse>();
  sortChange = output<SupplySortState>();

  private readonly _meta: SupplyTableMeta = {
    onOpen: (s) => this.open.emit(s),
    onEdit: (s) => this.edit.emit(s),
    onArchiveRestore: (s) => this.archiveRestore.emit(s),
    onDelete: (s) => this.delete.emit(s),
  };

  private readonly _columns = computed<ColumnDef<SupplyResponse>[]>(() => [
    {
      accessorKey: 'name',
      id: 'name',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Name' } }),
      cell: () => flexRenderComponent(SupplyNameCell),
    },
    {
      id: 'unit',
      accessorFn: (row) => row.unit,
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Unit' } }),
      cell: (info) => `<span class="text-sm text-muted-foreground">${escapeHtml(info.row.original.unit)}</span>`,
    },
    {
      id: 'onHand',
      accessorFn: (row) => row.totalOnHand,
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'On hand' } }),
      cell: () => flexRenderComponent(SupplyOnHandCell),
    },
    {
      id: 'status',
      accessorFn: (row) => row.status,
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Status' } }),
      cell: () => flexRenderComponent(SupplyStatusCell),
    },
    {
      id: 'actions',
      header: '',
      enableSorting: false,
      cell: () => flexRenderComponent(SupplyActionsCell),
    },
  ]);

  protected readonly table = createAngularTable<SupplyResponse>(() => ({
    data: this.items(),
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
      this.sortChange.emit({ id: first.id as SupplySortId, desc: first.desc });
    },
    state: {
      sorting: [this.sortState()],
    },
    meta: this._meta,
  }));

  private readonly TH_CLASSES: Record<string, string> = { actions: 'w-12' };
  private readonly TD_CLASSES: Record<string, string> = {
    name: 'overflow-hidden',
    unit: 'overflow-hidden',
    actions: 'w-12',
  };

  protected getThClass(columnId: string): string { return this.TH_CLASSES[columnId] ?? ''; }
  protected getTdClass(columnId: string): string { return this.TD_CLASSES[columnId] ?? ''; }

  protected getRowClass(supply: SupplyResponse): string {
    const base = 'cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-inset';
    return isTerminalStatus(supply.status) ? `${base} italic text-muted-foreground` : base;
  }

  protected onRowSpace(event: Event, supply: SupplyResponse): void {
    event.preventDefault(); // keep Space from scrolling the page
    this.open.emit(supply);
  }
}
