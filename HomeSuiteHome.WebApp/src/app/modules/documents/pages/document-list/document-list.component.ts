import { Component, ChangeDetectionStrategy, inject, signal, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, catchError, of, combineLatest, Subscription } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFileText, lucidePlus } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
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
    DocumentTableComponent,
    PaginationComponent,
    UploadDialogComponent,
  ],
  providers: [provideIcons({ lucideFileText, lucidePlus })],
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
          <div class="flex justify-end mb-4">
            <app-upload-dialog triggerSize="sm" (uploaded)="onRefresh()">
              <ng-icon hlm name="lucidePlus" size="sm" />
              Upload
            </app-upload-dialog>
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
