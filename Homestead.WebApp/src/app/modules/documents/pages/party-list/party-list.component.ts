import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '@/shared/services/auth.service';
import { Router, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, catchError, of, combineLatest } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideUsers, lucidePlus, lucideTrash2 } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { PartyTableComponent } from '../../components/party-table/party-table.component';
import { CreatePartyDialogComponent } from '../../components/create-party-dialog/create-party-dialog.component';
import { EditPartySheetComponent } from '../../components/edit-party-sheet/edit-party-sheet.component';
import { PaginationComponent } from '../../components/pagination/pagination.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getParties } from '@/api/generated/fn/documents/get-parties';
import { bulkDeleteParties } from '@/api/generated/fn/documents/bulk-delete-parties';
import type { GetPartiesResponse } from '@/api/generated/models/get-parties-response';
import type { PaginatedResponseOfGetPartiesResponse } from '@/api/generated/models/paginated-response-of-get-parties-response';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-party-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmSpinner,
    HlmEmptyImports,
    HlmButton,
    HlmAlertDialogImports,
    PartyTableComponent,
    CreatePartyDialogComponent,
    EditPartySheetComponent,
    PaginationComponent,
  ],
  providers: [provideIcons({ lucideUsers, lucidePlus, lucideTrash2 })],
  template: `
    <div>
      @if (loading() && !data()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      }

      @if (error()) {
        <section hlmEmpty>
          <hlm-empty-media variant="icon">
            <ng-icon hlm name="lucideUsers" size="xl" />
          </hlm-empty-media>
          <div hlmEmptyHeader>
            <h3 hlmEmptyTitle>Failed to load parties</h3>
            <p hlmEmptyDescription>There was an error loading the party list. Please try again.</p>
          </div>
        </section>
      }

      @if (data(); as result) {
        @if (result.items.length === 0) {
          <section hlmEmpty>
            <hlm-empty-media variant="icon">
              <ng-icon hlm name="lucideUsers" size="xl" />
            </hlm-empty-media>
            <div hlmEmptyHeader>
              <h3 hlmEmptyTitle>No parties yet</h3>
              <p hlmEmptyDescription>Create your first party to get started.</p>
            </div>
            @if (auth.canWrite()) {
              <app-create-party-dialog (created)="onRefresh()">
                <ng-icon hlm name="lucidePlus" size="sm" />
                New Party
              </app-create-party-dialog>
            }
          </section>
        } @else {
          <div class="relative">
            <!-- Normal toolbar -->
            <div [class.opacity-0]="hasSelection()" [class.pointer-events-none]="hasSelection()">
              <div class="flex items-center gap-2 mb-4">
                <div class="ml-auto flex items-center gap-2">
                  @if (auth.canWrite()) {
                    <app-create-party-dialog triggerSize="sm" (created)="onRefresh()">
                      <ng-icon hlm name="lucidePlus" size="sm" />
                      New Party
                    </app-create-party-dialog>
                  }
                </div>
              </div>
            </div>

            <!-- Selection toolbar -->
            <div class="absolute inset-x-0 top-0 flex items-center gap-2 h-8 bg-background" [class.opacity-0]="!hasSelection() || !auth.canWrite()" [class.pointer-events-none]="!hasSelection() || !auth.canWrite()">
              <span class="text-sm text-muted-foreground">
                {{ selectionCount() }} {{ selectionCount() > 1 ? 'parties' : 'party' }} selected
              </span>
              <div class="ml-auto flex items-center gap-2">
                <button hlmBtn variant="outline" size="sm" (click)="clearSelection()">
                  Clear selection
                </button>
                <hlm-alert-dialog>
                  <button hlmBtn variant="destructive" size="sm" hlmAlertDialogTrigger>
                    <ng-icon hlm name="lucideTrash2" size="sm" />
                    Delete
                  </button>
                  <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
                    <div hlmAlertDialogHeader>
                      <h2 hlmAlertDialogTitle>Delete {{ selectionCount() }} {{ selectionCount() > 1 ? 'parties' : 'party' }}?</h2>
                      <p hlmAlertDialogDescription>
                        This action cannot be undone. The selected {{ selectionCount() > 1 ? 'parties' : 'party' }} will be permanently deleted
                        and removed from all associated documents.
                      </p>
                    </div>
                    <div hlmAlertDialogFooter>
                      <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
                      <button hlmAlertDialogAction variant="destructive" (click)="onBulkDelete(ctx)" [disabled]="deleting()">
                        {{ deleting() ? 'Deleting...' : 'Delete' }}
                      </button>
                    </div>
                  </hlm-alert-dialog-content>
                </hlm-alert-dialog>
              </div>
            </div>
          </div>

          <app-party-table
            [parties]="result.items"
            [selectedIds]="selectedPartyIds()"
            (partySelect)="onPartySelect($event)"
            (selectionChange)="selectedPartyIds.set($event)"
          />
          <app-pagination
            [page]="+result.page"
            [totalPages]="+(result.totalPages ?? 0)"
            [totalCount]="+result.totalCount"
            [pageSize]="+result.pageSize"
            (pageChange)="onPageChange($event)"
            (pageSizeChange)="onPageSizeChange($event)"
          />
        }
      }
    </div>

    <app-edit-party-sheet
      [party]="selectedParty()"
      [state]="sheetState()"
      (saved)="onSheetSaved()"
      (closed)="onSheetClosed()"
    />
  `,
})
export class PartyListComponent {
  auth = inject(AuthService);
  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  deleting = signal(false);

  // Selection state
  selectedPartyIds = signal<Set<string>>(new Set());
  selectionCount = computed(() => this.selectedPartyIds().size);
  hasSelection = computed(() => this.selectionCount() > 0);

  // Sheet state
  selectedParty = signal<GetPartiesResponse | null>(null);
  sheetState = signal<BrnDialogState>('closed');

  private page = toSignal(
    this.route.queryParamMap.pipe(map((params) => +(params.get('page') ?? 1))),
    { initialValue: 1 },
  );

  private pageSize = toSignal(
    this.route.queryParamMap.pipe(map((params) => +(params.get('pageSize') ?? 10))),
    { initialValue: 10 },
  );

  data = toSignal(
    combineLatest([
      toObservable(this.page),
      toObservable(this.pageSize),
      toObservable(this.refreshTrigger),
    ]).pipe(
      switchMap(([page, pageSize]) => {
        this.loading.set(true);
        this.error.set(false);
        return getParties(this.http, this.apiConfig.rootUrl, {
          Page: page,
          PageSize: pageSize,
        }).pipe(
          map((r) => {
            this.loading.set(false);
            return r.body as PaginatedResponseOfGetPartiesResponse;
          }),
          catchError(() => {
            this.loading.set(false);
            this.error.set(true);
            return of(null);
          }),
        );
      }),
    ),
    { initialValue: null },
  );

  onPageChange(newPage: number): void {
    this.router.navigate(['/documents/parties'], {
      queryParams: { page: newPage },
      queryParamsHandling: 'merge',
    });
  }

  onPageSizeChange(newSize: number): void {
    this.router.navigate(['/documents/parties'], {
      queryParams: { page: 1, pageSize: newSize },
      queryParamsHandling: 'merge',
    });
  }

  onPartySelect(id: string): void {
    const parties = this.data()?.items;
    const party = parties?.find((p) => p.id === id) ?? null;
    if (party) {
      this.selectedParty.set(party);
      this.sheetState.set('open');
    }
  }

  onSheetSaved(): void {
    this.sheetState.set('closed');
    this.onRefresh();
  }

  onSheetClosed(): void {
    this.sheetState.set('closed');
    this.selectedParty.set(null);
  }

  clearSelection(): void {
    this.selectedPartyIds.set(new Set());
  }

  onBulkDelete(ctx: { close: () => void }): void {
    const ids = Array.from(this.selectedPartyIds());
    if (ids.length === 0) return;

    this.deleting.set(true);

    bulkDeleteParties(this.http, this.apiConfig.rootUrl, {
      body: { ids },
    }).subscribe({
      next: (response) => {
        this.deleting.set(false);
        ctx.close();
        const count = +response.body.deletedCount;
        toast.success(`${count} ${count > 1 ? 'parties' : 'party'} deleted.`);
        this.clearSelection();
        this.onRefresh();
      },
      error: () => {
        this.deleting.set(false);
        toast.error('Failed to delete parties. Please try again.');
      },
    });
  }

  onRefresh(): void {
    this.refreshTrigger.update((v) => v + 1);
  }
}
