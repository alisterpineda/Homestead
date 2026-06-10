import {
  Component,
  ChangeDetectionStrategy,
  computed,
  inject,
  input,
  output,
  LOCALE_ID,
} from '@angular/core';
import { formatDate } from '@angular/common';
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
import { ItemRowActionsComponent } from './item-row-actions.component';
import { ItemStatusBadgeComponent } from '../shared/item-status-badge.component';
import { isTerminalStatus } from '../shared/item-status-meta';
import { escapeHtml } from '@/shared/utils';
import type { ItemResponse } from '@/api/generated/models/item-response';

export type ItemSortId = 'name' | 'status' | 'location' | 'acquiredOn' | 'updatedAt';
export interface ItemSortState {
  readonly id: ItemSortId;
  readonly desc: boolean;
}

interface ItemTableMeta {
  onEdit: (item: ItemResponse) => void;
  onArchiveRestore: (item: ItemResponse) => void;
  onDelete: (item: ItemResponse) => void;
}

@Component({
  selector: 'app-item-status-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ItemStatusBadgeComponent],
  template: `<app-item-status-badge [status]="item.status" />`,
})
export class ItemStatusCell {
  private readonly ctx = injectFlexRenderContext<CellContext<ItemResponse, unknown>>();
  protected get item(): ItemResponse { return this.ctx.row.original; }
}

@Component({
  selector: 'app-item-date-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="text-muted-foreground">{{ formatted }}</span>`,
})
export class ItemDateCell {
  private readonly locale = inject(LOCALE_ID);
  private readonly ctx = injectFlexRenderContext<CellContext<ItemResponse, string | null | undefined>>();
  protected get formatted(): string {
    const value = this.ctx.getValue();
    return value ? formatDate(value, 'mediumDate', this.locale) : '—';
  }
}

@Component({
  selector: 'app-item-actions-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ItemRowActionsComponent],
  template: `
    <app-item-row-actions
      [item]="item"
      (edit)="onEdit($event)"
      (archiveRestore)="onArchiveRestore($event)"
      (delete)="onDelete($event)"
    />
  `,
})
export class ItemActionsCell {
  private readonly ctx = injectFlexRenderContext<CellContext<ItemResponse, unknown>>();
  private get meta(): ItemTableMeta { return this.ctx.table.options.meta as ItemTableMeta; }
  protected get item(): ItemResponse { return this.ctx.row.original; }
  protected onEdit(i: ItemResponse): void { this.meta.onEdit(i); }
  protected onArchiveRestore(i: ItemResponse): void { this.meta.onArchiveRestore(i); }
  protected onDelete(i: ItemResponse): void { this.meta.onDelete(i); }
}

@Component({
  selector: 'app-item-table',
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
              <tr hlmTr [class]="getRowClass(row.original)">
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
export class ItemTableComponent {
  items = input.required<ItemResponse[]>();
  sortState = input<ItemSortState>({ id: 'name', desc: false });

  edit = output<ItemResponse>();
  archiveRestore = output<ItemResponse>();
  delete = output<ItemResponse>();
  sortChange = output<ItemSortState>();

  private readonly _meta: ItemTableMeta = {
    onEdit: (i) => this.edit.emit(i),
    onArchiveRestore: (i) => this.archiveRestore.emit(i),
    onDelete: (i) => this.delete.emit(i),
  };

  private readonly _columns = computed<ColumnDef<ItemResponse>[]>(() => [
    {
      accessorKey: 'name',
      id: 'name',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Name' } }),
      cell: (info) => {
        const item = info.row.original;
        const name = `<span class="text-sm font-medium">${escapeHtml(item.name)}</span>`;
        const serial = item.serial
          ? `<span class="block truncate text-xs text-muted-foreground">${escapeHtml(item.serial)}</span>`
          : '';
        return `<div class="min-w-0">${name}${serial}</div>`;
      },
    },
    {
      id: 'status',
      accessorFn: (row) => row.status,
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Status' } }),
      cell: () => flexRenderComponent(ItemStatusCell),
    },
    {
      id: 'location',
      accessorFn: (row) => row.areaName ?? '',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Location' } }),
      cell: (info) => {
        const name = info.row.original.areaName;
        return name
          ? `<span class="text-sm">${escapeHtml(name)}</span>`
          : '<span class="text-sm italic text-muted-foreground">Unplaced</span>';
      },
    },
    {
      id: 'acquiredOn',
      accessorFn: (row) => row.acquiredOn ?? undefined,
      sortUndefined: 'last',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Acquired on' } }),
      cell: () => flexRenderComponent(ItemDateCell),
    },
    {
      id: 'updatedAt',
      accessorFn: (row) => row.updatedAt,
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Updated' } }),
      cell: () => flexRenderComponent(ItemDateCell),
    },
    {
      id: 'actions',
      header: '',
      enableSorting: false,
      cell: () => flexRenderComponent(ItemActionsCell),
    },
  ]);

  protected readonly table = createAngularTable<ItemResponse>(() => ({
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
      this.sortChange.emit({ id: first.id as ItemSortId, desc: first.desc });
    },
    state: {
      sorting: [this.sortState()],
    },
    meta: this._meta,
  }));

  private readonly TH_CLASSES: Record<string, string> = {
    actions: 'w-12',
  };

  private readonly TD_CLASSES: Record<string, string> = {
    name: 'overflow-hidden',
    location: 'overflow-hidden',
    acquiredOn: 'overflow-hidden',
    updatedAt: 'overflow-hidden',
    actions: 'w-12',
  };

  protected getThClass(columnId: string): string {
    return this.TH_CLASSES[columnId] ?? '';
  }

  protected getTdClass(columnId: string): string {
    return this.TD_CLASSES[columnId] ?? '';
  }

  protected getRowClass(item: ItemResponse): string {
    return isTerminalStatus(item.status) ? 'italic text-muted-foreground' : '';
  }
}
