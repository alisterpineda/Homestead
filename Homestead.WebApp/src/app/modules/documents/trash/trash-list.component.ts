import { Component, ChangeDetectionStrategy, inject, signal, computed, DestroyRef } from '@angular/core';
import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { Router, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '@/shared/services/auth.service';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import { switchMap, map, combineLatest } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTrash2, lucideRotateCcw, lucideX, lucideEye } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { PaginationComponent } from '@/shared/components/pagination/pagination.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { DocumentApiService } from '@/api/services/document-api.service';
import type { PaginatedResponseOfGetDocumentsResponse } from '@/api/generated/models/paginated-response-of-get-documents-response';
import { formatBytes } from '@/shared/utils';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { handleMutationError, handleQueryError } from '@/shared/utils/rxjs-error.utils';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-trash-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    NgIcon,
    HlmIconImports,
    HlmSpinner,
    HlmEmptyImports,
    HlmButton,
    HlmAlertDialogImports,
    HlmTableImports,
    HlmCheckboxImports,
    NgTemplateOutlet,
    PaginationComponent,
  ],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  providers: [provideIcons({ lucideTrash2, lucideRotateCcw, lucideX, lucideEye })],
  template: `
    <div class="flex min-h-0 flex-1 flex-col">
      @if (loading() && !data()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      }

      @if (error()) {
        <section hlmEmpty>
          <hlm-empty-media variant="icon">
            <ng-icon hlm name="lucideTrash2" size="xl" />
          </hlm-empty-media>
          <div hlmEmptyHeader>
            <h3 hlmEmptyTitle>Failed to load trash</h3>
            <p hlmEmptyDescription>There was an error loading the trash. Please try again.</p>
          </div>
        </section>
      }

      @if (data(); as result) {
          <section hlmEmpty [class.hidden]="!showInitialEmpty()">
            <hlm-empty-media variant="icon">
              <ng-icon hlm name="lucideTrash2" size="xl" />
            </hlm-empty-media>
            <div hlmEmptyHeader>
              <h3 hlmEmptyTitle>Trash is empty</h3>
              <p hlmEmptyDescription>Items you delete will appear here.</p>
            </div>
          </section>

          <div class="relative shrink-0" [class.hidden]="showInitialEmpty()">
            <!-- Normal toolbar -->
            <div [class.opacity-0]="isInSelectionState()" [class.pointer-events-none]="isInSelectionState()" [attr.inert]="isInSelectionState() ? '' : null">
              <div class="flex items-center justify-end gap-2 mb-3">
                @if (auth.canWrite()) {
                  <hlm-alert-dialog>
                    <button hlmBtn variant="destructive" size="sm" hlmAlertDialogTrigger>
                      <ng-icon hlm name="lucideTrash2" size="sm" />
                      Empty Trash
                    </button>
                    <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
                      <div hlmAlertDialogHeader>
                        <h2 hlmAlertDialogTitle>Empty trash?</h2>
                        <p hlmAlertDialogDescription>
                          This will permanently delete all items in the trash. This action cannot be undone.
                        </p>
                      </div>
                      <div hlmAlertDialogFooter>
                        <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
                        <button hlmAlertDialogAction variant="destructive" (click)="onEmptyTrash(ctx)" [disabled]="emptyingTrash()">
                          {{ emptyingTrash() ? 'Emptying...' : 'Empty Trash' }}
                        </button>
                      </div>
                    </hlm-alert-dialog-content>
                  </hlm-alert-dialog>
                }
              </div>
            </div>

            <!-- Selection toolbar: absolute overlay -->
            <div class="absolute inset-x-0 top-0 flex items-center gap-2 h-8 bg-background" [class.opacity-0]="!isInSelectionState() || !auth.canWrite()" [class.pointer-events-none]="!isInSelectionState() || !auth.canWrite()" [attr.inert]="!isInSelectionState() || !auth.canWrite() ? '' : null">
              <button hlmBtn variant="ghost" size="icon-sm" (click)="clearSelection()" aria-label="Exit selection mode">
                <ng-icon hlm name="lucideX" size="sm" />
              </button>
              @if (isInSelectionState()) {
                <span class="text-sm text-muted-foreground">
                  {{ selectionCount() }} document{{ selectionCount() > 1 ? 's' : '' }} selected
                </span>
                @if (!allPageSelected()) {
                  <button hlmBtn variant="link" size="sm" (click)="toggleSelectAll()">
                    Select all
                  </button>
                }
                <div class="ml-auto flex items-center gap-2">
                  <button hlmBtn variant="outline" size="sm" (click)="onBulkRestore()" [disabled]="restoring()">
                    <ng-icon hlm name="lucideRotateCcw" size="sm" />
                    {{ restoring() ? 'Restoring...' : 'Restore' }}
                  </button>
                  <hlm-alert-dialog>
                    <button hlmBtn variant="destructive" size="sm" hlmAlertDialogTrigger>
                      <ng-icon hlm name="lucideTrash2" size="sm" />
                      Delete Permanently
                    </button>
                    <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
                      <div hlmAlertDialogHeader>
                        <h2 hlmAlertDialogTitle>Permanently delete {{ selectionCount() }} document{{ selectionCount() > 1 ? 's' : '' }}?</h2>
                        <p hlmAlertDialogDescription>
                          This action cannot be undone. The selected document{{ selectionCount() > 1 ? 's' : '' }}
                          and {{ selectionCount() > 1 ? 'their' : 'its' }} associated files will be permanently deleted.
                        </p>
                      </div>
                      <div hlmAlertDialogFooter>
                        <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
                        <button hlmAlertDialogAction variant="destructive" (click)="onBulkPermanentlyDelete(ctx)" [disabled]="deleting()">
                          {{ deleting() ? 'Deleting...' : 'Delete Permanently' }}
                        </button>
                      </div>
                    </hlm-alert-dialog-content>
                  </hlm-alert-dialog>
                </div>
              } @else {
                <span class="text-sm text-muted-foreground">Select items</span>
              }
            </div>
          </div>

          <div class="flex min-h-0 flex-1 flex-col overflow-y-auto p-px" [class.hidden]="!hasItems()">
            <div class="overflow-clip rounded-md border">
              <div hlmTableContainer>
                <table hlmTable>
                  <thead hlmTHead class="sticky top-0 z-10 bg-background">
                    <tr hlmTr>
                      <th hlmTh class="w-10" (click)="$event.stopPropagation()">
                        <hlm-checkbox
                          [checked]="allPageSelected()"
                          [indeterminate]="someSelected()"
                          (checkedChange)="toggleSelectAll()"
                          aria-label="Select all"
                        />
                      </th>
                      <th hlmTh>Name</th>
                      <th hlmTh class="hidden md:table-cell">Type</th>
                      <th hlmTh class="hidden md:table-cell text-right">Size</th>
                      <th hlmTh class="hidden sm:table-cell">Deleted</th>
                      <th hlmTh class="w-10"><span class="sr-only">Actions</span></th>
                    </tr>
                  </thead>
                  <tbody hlmTBody>
                    @for (doc of result.items; track doc.id) {
                      <tr hlmTr class="cursor-pointer" (click)="onDocumentSelect(doc.id)">
                        <td hlmTd class="w-10" (click)="$event.stopPropagation()" (keydown.enter)="$event.stopPropagation()">
                          <hlm-checkbox
                            [checked]="isSelected(doc.id)"
                            (checkedChange)="onDocumentSelect(doc.id)"
                            aria-label="Select {{ doc.name }}"
                          />
                        </td>
                        <td hlmTd class="font-medium">{{ doc.name }}</td>
                        <td hlmTd class="hidden md:table-cell text-muted-foreground">{{ doc.contentType }}</td>
                        <td hlmTd class="hidden md:table-cell text-right text-muted-foreground">{{ getFormattedSize(+doc.fileSize) }}</td>
                        <td hlmTd class="hidden sm:table-cell text-muted-foreground">{{ doc.deletedAt | date:'mediumDate' }}</td>
                        <td hlmTd class="w-10" (click)="$event.stopPropagation()">
                          <a
                            hlmBtn
                            variant="ghost"
                            size="icon-sm"
                            [href]="getFileUrl(doc.id)"
                            target="_blank"
                            aria-label="View file"
                          >
                            <ng-icon hlm name="lucideEye" size="sm" />
                          </a>
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            </div>
            <ng-template #paginationTpl>
              <app-pagination
                [page]="+result.page"
                [totalPages]="+(result.totalPages ?? 0)"
                [totalCount]="+result.totalCount"
                [pageSize]="+result.pageSize"
                (pageChange)="onPageChange($event)"
                (pageSizeChange)="onPageSizeChange($event)"
              />
            </ng-template>
            <div class="mt-auto sm:hidden">
              <ng-container [ngTemplateOutlet]="paginationTpl" />
            </div>
          </div>
          <div class="shrink-0 max-sm:hidden" [class.hidden]="!hasItems()">
            <ng-container [ngTemplateOutlet]="paginationTpl" />
          </div>
      }

    </div>
  `,
})
export class TrashListComponent {
  auth = inject(AuthService);
  private documentApi = inject(DocumentApiService);
  private apiConfig = inject(ApiConfiguration);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private destroyRef = inject(DestroyRef);
  private errorHandler = inject(ErrorHandlerService);
  private prefs = inject(UserPreferencesService);

  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);
  deleting = signal(false);
  restoring = signal(false);
  emptyingTrash = signal(false);

  selectedDocumentIds = signal<Set<string>>(new Set());
  selectionCount = computed(() => this.selectedDocumentIds().size);
  isInSelectionState = computed(() => this.selectionCount() > 0);
  allPageSelected = computed(() => {
    const items = this.data()?.items;
    if (!items || items.length === 0) return false;
    const selected = this.selectedDocumentIds();
    return items.every(d => selected.has(d.id));
  });
  someSelected = computed(() => {
    const items = this.data()?.items;
    if (!items || items.length === 0) return false;
    const selected = this.selectedDocumentIds();
    return items.some(d => selected.has(d.id)) && !items.every(d => selected.has(d.id));
  });

  hasItems = computed(() => (this.data()?.items?.length ?? 0) > 0);
  showInitialEmpty = computed(() => !this.hasItems() && !this.loading());

  private page = toSignal(
    this.route.queryParamMap.pipe(map((params) => +(params.get('page') ?? 1))),
    { initialValue: 1 },
  );

  private pageSize = toSignal(
    this.route.queryParamMap.pipe(map((params) => +(params.get('pageSize') ?? this.prefs.pageSize()))),
    { initialValue: this.prefs.pageSize() },
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
        return this.documentApi.getDocuments({
          Page: page,
          PageSize: pageSize,
          Deleted: true,
          IncludeChildTags: false,
          SortBy: 'DeletedAt',
          SortDir: 'Desc',
        }).pipe(
          map((r) => {
            this.loading.set(false);
            return r as PaginatedResponseOfGetDocumentsResponse;
          }),
          handleQueryError(this.loading, this.error, null),
        );
      }),
    ),
    { initialValue: null },
  );

  isSelected(id: string): boolean {
    return this.selectedDocumentIds().has(id);
  }

  getFileUrl(id: string): string {
    return `${this.apiConfig.rootUrl}/api/v1/documents/${id}/file`;
  }

  getFormattedSize(bytes: number): string {
    return formatBytes(bytes);
  }

  toggleSelectAll(): void {
    if (this.allPageSelected()) {
      this.selectedDocumentIds.set(new Set());
    } else {
      const items = this.data()?.items;
      if (!items) return;
      this.selectedDocumentIds.set(new Set(items.map(d => d.id)));
    }
  }

  onPageChange(newPage: number): void {
    this.clearSelection();
    this.router.navigate(['/documents/trash'], {
      queryParams: { page: newPage },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onPageSizeChange(newSize: number): void {
    this.prefs.pageSize.set(newSize);
    this.clearSelection();
    this.router.navigate(['/documents/trash'], {
      queryParams: { page: 1, pageSize: newSize },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  onDocumentSelect(id: string): void {
    this.selectedDocumentIds.update(prev => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }

  clearSelection(): void {
    this.selectedDocumentIds.set(new Set());
  }

  onBulkRestore(): void {
    const ids = Array.from(this.selectedDocumentIds());
    if (ids.length === 0) return;

    this.restoring.set(true);

    this.documentApi.bulkRestoreDocuments({
      body: { ids },
    }).pipe(
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.restoring, 'Failed to restore documents. Please try again.'),
    ).subscribe({
      next: (response) => {
        this.restoring.set(false);
        const count = +response.restoredCount;
        toast.success(`${count} document${count > 1 ? 's' : ''} restored.`);
        this.clearSelection();
        this.refreshTrigger.update(v => v + 1);
      },
    });
  }

  onBulkPermanentlyDelete(ctx: { close: () => void }): void {
    const ids = Array.from(this.selectedDocumentIds());
    if (ids.length === 0) return;

    this.deleting.set(true);

    this.documentApi.bulkPermanentlyDeleteDocuments({
      body: { ids },
    }).pipe(
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.deleting, 'Failed to delete documents. Please try again.'),
    ).subscribe({
      next: (response) => {
        this.deleting.set(false);
        ctx.close();
        const count = +response.deletedCount;
        toast.success(`${count} document${count > 1 ? 's' : ''} permanently deleted.`);
        this.clearSelection();
        this.refreshTrigger.update(v => v + 1);
      },
    });
  }

  onEmptyTrash(ctx: { close: () => void }): void {
    this.emptyingTrash.set(true);

    this.documentApi.emptyTrash().pipe(
      takeUntilDestroyed(this.destroyRef),
      handleMutationError(this.errorHandler, this.emptyingTrash, 'Failed to empty trash. Please try again.'),
    ).subscribe({
      next: (response) => {
        this.emptyingTrash.set(false);
        ctx.close();
        const count = +response.deletedCount;
        toast.success(`${count} document${count > 1 ? 's' : ''} permanently deleted.`);
        this.clearSelection();
        this.refreshTrigger.update(v => v + 1);
      },
    });
  }
}
