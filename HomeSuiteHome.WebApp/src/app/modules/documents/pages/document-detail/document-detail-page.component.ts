import {
  Component,
  ChangeDetectionStrategy,
  signal,
  inject,
  effect,
  untracked,
  computed,
  OnDestroy,
  viewChild,
  afterNextRender,
  TemplateRef,
} from '@angular/core';
import { Location, NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { DomSanitizer } from '@angular/platform-browser';
import { ActivatedRoute, Router } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, of, map, catchError, combineLatest, Subscription } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideFileText,
  lucideFileSearch,
  lucideDownload,
  lucideTrash2,
  lucideSave,
  lucideEllipsisVertical,
  lucideFilePlus2,
} from '@ng-icons/lucide';
import { BrnSelectImports } from '@spartan-ng/brain/select';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmTabsImports } from '@/shared/components/spartan/tabs';
import { HlmResizableImports } from '@/shared/components/spartan/resizable';
import { HlmBreadCrumbImports } from '@/shared/components/spartan/breadcrumb';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmFormFieldImports } from '@/shared/components/spartan/form-field';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmTextareaImports } from '@/shared/components/spartan/textarea';
import { HlmSidebarService } from '@/shared/components/spartan/sidebar';
import { TagInputComponent } from '../../components/tag-input/tag-input.component';
import { PartyInputComponent } from '../../components/party-input/party-input.component';
import type { PartyOption } from '../../components/party-input/party-input.component';
import { AddRevisionDialogComponent } from '../../components/add-revision-dialog/add-revision-dialog.component';
import { formatBytes } from '@/shared/utils';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getDocument } from '@/api/generated/fn/documents/get-document';
import { getDocumentRevision } from '@/api/generated/fn/documents/get-document-revision';
import { updateDocument } from '@/api/generated/fn/documents/update-document';
import { deleteDocument } from '@/api/generated/fn/documents/delete-document';
import type { GetDocumentResponse } from '@/api/generated/models/get-document-response';
import type { GetDocumentRevisionResponse } from '@/api/generated/models/get-document-revision-response';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-document-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgTemplateOutlet,
    FormsModule,
    NgIcon,
    BrnSelectImports,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmTabsImports,
    HlmResizableImports,
    HlmBreadCrumbImports,
    HlmDropdownMenuImports,
    HlmSelectImports,
    HlmInput,
    HlmFormFieldImports,
    HlmLabel,
    TagInputComponent,
    PartyInputComponent,
    HlmTextareaImports,
    AddRevisionDialogComponent,
  ],
  providers: [
    provideIcons({
      lucideFileText,
      lucideFileSearch,
      lucideDownload,
      lucideTrash2,
      lucideSave,
      lucideEllipsisVertical,
      lucideFilePlus2,
    }),
  ],
  template: `
    <ng-template #headerTitle>
      <nav hlmBreadcrumb>
        <ol hlmBreadcrumbList>
          <li hlmBreadcrumbItem>
            <a hlmBreadcrumbLink link="/documents">Documents</a>
          </li>
          <li hlmBreadcrumbSeparator></li>
          @if (hasMultipleRevisions()) {
            <li hlmBreadcrumbItem>
              <a hlmBreadcrumbLink [link]="['/documents', documentId()]">{{ document()?.name ?? 'Document' }}</a>
            </li>
            <li hlmBreadcrumbSeparator></li>
            <li hlmBreadcrumbItem>
              <brn-select [ngModel]="selectedRevisionValue()" (ngModelChange)="onRevisionSelected($event)" placeholder="Revision">
                <hlm-select-trigger size="sm" class="h-7 text-xs">
                  <hlm-select-value />
                </hlm-select-trigger>
                <hlm-select-content>
                  @for (rev of sortedRevisions(); track rev.id) {
                    <hlm-option [value]="rev.id">{{ revisionLabel(rev) }}</hlm-option>
                  }
                </hlm-select-content>
              </brn-select>
            </li>
          } @else {
            <li hlmBreadcrumbItem>
              <span hlmBreadcrumbPage>{{ document()?.name ?? 'Document' }}</span>
            </li>
          }
        </ol>
      </nav>
    </ng-template>

    @if (loading()) {
      <div class="flex h-full items-center justify-center">
        <hlm-spinner />
      </div>
    }

    @if (document(); as doc) {
      <div class="flex items-center gap-2 mb-4">
        <div class="ml-auto flex items-center gap-2">
          <button hlmBtn size="sm" variant="outline" (click)="handleDownload()">
            <ng-icon hlm name="lucideDownload" size="sm" />
            Download
          </button>
          <button hlmBtn size="sm" (click)="handleSave()" [disabled]="!hasChanges() || saving()">
            <ng-icon hlm name="lucideSave" size="sm" />
            {{ saving() ? 'Saving...' : 'Save' }}
          </button>
          <button hlmBtn variant="outline" size="icon-sm" [hlmDropdownMenuTrigger]="moreMenu">
            <ng-icon hlm name="lucideEllipsisVertical" size="sm" />
          </button>
          <ng-template #moreMenu>
            <hlm-dropdown-menu class="w-48">
              <button hlmDropdownMenuItem (triggered)="addRevisionDialog.open()">
                <ng-icon hlm name="lucideFilePlus2" size="sm" />
                <span>Add Revision</span>
              </button>
              <button hlmDropdownMenuItem variant="destructive" (triggered)="handleDelete()" [disabled]="deleting()">
                <ng-icon hlm name="lucideTrash2" size="sm" />
                <span>{{ deleting() ? 'Deleting...' : 'Delete' }}</span>
              </button>
            </hlm-dropdown-menu>
          </ng-template>
        </div>
      </div>

      <app-add-revision-dialog
        #addRevisionDialog
        [documentId]="doc.id"
        (revisionCreated)="refreshTrigger.update(v => v + 1)"
      />

      <!-- Shared content templates -->
      <ng-template #previewContent>
        @if (isPdf() && safeFileUrl()) {
          <iframe
            [src]="safeFileUrl()"
            class="h-full w-full"
            [title]="'Preview of ' + doc.name"
          ></iframe>
        } @else if (isImage() && fileBlobUrl()) {
          <div class="flex h-full w-full items-center justify-center bg-black">
            <img
              [src]="fileBlobUrl()"
              [alt]="doc.name"
              class="checkerboard max-h-full max-w-full"
            />
          </div>
        } @else if (!fileBlobUrl() && (isPdf() || isImage())) {
          <div class="flex h-full items-center justify-center">
            <hlm-spinner />
          </div>
        } @else {
          <div
            class="flex h-full flex-col items-center justify-center gap-2 bg-muted text-muted-foreground"
          >
            <ng-icon hlm name="lucideFileText" size="xl" />
            <span class="text-sm">Preview not available</span>
          </div>
        }
      </ng-template>

      <ng-template #infoContent>
        <div class="space-y-4">
          <!-- Name -->
          <hlm-form-field>
            <label hlmLabel for="doc-name">Name</label>
            <input
              hlmInput
              id="doc-name"
              [value]="editName()"
              (input)="editName.set(asInputValue($event))"
            />
          </hlm-form-field>

          <!-- Document Date -->
          <hlm-form-field>
            <label hlmLabel for="doc-date">Document Date</label>
            <input
              hlmInput
              type="date"
              id="doc-date"
              [value]="editDocumentDate()"
              (input)="editDocumentDate.set(asInputValue($event))"
            />
          </hlm-form-field>

          <!-- Valid From / Valid Until -->
          <div class="grid grid-cols-2 gap-3">
            <hlm-form-field>
              <label hlmLabel for="doc-valid-from">Valid From</label>
              <input
                hlmInput
                type="date"
                id="doc-valid-from"
                [value]="editValidFrom()"
                (input)="editValidFrom.set(asInputValue($event))"
              />
            </hlm-form-field>
            <hlm-form-field>
              <label hlmLabel for="doc-valid-until">Valid Until</label>
              <input
                hlmInput
                type="date"
                id="doc-valid-until"
                [value]="editValidUntil()"
                (input)="editValidUntil.set(asInputValue($event))"
              />
            </hlm-form-field>
          </div>

          <!-- Tags -->
          <div class="space-y-2">
            <label hlmLabel>Tags</label>
            <app-tag-input [value]="editTags()" (valueChange)="editTags.set($event)" />
          </div>

          <!-- Parties -->
          <div class="space-y-2">
            <label hlmLabel>Parties</label>
            <app-party-input [value]="editParties()" (valueChange)="editParties.set($event)" />
          </div>

          <!-- Added -->
          <div class="space-y-2">
            <label hlmLabel>Added</label>
            <p class="text-sm text-muted-foreground">
              {{ formatDate(doc.createdAt) }}
            </p>
          </div>

          <!-- Size -->
          <div class="space-y-2">
            <label hlmLabel>Size</label>
            <p class="text-sm text-muted-foreground">
              {{ formatBytes(+displayFileSize()) }}
            </p>
          </div>

          <!-- Type -->
          <div class="space-y-2">
            <label hlmLabel>Type</label>
            <p class="text-sm text-muted-foreground">{{ displayContentType() }}</p>
          </div>
        </div>
      </ng-template>

      <ng-template #textContent>
        @if (displayExtractedText()) {
          <textarea
            hlmTextarea
            readonly
            class="h-full resize-none [field-sizing:fixed] bg-muted"
            [value]="displayExtractedText()"
          ></textarea>
        } @else {
          <div class="flex flex-col items-center justify-center gap-2 py-8 text-muted-foreground">
            <ng-icon hlm name="lucideFileSearch" size="xl" />
            <span class="text-sm">No extracted text available</span>
          </div>
        }
      </ng-template>

      @if (isMobile()) {
        <!-- Mobile: single tabbed layout -->
        <hlm-tabs tab="preview" class="min-w-0 flex flex-1 flex-col overflow-hidden rounded-md border">
          <hlm-tabs-list variant="line" class="mx-4 mt-3">
            <button hlmTabsTrigger="preview">Preview</button>
            <button hlmTabsTrigger="info">Info</button>
            <button hlmTabsTrigger="text">Text</button>
          </hlm-tabs-list>
          <div hlmTabsContent="preview" class="min-h-0 flex-1 overflow-hidden">
            <ng-container [ngTemplateOutlet]="previewContent" />
          </div>
          <div hlmTabsContent="info" class="flex-1 overflow-y-auto px-4 py-3">
            <ng-container [ngTemplateOutlet]="infoContent" />
          </div>
          <div hlmTabsContent="text" class="min-h-0 min-w-0 flex-1 overflow-hidden px-4 py-3">
            <ng-container [ngTemplateOutlet]="textContent" />
          </div>
        </hlm-tabs>
      } @else {
        <!-- Desktop: resizable two-panel layout -->
        <div hlmResizableGroup direction="horizontal" class="min-h-0 flex-1 rounded-md border">
          <!-- Preview panel -->
          <div hlmResizablePanel [defaultSize]="75" [minSize]="50" class="overflow-hidden">
            <ng-container [ngTemplateOutlet]="previewContent" />
          </div>

          <hlm-resizable-handle withHandle />

          <!-- Info/Text tabs panel -->
          <div hlmResizablePanel [defaultSize]="25" [minSize]="20" class="flex flex-col overflow-hidden">
            <hlm-tabs tab="info" class="min-w-0 flex flex-1 flex-col overflow-hidden">
              <hlm-tabs-list variant="line" class="mx-4 mt-3">
                <button hlmTabsTrigger="info">Info</button>
                <button hlmTabsTrigger="text">Text</button>
              </hlm-tabs-list>
              <div hlmTabsContent="info" class="flex-1 overflow-y-auto px-4 py-3">
                <ng-container [ngTemplateOutlet]="infoContent" />
              </div>
              <div hlmTabsContent="text" class="min-h-0 min-w-0 flex-1 overflow-hidden px-4 py-3">
                <ng-container [ngTemplateOutlet]="textContent" />
              </div>
            </hlm-tabs>
          </div>
        </div>
      }
    }
  `,
  styles: [
    `
      .checkerboard {
        background-color: var(--background);
        background-image: repeating-conic-gradient(
          var(--muted) 0% 25%,
          transparent 0% 50%
        );
        background-size: 20px 20px;
      }
    `,
  ],
  host: {
    class: 'flex flex-col h-full',
  },
})
export class DocumentDetailPageComponent implements OnDestroy {
  editName = signal('');
  editTags = signal<string[]>([]);
  editParties = signal<PartyOption[]>([]);
  editDocumentDate = signal('');
  editValidFrom = signal('');
  editValidUntil = signal('');
  saving = signal(false);
  deleting = signal(false);
  loading = signal(false);
  refreshTrigger = signal(0);

  formatBytes = formatBytes;

  private sidebarService = inject(HlmSidebarService);
  isMobile = this.sidebarService.isMobile;

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private sanitizer = inject(DomSanitizer);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private processingHub = inject(ProcessingHubService);
  private pageHeaderService = inject(PageHeaderService);
  private hubSub: Subscription;

  private headerTitleRef = viewChild<TemplateRef<unknown>>('headerTitle');

  documentId = toSignal(
    this.route.paramMap.pipe(map((p) => p.get('id'))),
    { initialValue: null },
  );

  private revisionId = signal<string | null>(this.route.snapshot.paramMap.get('revisionId'));

  fileBlobUrl = signal<string | null>(null);

  safeFileUrl = computed(() => {
    const url = this.fileBlobUrl();
    return url ? this.sanitizer.bypassSecurityTrustResourceUrl(url) : null;
  });

  hasChanges = computed(() => {
    const doc = this.document();
    if (!doc) return false;
    if (this.editName() !== doc.name) return true;
    const origTags = (doc.tags?.map((t) => t.path) ?? []).slice().sort();
    const currTags = this.editTags().slice().sort();
    if (origTags.length !== currTags.length || origTags.some((t, i) => t !== currTags[i]))
      return true;
    const origPartyIds = (doc.parties?.map((p) => p.id) ?? []).slice().sort();
    const currPartyIds = this.editParties().map((p) => p.id).slice().sort();
    if (
      origPartyIds.length !== currPartyIds.length ||
      origPartyIds.some((id, i) => id !== currPartyIds[i])
    )
      return true;
    if (this.editDocumentDate() !== this.toDateInputValue(doc.documentDate)) return true;
    if (this.editValidFrom() !== this.toDateInputValue(doc.validFrom)) return true;
    if (this.editValidUntil() !== this.toDateInputValue(doc.validUntil)) return true;
    return false;
  });

  // Revision computed signals
  sortedRevisions = computed(() => {
    const doc = this.document();
    if (!doc?.revisions) return [];
    return [...doc.revisions].sort((a, b) => +b.revisionNumber - +a.revisionNumber);
  });

  private latestRevision = computed(() => {
    const revisions = this.sortedRevisions();
    return revisions.length > 0 ? revisions[0] : null;
  });

  activeRevision = computed(() => {
    const revId = this.revisionId();
    const doc = this.document();
    if (!doc?.revisions) return null;
    if (revId) {
      return doc.revisions.find((r) => r.id === revId) ?? this.latestRevision();
    }
    return this.latestRevision();
  });

  hasMultipleRevisions = computed(() => {
    const doc = this.document();
    return (doc?.revisions?.length ?? 0) >= 2;
  });

  selectedRevisionValue = computed(() => {
    return this.revisionId() ?? this.latestRevision()?.id ?? '';
  });

  // Revision detail (for extractedText of non-latest revisions)
  private revisionDetail = signal<GetDocumentRevisionResponse | null>(null);

  // Revision-aware display signals
  displayContentType = computed(() => {
    const rev = this.activeRevision();
    return rev?.contentType ?? this.document()?.contentType ?? '';
  });

  displayFileSize = computed(() => {
    const rev = this.activeRevision();
    return rev?.fileSize ?? this.document()?.fileSize ?? 0;
  });

  displayFileExtension = computed(() => {
    const rev = this.activeRevision();
    return rev?.fileExtension ?? this.document()?.fileExtension ?? '';
  });

  displayExtractedText = computed(() => {
    const revId = this.revisionId();
    if (revId) {
      // Viewing a specific revision — use revision detail's extractedText
      return this.revisionDetail()?.extractedText ?? null;
    }
    // Viewing latest — use document-level extractedText
    return this.document()?.extractedText ?? null;
  });

  private fileSub: Subscription | null = null;
  private revisionDetailSub: Subscription | null = null;

  private documentData = toSignal(
    combineLatest([toObservable(this.documentId), toObservable(this.refreshTrigger)]).pipe(
      switchMap(([id]) => {
        if (!id) return of(null);
        this.loading.set(true);
        return getDocument(this.http, this.apiConfig.rootUrl, { id }).pipe(
          map((r) => {
            this.loading.set(false);
            return r.body as GetDocumentResponse;
          }),
          catchError(() => {
            this.loading.set(false);
            return of(null);
          }),
        );
      }),
    ),
    { initialValue: null },
  );

  document = this.documentData;

  constructor() {
    effect(() => {
      const doc = this.document();
      if (doc) {
        this.editName.set(doc.name);
        this.editTags.set(doc.tags?.map((t) => t.path) ?? []);
        this.editParties.set(doc.parties?.map((p) => ({ id: p.id, name: p.name })) ?? []);
        this.editDocumentDate.set(this.toDateInputValue(doc.documentDate));
        this.editValidFrom.set(this.toDateInputValue(doc.validFrom));
        this.editValidUntil.set(this.toDateInputValue(doc.validUntil));
      }
    });

    // Load file blob reactively — reads all signals atomically
    effect(() => {
      const id = this.documentId();
      const revId = this.revisionId();
      // eslint-disable-next-line @typescript-eslint/no-unused-expressions
      this.refreshTrigger(); // track refresh trigger

      // untracked: revokeCurrentBlobUrl reads fileBlobUrl — must not become a dependency
      untracked(() => {
        this.fileSub?.unsubscribe();
        this.revokeCurrentBlobUrl();
      });

      if (!id) return;

      let url = `/api/v1/documents/${id}/file`;
      if (revId) {
        url += `?revisionId=${revId}`;
      }

      this.fileSub = this.http
        .get(url, { responseType: 'blob' })
        .pipe(
          map((blob) => URL.createObjectURL(blob)),
          catchError(() => of(null)),
        )
        .subscribe((blobUrl) => this.fileBlobUrl.set(blobUrl));
    });

    // Load revision detail when viewing a specific revision
    effect(() => {
      const docId = this.documentId();
      const revId = this.revisionId();
      // eslint-disable-next-line @typescript-eslint/no-unused-expressions
      this.refreshTrigger();

      this.revisionDetailSub?.unsubscribe();

      if (!docId || !revId) {
        this.revisionDetail.set(null);
        return;
      }

      this.revisionDetailSub = getDocumentRevision(this.http, this.apiConfig.rootUrl, {
        documentId: docId,
        id: revId,
      })
        .pipe(
          map((r) => r.body as GetDocumentRevisionResponse),
          catchError(() => of(null)),
        )
        .subscribe((detail) => this.revisionDetail.set(detail));
    });

    afterNextRender(() => {
      const tpl = this.headerTitleRef();
      if (tpl) {
        this.pageHeaderService.setTitle(tpl);
      }
    });

    this.hubSub = this.processingHub.jobCompleted$.subscribe(() => {
      this.refreshTrigger.update((v) => v + 1);
    });
  }

  ngOnDestroy(): void {
    this.fileSub?.unsubscribe();
    this.revisionDetailSub?.unsubscribe();
    this.revokeCurrentBlobUrl();
    this.hubSub.unsubscribe();
    this.pageHeaderService.clearTitle();
  }

  isPdf(): boolean {
    return this.displayContentType() === 'application/pdf';
  }

  isImage(): boolean {
    return this.displayContentType()?.startsWith('image/') ?? false;
  }

  revisionLabel(rev: { revisionNumber: number | string; label?: string | null }): string {
    const base = `Revision ${rev.revisionNumber}`;
    return rev.label ? `${base} - ${rev.label}` : base;
  }

  private location = inject(Location);

  onRevisionSelected(revisionId: string): void {
    const docId = this.documentId();
    if (!docId) return;
    const latest = this.latestRevision();
    const isLatest = latest && revisionId === latest.id;
    this.revisionId.set(isLatest ? null : revisionId);

    const path = isLatest
      ? `/documents/${docId}`
      : `/documents/${docId}/revisions/${revisionId}`;
    this.location.go(path);
  }

  private revokeCurrentBlobUrl(): void {
    const url = this.fileBlobUrl();
    if (url) {
      URL.revokeObjectURL(url);
      this.fileBlobUrl.set(null);
    }
  }

  formatDate(dateStr: string): string {
    return new Date(dateStr).toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  }

  toDateInputValue(isoString: string | null | undefined): string {
    return isoString ? isoString.substring(0, 10) : '';
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  handleDownload(): void {
    const url = this.fileBlobUrl();
    const doc = this.document();
    if (!url || !doc) return;
    const a = document.createElement('a');
    a.href = url;
    a.download = `${doc.name}${this.displayFileExtension()}`;
    a.click();
  }

  handleDelete(): void {
    const id = this.documentId();
    if (!id) return;
    this.deleting.set(true);
    deleteDocument(this.http, this.apiConfig.rootUrl, { id }).subscribe({
      next: () => {
        toast.success('Document deleted.');
        this.router.navigate(['/documents']);
      },
      error: () => {
        this.deleting.set(false);
        toast.error('Failed to delete document.');
      },
    });
  }

  handleSave(): void {
    const id = this.documentId();
    if (!id) return;
    this.saving.set(true);
    updateDocument(this.http, this.apiConfig.rootUrl, {
      id,
      body: {
        name: this.editName(),
        documentDate: this.editDocumentDate() || null,
        validFrom: this.editValidFrom() || null,
        validUntil: this.editValidUntil() || null,
        tagPaths: this.editTags(),
        partyIds: this.editParties().map((p) => p.id),
      },
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.refreshTrigger.update((v) => v + 1);
        toast.success('Document updated.');
      },
      error: () => {
        this.saving.set(false);
        toast.error('Failed to save changes.');
      },
    });
  }
}
