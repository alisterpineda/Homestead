import {
  Component,
  ChangeDetectionStrategy,
  signal,
  inject,
  effect,
  computed,
  OnDestroy,
  viewChild,
  afterNextRender,
  TemplateRef,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { DomSanitizer } from '@angular/platform-browser';
import { ActivatedRoute, Router } from '@angular/router';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap, of, map, catchError, combineLatest, tap, Subscription } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideFileText,
  lucideFileSearch,
  lucideDownload,
  lucideTrash2,
  lucideSave,
  lucideEllipsisVertical,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmTabsImports } from '@/shared/components/spartan/tabs';
import { HlmResizableImports } from '@/shared/components/spartan/resizable';
import { HlmBreadCrumbImports } from '@/shared/components/spartan/breadcrumb';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmFormFieldImports } from '@/shared/components/spartan/form-field';
import { HlmLabel } from '@/shared/components/spartan/label';
import { TagInputComponent } from '../../components/tag-input/tag-input.component';
import { PartyInputComponent } from '../../components/party-input/party-input.component';
import type { PartyOption } from '../../components/party-input/party-input.component';
import { formatBytes } from '@/shared/utils';
import { ApiConfiguration } from '@/api/generated/api-configuration';
import { getDocument } from '@/api/generated/fn/documents/get-document';
import { updateDocument } from '@/api/generated/fn/documents/update-document';
import { deleteDocument } from '@/api/generated/fn/documents/delete-document';
import type { GetDocumentResponse } from '@/api/generated/models/get-document-response';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { toast } from 'ngx-sonner';

@Component({
  selector: 'app-document-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmIconImports,
    HlmButton,
    HlmSpinner,
    HlmTabsImports,
    HlmResizableImports,
    HlmBreadCrumbImports,
    HlmDropdownMenuImports,
    HlmInput,
    HlmFormFieldImports,
    HlmLabel,
    TagInputComponent,
    PartyInputComponent,
  ],
  providers: [
    provideIcons({
      lucideFileText,
      lucideFileSearch,
      lucideDownload,
      lucideTrash2,
      lucideSave,
      lucideEllipsisVertical,
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
          <li hlmBreadcrumbItem>
            <span hlmBreadcrumbPage>{{ document()?.name ?? 'Document' }}</span>
          </li>
        </ol>
      </nav>
    </ng-template>

    @if (loading()) {
      <div class="flex h-full items-center justify-center">
        <hlm-spinner />
      </div>
    }

    @if (document(); as doc) {
      <div class="flex justify-end gap-2 mb-4">
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
          <hlm-dropdown-menu>
            <button hlmDropdownMenuItem variant="destructive" (triggered)="handleDelete()" [disabled]="deleting()">
              <ng-icon hlm name="lucideTrash2" size="sm" />
              <span>{{ deleting() ? 'Deleting...' : 'Delete' }}</span>
            </button>
          </hlm-dropdown-menu>
        </ng-template>
      </div>

      <!-- Resizable layout -->
      <div hlmResizableGroup direction="horizontal" class="min-h-0 flex-1 rounded-md border">
        <!-- Preview panel -->
        <div hlmResizablePanel [defaultSize]="60" [minSize]="30" class="overflow-hidden">
          @if (isPdf()) {
            <iframe
              [src]="safeFileUrl()"
              class="h-full w-full"
              [title]="'Preview of ' + doc.name"
            ></iframe>
          } @else {
            <div
              class="flex h-full flex-col items-center justify-center gap-2 bg-muted text-muted-foreground"
            >
              <ng-icon hlm name="lucideFileText" size="xl" />
              <span class="text-sm">Preview not available</span>
            </div>
          }
        </div>

        <hlm-resizable-handle withHandle />

        <!-- Info/Text tabs panel -->
        <div hlmResizablePanel [defaultSize]="40" [minSize]="25" class="flex flex-col overflow-hidden">
          <hlm-tabs tab="info" class="flex flex-1 flex-col overflow-hidden">
            <hlm-tabs-list variant="line" class="mx-4 mt-3">
              <button hlmTabsTrigger="info">Info</button>
              <button hlmTabsTrigger="text">Text</button>
            </hlm-tabs-list>
            <div hlmTabsContent="info" class="flex-1 overflow-y-auto px-4 py-3">
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
                    {{ formatBytes(+doc.fileSize) }}
                  </p>
                </div>

                <!-- Type -->
                <div class="space-y-2">
                  <label hlmLabel>Type</label>
                  <p class="text-sm text-muted-foreground">{{ doc.contentType }}</p>
                </div>
              </div>
            </div>
            <div hlmTabsContent="text" class="flex-1 overflow-y-auto px-4 py-3">
              @if (doc.extractedText) {
                <div class="max-h-96 overflow-y-auto rounded-md border border-border bg-muted p-4">
                  <pre class="whitespace-pre-wrap text-sm text-foreground">{{ doc.extractedText }}</pre>
                </div>
              } @else {
                <div class="flex flex-col items-center justify-center gap-2 py-8 text-muted-foreground">
                  <ng-icon hlm name="lucideFileSearch" size="xl" />
                  <span class="text-sm">No extracted text available</span>
                </div>
              }
            </div>
          </hlm-tabs>
        </div>
      </div>
    }
  `,
  host: {
    class: 'flex flex-col h-full',
  },
})
export class DocumentDetailPageComponent implements OnDestroy {
  editName = signal('');
  editTags = signal<string[]>([]);
  editParties = signal<PartyOption[]>([]);
  saving = signal(false);
  deleting = signal(false);
  loading = signal(false);
  private refreshTrigger = signal(0);

  formatBytes = formatBytes;

  private http = inject(HttpClient);
  private apiConfig = inject(ApiConfiguration);
  private sanitizer = inject(DomSanitizer);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private processingHub = inject(ProcessingHubService);
  private pageHeaderService = inject(PageHeaderService);
  private hubSub: Subscription;

  private headerTitleRef = viewChild<TemplateRef<unknown>>('headerTitle');

  private documentId = toSignal(
    this.route.paramMap.pipe(map((p) => p.get('id'))),
    { initialValue: null },
  );

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
    return false;
  });

  private _fileBlob = toSignal(
    combineLatest([toObservable(this.documentId), toObservable(this.refreshTrigger)]).pipe(
      tap(() => this.revokeCurrentBlobUrl()),
      switchMap(([id]) => {
        if (!id) return of(null);
        return this.http
          .get(`/api/v1/documents/${id}/file`, { responseType: 'blob' })
          .pipe(
            map((blob) => URL.createObjectURL(blob)),
            catchError(() => of(null)),
          );
      }),
      tap((url) => this.fileBlobUrl.set(url)),
    ),
    { initialValue: null },
  );

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
      }
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
    this.revokeCurrentBlobUrl();
    this.hubSub.unsubscribe();
    this.pageHeaderService.clearTitle();
  }

  isPdf(): boolean {
    return this.document()?.contentType === 'application/pdf';
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

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  handleDownload(): void {
    const url = this.fileBlobUrl();
    const doc = this.document();
    if (!url || !doc) return;
    const a = document.createElement('a');
    a.href = url;
    a.download = `${doc.name}${doc.fileExtension}`;
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
