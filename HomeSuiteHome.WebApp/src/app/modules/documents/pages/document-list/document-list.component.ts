import { Component, ChangeDetectionStrategy, inject, signal, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, catchError, of, combineLatest, Subscription } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFileText, lucidePlus, lucideSearch, lucideListFilter, lucideLayoutList, lucideLayoutGrid } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmButtonGroup } from '@/shared/components/spartan/button-group';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { DocumentTableComponent } from '../../components/document-table/document-table.component';
import { PaginationComponent } from '../../components/pagination/pagination.component';
import { UploadDialogComponent } from '../../components/upload-dialog/upload-dialog.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getDocuments } from '@/api/generated/fn/documents/get-documents';
import type { PaginatedResponseOfGetDocumentsResponse } from '@/api/generated/models/paginated-response-of-get-documents-response';

@Component({
  selector: 'app-document-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmSpinner,
    HlmEmptyImports,
    HlmInput,
    HlmButton,
    HlmButtonGroup,
    DocumentTableComponent,
    PaginationComponent,
    UploadDialogComponent,
  ],
  providers: [provideIcons({ lucideFileText, lucidePlus, lucideSearch, lucideListFilter, lucideLayoutList, lucideLayoutGrid })],
  template: `
    <div>
      @if (loading()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      }

      @if (error()) {
        <section hlmEmpty>
          <hlm-empty-media variant="icon">
            <ng-icon hlm name="lucideFileText" size="xl" />
          </hlm-empty-media>
          <div hlmEmptyHeader>
            <h3 hlmEmptyTitle>Failed to load documents</h3>
            <p hlmEmptyDescription>There was an error loading the document list. Please try again.</p>
          </div>
        </section>
      }

      @if (data(); as result) {
        @if (result.items.length === 0) {
          <section hlmEmpty>
            <hlm-empty-media variant="icon">
              <ng-icon hlm name="lucideFileText" size="xl" />
            </hlm-empty-media>
            <div hlmEmptyHeader>
              <h3 hlmEmptyTitle>No documents yet</h3>
              <p hlmEmptyDescription>Upload your first document to get started.</p>
            </div>
            <app-upload-dialog (uploaded)="onRefresh()">
              <ng-icon hlm name="lucidePlus" size="sm" />
              Upload Document
            </app-upload-dialog>
          </section>
        } @else {
          <div class="flex items-center gap-2 mb-4">
            <!-- TODO: Wire up search to filter documents by name/content -->
            <div class="relative">
              <ng-icon hlm name="lucideSearch" size="sm" class="absolute left-2 top-1/2 -translate-y-1/2 text-muted-foreground" />
              <input hlmInput placeholder="Search documents..." class="pl-8 h-8 w-[200px]" />
            </div>
            <div class="ml-auto flex items-center gap-2">
              <!-- TODO: Wire up filters (e.g. by date, type, tags) -->
              <button hlmBtn variant="outline" size="sm">
                <ng-icon hlm name="lucideListFilter" size="sm" />
                Filters
              </button>
              <!-- TODO: Wire up view toggle to switch between table and grid views -->
              <div hlmButtonGroup aria-label="View mode">
                <button hlmBtn [variant]="viewMode() === 'table' ? 'secondary' : 'outline'" size="icon-sm" (click)="viewMode.set('table')" aria-label="Table view">
                  <ng-icon hlm name="lucideLayoutList" size="sm" />
                </button>
                <button hlmBtn [variant]="viewMode() === 'grid' ? 'secondary' : 'outline'" size="icon-sm" (click)="viewMode.set('grid')" aria-label="Grid view">
                  <ng-icon hlm name="lucideLayoutGrid" size="sm" />
                </button>
              </div>
              <app-upload-dialog triggerSize="sm" (uploaded)="onRefresh()">
                <ng-icon hlm name="lucidePlus" size="sm" />
                Upload
              </app-upload-dialog>
            </div>
          </div>
          <app-document-table
            [documents]="result.items"
            (documentSelect)="onDocumentSelect($event)"
          />
          <app-pagination
            [page]="+result.page"
            [totalPages]="+(result.totalPages ?? 0)"
            (pageChange)="onPageChange($event)"
          />
        }
      }

    </div>
  `,
})
export class DocumentListComponent implements OnDestroy {
  viewMode = signal<'table' | 'grid'>('table');
  refreshTrigger = signal(0);
  loading = signal(false);
  error = signal(false);

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private processingHub = inject(ProcessingHubService);
  private hubSub: Subscription;

  constructor() {
    this.hubSub = this.processingHub.jobCompleted$.subscribe(() => {
      this.refreshTrigger.update((v) => v + 1);
    });
  }

  ngOnDestroy(): void {
    this.hubSub.unsubscribe();
  }

  private page = toSignal(
    this.route.queryParamMap.pipe(map((params) => +(params.get('page') ?? 1))),
    { initialValue: 1 },
  );

  data = toSignal(
    combineLatest([toObservable(this.page), toObservable(this.refreshTrigger)]).pipe(
      switchMap(([page]) => {
        this.loading.set(true);
        this.error.set(false);
        return getDocuments(this.http, this.apiConfig.rootUrl, { Page: page, PageSize: 10 }).pipe(
          map((r) => {
            this.loading.set(false);
            return r.body as PaginatedResponseOfGetDocumentsResponse;
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
    this.router.navigate(['/documents'], { queryParams: { page: newPage } });
  }

  onDocumentSelect(id: string): void {
    this.router.navigate(['/documents', id]);
  }

  onRefresh(): void {
    this.refreshTrigger.update((v) => v + 1);
  }
}
