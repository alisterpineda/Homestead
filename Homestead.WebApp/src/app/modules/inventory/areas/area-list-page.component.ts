import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
} from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, firstValueFrom } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucidePlus,
  lucideMapPin,
  lucideCircleAlert,
  lucideArrowUp,
  lucideArrowDown,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { InventoryAreaApiService } from '@/api/services/inventory-area-api.service';
import { AreaTableComponent, type AreaSortId, type AreaSortState } from './area-table.component';
import { CreateAreaDialogComponent } from './create-area-dialog/create-area-dialog.component';
import { EditAreaDialogComponent } from './edit-area-dialog/edit-area-dialog.component';
import { handleQueryError, handleMutationError } from '@/shared/utils/rxjs-error.utils';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { AreaResponse } from '@/api/generated/models/area-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';

interface AreaSortFieldOption {
  readonly id: AreaSortId;
  readonly label: string;
  readonly defaultDesc: boolean;
}

@Component({
  selector: 'app-area-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmEmptyImports,
    HlmAlertDialogImports,
    HlmDropdownMenuImports,
    AreaTableComponent,
    CreateAreaDialogComponent,
    EditAreaDialogComponent,
  ],
  providers: [provideIcons({
    lucidePlus,
    lucideMapPin,
    lucideCircleAlert,
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
            <div hlmEmptyTitle>Failed to load areas</div>
            <div hlmEmptyDescription>There was an error loading your areas. Please try again.</div>
          </div>
        </div>
      }

      @if (data() !== null) {
        @if (areas().length === 0) {
          <div hlmEmpty>
            <div hlmEmptyHeader>
              <div hlmEmptyMedia variant="icon">
                <ng-icon name="lucideMapPin" />
              </div>
              <div hlmEmptyTitle>No areas yet</div>
              <div hlmEmptyDescription>Create areas (rooms or zones) to anchor where your items live.</div>
            </div>
            <div hlmEmptyContent>
              <app-create-area-dialog (created)="onChanged()">
                <ng-icon hlm name="lucidePlus" size="sm" />
                New area
              </app-create-area-dialog>
            </div>
          </div>
        } @else {
          <div class="flex items-center justify-between gap-2">
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

            <app-create-area-dialog triggerSize="sm" (created)="onChanged()">
              <ng-icon hlm name="lucidePlus" size="sm" />
              New area
            </app-create-area-dialog>
          </div>

          <app-area-table
            [areas]="sortedAreas()"
            [sortState]="sortState()"
            (sortChange)="sortState.set($event)"
            (edit)="onEdit($event)"
            (delete)="onDeleteRequest($event)"
          />
        }
      }
    </div>

    <app-edit-area-dialog
      [area]="selectedArea()"
      [state]="editState()"
      (saved)="onEditSaved()"
      (closed)="onEditClosed()"
    />

    <hlm-alert-dialog [state]="deleteState()" (stateChanged)="deleteState.set($event)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <div hlmAlertDialogHeader>
          <h2 hlmAlertDialogTitle>Delete {{ pendingDelete()?.name ?? 'area' }}?</h2>
          <p hlmAlertDialogDescription>
            This permanently removes the area. Areas with sub-areas or items in them cannot be
            deleted — move or remove those first.
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
export class AreaListPageComponent {
  private areaApi = inject(InventoryAreaApiService);
  private errorHandler = inject(ErrorHandlerService);

  readonly sortFields: readonly AreaSortFieldOption[] = [
    { id: 'name', label: 'Name', defaultDesc: false },
    { id: 'liveItemCount', label: 'Items', defaultDesc: true },
    { id: 'createdAt', label: 'Created', defaultDesc: true },
  ];

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);

  sortState = signal<AreaSortState>({ id: 'name', desc: false });

  selectedArea = signal<AreaResponse | null>(null);
  editState = signal<BrnDialogState>('closed');

  pendingDelete = signal<AreaResponse | null>(null);
  deleteState = signal<BrnDialogState>('closed');
  deleting = signal(false);

  data = toSignal<AreaResponse[] | null, AreaResponse[] | null>(
    toObservable(this.refreshTrigger).pipe(
      switchMap(() => {
        this.loading.set(true);
        this.error.set(false);
        return this.areaApi.getAreas({ Page: 1, PageSize: 200 }).pipe(
          map(r => {
            this.loading.set(false);
            return (r.items ?? []) as AreaResponse[] | null;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  areas = computed(() => this.data() ?? []);

  sortedAreas = computed(() => {
    const list = [...this.areas()];
    const { id, desc } = this.sortState();
    const dir: 1 | -1 = desc ? -1 : 1;
    list.sort((a, b) => {
      switch (id) {
        case 'name':
          return a.name.localeCompare(b.name) * dir;
        case 'liveItemCount':
          return (a.liveItemCount - b.liveItemCount) * dir;
        case 'createdAt':
          return (a.createdAt < b.createdAt ? -1 : a.createdAt > b.createdAt ? 1 : 0) * dir;
        default:
          return 0;
      }
    });
    return list;
  });

  sortFieldLabel = computed(() =>
    this.sortFields.find(f => f.id === this.sortState().id)?.label ?? 'Sort');

  onSortByChange(id: AreaSortId): void {
    const field = this.sortFields.find(f => f.id === id);
    this.sortState.set({ id, desc: field?.defaultDesc ?? false });
  }

  onSortDirChange(desc: boolean): void {
    this.sortState.update(prev => ({ id: prev.id, desc }));
  }

  onChanged(): void {
    this.refreshTrigger.update(v => v + 1);
  }

  onEdit(area: AreaResponse): void {
    this.selectedArea.set(area);
    this.editState.set('open');
  }

  onEditSaved(): void {
    this.editState.set('closed');
    this.onChanged();
  }

  onEditClosed(): void {
    this.editState.set('closed');
    this.selectedArea.set(null);
  }

  onDeleteRequest(area: AreaResponse): void {
    this.pendingDelete.set(area);
    this.deleteState.set('open');
  }

  confirmDelete(ctx: { close: () => void }): void {
    const area = this.pendingDelete();
    if (!area) return;
    this.deleting.set(true);
    const result$ = this.areaApi.deleteArea({ id: area.id }).pipe(
      handleMutationError(this.errorHandler, this.deleting, 'Failed to delete area.'),
    );
    firstValueFrom(result$).then(() => {
      this.deleting.set(false);
      ctx.close();
      this.pendingDelete.set(null);
      this.onChanged();
      toast.success('Area deleted.');
    }).catch(() => {
      this.deleting.set(false);
    });
  }
}
