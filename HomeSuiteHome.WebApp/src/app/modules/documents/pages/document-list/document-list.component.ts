import { Component, ChangeDetectionStrategy, inject, signal, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, RouterLink, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, catchError, of, combineLatest, Subscription } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFileText, lucidePlus } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { DocumentTableComponent } from '../../components/document-table/document-table.component';
import { DocumentDetailPanelComponent } from '../../components/document-detail-panel/document-detail-panel.component';
import { PaginationComponent } from '../../components/pagination/pagination.component';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getDocuments } from '@/api/generated/fn/documents/get-documents';
import type { PaginatedResponseOfGetDocumentsResponse } from '@/api/generated/models/paginated-response-of-get-documents-response';

@Component({
  selector: 'app-document-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmEmptyImports,
    DocumentTableComponent,
    DocumentDetailPanelComponent,
    PaginationComponent,
  ],
  providers: [provideIcons({ lucideFileText, lucidePlus })],
  template: `
    <div>
      <!-- Page Header -->
      <div class="mb-6 flex items-center justify-between">
        <div>
          <h1 class="text-2xl font-bold tracking-tight text-foreground">Documents</h1>
          <p class="text-sm text-muted-foreground">Manage your uploaded documents</p>
        </div>
        <a routerLink="/documents/upload">
          <button hlmBtn>
            <ng-icon hlm name="lucidePlus" size="sm" />
            Upload
          </button>
        </a>
      </div>

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
            <a routerLink="/documents/upload">
              <button hlmBtn>
                <ng-icon hlm name="lucidePlus" size="sm" />
                Upload Document
              </button>
            </a>
          </section>
        } @else {
          <app-document-table
            [documents]="result.items"
            [selectedDocumentId]="selectedDocumentId()"
            (documentSelect)="selectedDocumentId.set($event)"
          />
          <app-pagination
            [page]="+result.page"
            [totalPages]="+(result.totalPages ?? 0)"
            (pageChange)="onPageChange($event)"
          />
        }
      }

      <app-document-detail-panel
        [documentId]="selectedDocumentId()"
        (closed)="selectedDocumentId.set(null)"
        (refreshList)="onRefresh()"
      />
    </div>
  `,
})
export class DocumentListComponent implements OnDestroy {
  selectedDocumentId = signal<string | null>(null);
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

  onRefresh(): void {
    this.refreshTrigger.update((v) => v + 1);
  }
}
