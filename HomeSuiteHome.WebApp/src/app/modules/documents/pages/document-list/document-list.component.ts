import { Component, ChangeDetectionStrategy, inject, signal, computed, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, RouterLink, ActivatedRoute } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, map, catchError, of, combineLatest, Subscription } from 'rxjs';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { LucideAngularModule } from 'lucide-angular';
import { FileText, Plus } from 'lucide-angular';
import { PageHeaderComponent } from '@/shared/components/ui/page-header/page-header.component';
import { ButtonComponent } from '@/shared/components/ui/button/button.component';
import { SpinnerComponent } from '@/shared/components/ui/spinner/spinner.component';
import { EmptyStateComponent } from '@/shared/components/ui/empty-state/empty-state.component';
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
    LucideAngularModule,
    PageHeaderComponent,
    ButtonComponent,
    SpinnerComponent,
    EmptyStateComponent,
    DocumentTableComponent,
    DocumentDetailPanelComponent,
    PaginationComponent,
  ],
  template: `
    <div>
      <app-page-header title="Documents" description="Manage your uploaded documents">
        <a action routerLink="/documents/upload">
          <app-button>
            <lucide-icon [img]="plusIcon" [size]="16" />
            Upload
          </app-button>
        </a>
      </app-page-header>

      @if (loading()) {
        <div class="flex justify-center py-16">
          <app-spinner size="lg" />
        </div>
      }

      @if (error()) {
        <app-empty-state
          [icon]="fileTextIcon"
          title="Failed to load documents"
          description="There was an error loading the document list. Please try again."
        />
      }

      @if (data(); as result) {
        @if (result.items.length === 0) {
          <app-empty-state
            [icon]="fileTextIcon"
            title="No documents yet"
            description="Upload your first document to get started."
          >
            <a routerLink="/documents/upload">
              <app-button>
                <lucide-icon [img]="plusIcon" [size]="16" />
                Upload Document
              </app-button>
            </a>
          </app-empty-state>
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
  fileTextIcon = FileText;
  plusIcon = Plus;

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
