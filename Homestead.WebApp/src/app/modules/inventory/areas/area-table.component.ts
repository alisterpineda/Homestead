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
import { RouterLink } from '@angular/router';
import { HlmTableImports } from '@/shared/components/spartan/table';
import {
  type CellContext,
  type ColumnDef,
  flexRenderComponent,
  FlexRenderDirective,
  injectFlexRenderContext,
  injectTable,
  rowSortingFeature,
  tableFeatures,
} from '@tanstack/angular-table';
import { SortHeaderButton } from '../../documents/list/document-table/sort-header-button';
import { AreaRowActionsComponent } from './area-row-actions.component';
import type { AreaResponse } from '@/api/generated/models/area-response';

const areaTableFeatures = tableFeatures({
  rowSortingFeature,
});
type AreaTableFeatures = typeof areaTableFeatures;

export type AreaSortId = 'name' | 'liveItemCount' | 'createdAt';
export interface AreaSortState {
  readonly id: AreaSortId;
  readonly desc: boolean;
}

interface AreaTableMeta {
  onEdit: (area: AreaResponse) => void;
  onDelete: (area: AreaResponse) => void;
  hasChildren: (area: AreaResponse) => boolean;
}

@Component({
  selector: 'app-area-name-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <a
      routerLink="/inventory/items"
      [queryParams]="{ area: area.id }"
      class="block min-w-0 truncate text-sm font-medium"
    >{{ area.name }}</a>
    @if (area.parentAreaName) {
      <span class="block min-w-0 truncate text-xs text-muted-foreground">in {{ area.parentAreaName }}</span>
    }
  `,
})
export class AreaNameCell {
  private readonly ctx = injectFlexRenderContext<CellContext<AreaTableFeatures, AreaResponse, unknown>>();
  protected get area(): AreaResponse { return this.ctx.row.original; }
}

@Component({
  selector: 'app-area-created-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="text-muted-foreground">{{ formatted }}</span>`,
})
export class AreaCreatedCell {
  private readonly locale = inject(LOCALE_ID);
  private readonly ctx = injectFlexRenderContext<CellContext<AreaTableFeatures, AreaResponse, unknown>>();
  protected get formatted(): string {
    const value = this.ctx.row.original.createdAt;
    return value ? formatDate(value, 'mediumDate', this.locale) : '—';
  }
}

@Component({
  selector: 'app-area-actions-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AreaRowActionsComponent],
  template: `
    <app-area-row-actions
      [area]="area"
      [hasChildren]="hasChildren"
      (edit)="onEdit($event)"
      (delete)="onDelete($event)"
    />
  `,
})
export class AreaActionsCell {
  private readonly ctx = injectFlexRenderContext<CellContext<AreaTableFeatures, AreaResponse, unknown>>();
  private get meta(): AreaTableMeta { return this.ctx.table.options.meta as AreaTableMeta; }
  protected get area(): AreaResponse { return this.ctx.row.original; }
  protected get hasChildren(): boolean { return this.meta.hasChildren(this.area); }
  protected onEdit(a: AreaResponse): void { this.meta.onEdit(a); }
  protected onDelete(a: AreaResponse): void { this.meta.onDelete(a); }
}

@Component({
  selector: 'app-area-table',
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
export class AreaTableComponent {
  areas = input.required<AreaResponse[]>();
  sortState = input<AreaSortState>({ id: 'name', desc: false });

  edit = output<AreaResponse>();
  delete = output<AreaResponse>();
  sortChange = output<AreaSortState>();

  /** Area ids that have at least one child — drives the delete gate (parents can't be deleted). */
  private readonly childParentIds = computed(() => {
    const ids = new Set<string>();
    for (const a of this.areas())
      if (a.parentAreaId) ids.add(a.parentAreaId);
    return ids;
  });

  private readonly _meta: AreaTableMeta = {
    onEdit: (a) => this.edit.emit(a),
    onDelete: (a) => this.delete.emit(a),
    hasChildren: (a) => this.childParentIds().has(a.id),
  };

  private readonly _columns = computed<ColumnDef<AreaTableFeatures, AreaResponse>[]>(() => [
    {
      accessorKey: 'name',
      id: 'name',
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Name' } }),
      cell: () => flexRenderComponent(AreaNameCell),
    },
    {
      id: 'liveItemCount',
      accessorFn: (row) => row.liveItemCount,
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Items' } }),
      cell: (info) => `<span class="tabular-nums text-muted-foreground">${info.getValue<number>()}</span>`,
    },
    {
      id: 'createdAt',
      accessorFn: (row) => row.createdAt,
      header: () => flexRenderComponent(SortHeaderButton, { inputs: { header: 'Created' } }),
      cell: () => flexRenderComponent(AreaCreatedCell),
    },
    {
      id: 'actions',
      header: '',
      enableSorting: false,
      cell: () => flexRenderComponent(AreaActionsCell),
    },
  ]);

  protected readonly table = injectTable(() => ({
    features: areaTableFeatures,
    data: this.areas(),
    columns: this._columns(),
    getRowId: (row) => row.id,
    enableSorting: true,
    enableMultiSort: false,
    manualSorting: true,
    onSortingChange: (updater) => {
      const current = [this.sortState()];
      const next = updater instanceof Function ? updater(current) : updater;
      const first = next[0] ?? { id: 'name', desc: false };
      const prev = this.sortState();
      if (prev.id === first.id && prev.desc === first.desc) return;
      this.sortChange.emit({ id: first.id as AreaSortId, desc: first.desc });
    },
    state: {
      sorting: [this.sortState()],
    },
    meta: this._meta,
  }));

  private readonly TH_CLASSES: Record<string, string> = {
    liveItemCount: 'text-right',
    actions: 'w-12',
  };

  private readonly TD_CLASSES: Record<string, string> = {
    name: 'overflow-hidden',
    liveItemCount: 'text-right tabular-nums overflow-hidden',
    createdAt: 'overflow-hidden',
    actions: 'w-12',
  };

  protected getThClass(columnId: string): string {
    return this.TH_CLASSES[columnId] ?? '';
  }

  protected getTdClass(columnId: string): string {
    return this.TD_CLASSES[columnId] ?? '';
  }
}
