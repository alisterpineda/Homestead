import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
} from '@angular/core';
import { Router } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, firstValueFrom } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucidePlus,
  lucideBoxes,
  lucideCircleAlert,
  lucideListFilter,
  lucideArrowUp,
  lucideArrowDown,
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
import { InventorySupplyApiService } from '@/api/services/inventory-supply-api.service';
import { SupplyTableComponent, type SupplySortId, type SupplySortState } from './supply-table.component';
import { CreateSupplyDialogComponent } from './create-supply-dialog/create-supply-dialog.component';
import { EditSupplySheetComponent } from './edit-supply-sheet/edit-supply-sheet.component';
import { itemStatusLabel, isTerminalStatus } from '../shared/item-status-meta';
import { replace } from '@/shared/json-patch';
import { handleQueryError, handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { SupplyResponse } from '@/api/generated/models/supply-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';

interface SupplySortFieldOption {
  readonly id: SupplySortId;
  readonly label: string;
  readonly defaultDesc: boolean;
}

@Component({
  selector: 'app-supply-list-page',
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
    SupplyTableComponent,
    CreateSupplyDialogComponent,
    EditSupplySheetComponent,
  ],
  providers: [provideIcons({
    lucidePlus,
    lucideBoxes,
    lucideCircleAlert,
    lucideListFilter,
    lucideArrowUp,
    lucideArrowDown,
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
            <div hlmEmptyTitle>Failed to load supplies</div>
            <div hlmEmptyDescription>There was an error loading your supplies. Please try again.</div>
          </div>
        </div>
      }

      @if (data() !== null) {
        @if (allSupplies().length === 0) {
          <div hlmEmpty>
            <div hlmEmptyHeader>
              <div hlmEmptyMedia variant="icon">
                <ng-icon name="lucideBoxes" />
              </div>
              <div hlmEmptyTitle>No supplies yet</div>
              <div hlmEmptyDescription>Track consumables you keep in bulk and see at a glance which are running low.</div>
            </div>
            <div hlmEmptyContent>
              <app-create-supply-dialog (created)="onChanged()">
                <ng-icon hlm name="lucidePlus" size="sm" />
                New supply
              </app-create-supply-dialog>
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
                <div hlmPopoverContent *hlmPopoverPortal class="w-[240px] p-0" align="start">
                  <div class="flex items-center justify-between px-3 py-2">
                    <span class="text-sm font-medium">Filters</span>
                    @if (showInactive() || lowStockOnly()) {
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
                  <div class="space-y-1 p-2">
                    <label class="flex items-center gap-2 px-1 py-1.5 text-sm cursor-pointer" hlmLabel>
                      <hlm-checkbox
                        [checked]="lowStockOnly()"
                        (checkedChange)="lowStockOnly.set($event === true)"
                        [aria-label]="'Low stock only'"
                      />
                      <span>Low stock only ({{ lowCount() }})</span>
                    </label>
                    <label class="flex items-center gap-2 px-1 py-1.5 text-sm cursor-pointer" hlmLabel>
                      <hlm-checkbox
                        [checked]="showInactive()"
                        (checkedChange)="showInactive.set($event === true)"
                        [aria-label]="'Show archived supplies'"
                      />
                      <span>Show archived ({{ inactiveCount() }})</span>
                    </label>
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
            <app-create-supply-dialog triggerSize="sm" (created)="onChanged()">
              <ng-icon hlm name="lucidePlus" size="sm" />
              New supply
            </app-create-supply-dialog>
          </div>

          @if (sortedVisibleSupplies().length === 0) {
            <div hlmEmpty>
              <div hlmEmptyHeader>
                <div hlmEmptyMedia variant="icon">
                  <ng-icon name="lucideBoxes" />
                </div>
                <div hlmEmptyTitle>No supplies match</div>
                <div hlmEmptyDescription>No supplies match the current filters.</div>
              </div>
            </div>
          } @else {
            <app-supply-table
              [items]="sortedVisibleSupplies()"
              [sortState]="sortState()"
              (sortChange)="sortState.set($event)"
              (open)="onOpen($event)"
              (edit)="onEdit($event)"
              (archiveRestore)="onArchiveRestore($event)"
              (delete)="onDeleteRequest($event)"
            />
          }
        }
      }
    </div>

    <app-edit-supply-sheet
      [supply]="selectedSupply()"
      [state]="sheetState()"
      (saved)="onSheetSaved()"
      (closed)="onSheetClosed()"
    />

    <hlm-alert-dialog [state]="deleteState()" (stateChanged)="deleteState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Delete {{ pendingDelete()?.name ?? 'supply' }}?</h2>
          <p hlmAlertDialogDescription>
            This permanently removes the supply. Supplies with stock history cannot be deleted — archive them instead.
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
export class SupplyListPageComponent {
  private supplyApi = inject(InventorySupplyApiService);
  private errorHandler = inject(ErrorHandlerService);
  private router = inject(Router);

  readonly sortFields: readonly SupplySortFieldOption[] = [
    { id: 'name', label: 'Name', defaultDesc: false },
    { id: 'unit', label: 'Unit', defaultDesc: false },
    { id: 'onHand', label: 'On hand', defaultDesc: true },
    { id: 'status', label: 'Status', defaultDesc: false },
  ];

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  showInactive = signal(false);
  lowStockOnly = signal(false);

  sortState = signal<SupplySortState>({ id: 'name', desc: false });

  selectedSupply = signal<SupplyResponse | null>(null);
  sheetState = signal<BrnDialogState>('closed');

  pendingDelete = signal<SupplyResponse | null>(null);
  deleteState = signal<BrnDialogState>('closed');
  deleting = signal(false);
  archiving = signal(false);

  data = toSignal<SupplyResponse[] | null, SupplyResponse[] | null>(
    toObservable(this.refreshTrigger).pipe(
      switchMap(() => {
        this.loading.set(true);
        this.error.set(false);
        return this.supplyApi.getSupplies({ Page: 1, PageSize: 200, IncludeInactive: true }).pipe(
          map((r) => {
            this.loading.set(false);
            return (r.items ?? []) as SupplyResponse[] | null;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  allSupplies = computed(() => this.data() ?? []);

  inactiveCount = computed(() => this.allSupplies().filter((s) => isTerminalStatus(s.status)).length);
  lowCount = computed(() => this.allSupplies().filter((s) => s.isLow && !isTerminalStatus(s.status)).length);

  visibleSupplies = computed(() => {
    let list = this.allSupplies();
    if (!this.showInactive()) list = list.filter((s) => !isTerminalStatus(s.status));
    if (this.lowStockOnly()) list = list.filter((s) => s.isLow);
    return list;
  });

  sortedVisibleSupplies = computed(() => {
    const items = [...this.visibleSupplies()];
    const { id, desc } = this.sortState();
    const dir: 1 | -1 = desc ? -1 : 1;
    items.sort((a, b) => {
      switch (id) {
        case 'name':
          return a.name.localeCompare(b.name) * dir;
        case 'unit':
          return a.unit.localeCompare(b.unit) * dir;
        case 'onHand':
          return (a.totalOnHand - b.totalOnHand) * dir;
        case 'status':
          return itemStatusLabel(a.status).localeCompare(itemStatusLabel(b.status)) * dir;
        default:
          return 0;
      }
    });
    return items;
  });

  sortFieldLabel = computed(() =>
    this.sortFields.find((f) => f.id === this.sortState().id)?.label ?? 'Sort');

  filterLabel = computed(() => {
    if (this.lowStockOnly()) return 'Filter: Low stock';
    return this.showInactive() ? 'Filter: Archived' : 'Filter';
  });

  resetFilters(): void {
    this.showInactive.set(false);
    this.lowStockOnly.set(false);
  }

  onSortByChange(id: SupplySortId): void {
    const field = this.sortFields.find((f) => f.id === id);
    this.sortState.set({ id, desc: field?.defaultDesc ?? false });
  }

  onSortDirChange(desc: boolean): void {
    this.sortState.update((prev) => ({ id: prev.id, desc }));
  }

  onChanged(): void {
    this.refreshTrigger.update((v) => v + 1);
  }

  onOpen(supply: SupplyResponse): void {
    this.router.navigate(['/inventory/supplies', supply.id]);
  }

  onEdit(supply: SupplyResponse): void {
    this.selectedSupply.set(supply);
    this.sheetState.set('open');
  }

  onSheetSaved(): void {
    this.sheetState.set('closed');
    this.onChanged();
  }

  onSheetClosed(): void {
    this.sheetState.set('closed');
    this.selectedSupply.set(null);
  }

  onArchiveRestore(supply: SupplyResponse): void {
    const terminal = isTerminalStatus(supply.status);
    const newStatus = terminal ? 'Active' : 'Archived';
    this.archiving.set(true);
    this.supplyApi.patchSupply({
      id: supply.id,
      body: [replace('/status', newStatus)],
    }).pipe(
      handleMutationError(this.errorHandler, this.archiving, 'Failed to update supply.'),
    ).subscribe(() => {
      this.archiving.set(false);
      toast.success(terminal ? 'Supply restored.' : 'Supply archived.');
      this.onChanged();
    });
  }

  onDeleteRequest(supply: SupplyResponse): void {
    this.pendingDelete.set(supply);
    this.deleteState.set('open');
  }

  confirmDelete(ctx: { close: () => void }): void {
    const supply = this.pendingDelete();
    if (!supply) return;
    this.deleting.set(true);
    const result$ = this.supplyApi.deleteSupply({ id: supply.id }).pipe(
      handleMutationError(this.errorHandler, this.deleting, 'Failed to delete supply.'),
    );
    firstValueFrom(result$).then(() => {
      this.deleting.set(false);
      ctx.close();
      this.pendingDelete.set(null);
      this.onChanged();
      toast.success('Supply deleted.');
    }).catch(() => {
      this.deleting.set(false);
    });
  }
}
