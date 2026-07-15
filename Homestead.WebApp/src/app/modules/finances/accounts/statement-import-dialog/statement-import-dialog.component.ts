import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { Router } from '@angular/router';
import { HttpEventType, HttpResponse } from '@angular/common/http';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { firstValueFrom, of } from 'rxjs';
import { filter, map, debounceTime, switchMap, catchError } from 'rxjs/operators';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFileUp, lucideFileText, lucideSearch } from '@ng-icons/lucide';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmTabsImports } from '@/shared/components/spartan/tabs';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { SearchInputComponent } from '@/shared/components/search-input/search-input.component';
import { UploadDropzoneComponent } from '../../../documents/list/upload-dropzone/upload-dropzone.component';
import { DocumentApiService } from '@/api/services/document-api.service';
import { FinancesStatementApiService } from '@/api/services/finances-statement-api.service';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import type { CreateDocumentResponse } from '@/api/generated/models/create-document-response';
import type { GetDocumentResponse } from '@/api/generated/models/get-document-response';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';

/**
 * Entry point for importing a statement into an account. Offers two sources:
 *  - Upload a new document (primary): upload → wait for processing → parse → review.
 *  - Pick an existing document: search → select → parse → review.
 * On a successful parse it navigates to the two-panel review route.
 */
@Component({
  selector: 'app-statement-import-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmDialogImports,
    HlmButton,
    HlmSpinner,
    HlmTabsImports,
    HlmIconImports,
    NgIcon,
    SearchInputComponent,
    UploadDropzoneComponent,
  ],
  providers: [provideIcons({ lucideFileUp, lucideFileText, lucideSearch })],
  template: `
    <hlm-dialog (stateChanged)="onStateChanged($event)">
      <button hlmBtn [size]="triggerSize()" [variant]="triggerVariant()" hlmDialogTrigger>
        <ng-content />
      </button>

      <hlm-dialog-content
        *hlmDialogPortal="let ctx"
        class="max-h-[calc(100dvh-2rem)] overflow-y-auto sm:max-w-[560px]"
      >
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Import statement</h2>
          <p hlmDialogDescription>
            Upload a statement or pick an existing document. We'll read its closing balance for you to review.
          </p>
        </div>

        @if (busy()) {
          <div class="flex flex-col items-center justify-center gap-3 py-10">
            <hlm-spinner />
            <p class="text-sm text-muted-foreground">{{ status() }}</p>
          </div>
        } @else {
          <hlm-tabs tab="upload" class="w-full">
            <hlm-tabs-list class="w-full" aria-label="Statement source">
              <button hlmTabsTrigger="upload" class="flex-1">
                <ng-icon hlm name="lucideFileUp" size="sm" />
                Upload
              </button>
              <button hlmTabsTrigger="existing" class="flex-1">
                <ng-icon hlm name="lucideFileText" size="sm" />
                Existing
              </button>
            </hlm-tabs-list>

            <div hlmTabsContent="upload" class="flex flex-col gap-4 pt-4">
              <app-upload-dropzone [file]="file()" (fileSelect)="file.set($event)" />
              <div hlmDialogFooter>
                <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
                <button hlmBtn type="button" [disabled]="!file()" (click)="startUpload()">
                  Import
                </button>
              </div>
            </div>

            <div hlmTabsContent="existing" class="flex flex-col gap-3 pt-4">
              <app-search-input [(value)]="search" placeholder="Search documents…" />
              <div class="max-h-64 overflow-y-auto rounded-md border border-border">
                @if (results().length === 0) {
                  <p class="px-3 py-6 text-center text-sm text-muted-foreground">
                    {{ search().trim() ? 'No documents found.' : 'Type to search documents.' }}
                  </p>
                } @else {
                  <ul class="divide-y divide-border">
                    @for (doc of results(); track doc.id) {
                      <li>
                        <button
                          type="button"
                          class="flex w-full items-center gap-2 px-3 py-2 text-left text-sm hover:bg-accent"
                          (click)="startExisting(doc.id)"
                        >
                          <ng-icon hlm name="lucideFileText" size="sm" class="text-muted-foreground" />
                          <span class="truncate">{{ doc.name }}</span>
                        </button>
                      </li>
                    }
                  </ul>
                }
              </div>
              <div hlmDialogFooter>
                <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
              </div>
            </div>
          </hlm-tabs>
        }
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class StatementImportDialogComponent {
  accountId = input.required<string>();
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon' | 'icon-sm' | 'icon-lg'>('sm');
  triggerVariant = input<'default' | 'secondary' | 'outline' | 'ghost'>('outline');

  file = signal<File | null>(null);
  search = signal('');
  busy = signal(false);
  status = signal('');

  private dialog = viewChild(BrnDialog);
  private documentApi = inject(DocumentApiService);
  private statementApi = inject(FinancesStatementApiService);
  private processingHub = inject(ProcessingHubService);
  private errorHandler = inject(ErrorHandlerService);
  private router = inject(Router);

  readonly results = toSignal(
    toObservable(this.search).pipe(
      debounceTime(300),
      switchMap((term) =>
        term.trim()
          ? this.documentApi.getDocuments({ Search: term, PageSize: 10, IncludeChildTags: false }).pipe(
              map((r) => r.items),
              catchError(() => of([] as GetDocumentsResponse[])),
            )
          : of([] as GetDocumentsResponse[]),
      ),
    ),
    { initialValue: [] as GetDocumentsResponse[] },
  );

  onStateChanged(state: 'open' | 'closed'): void {
    if (state === 'open') {
      this.file.set(null);
      this.search.set('');
      this.busy.set(false);
      this.status.set('');
    }
  }

  startUpload(): void {
    const f = this.file();
    if (!f) return;
    void this.runUpload(f, this.accountId());
  }

  startExisting(documentId: string): void {
    void this.runParse(documentId, this.accountId());
  }

  private async runUpload(file: File, accountId: string): Promise<void> {
    this.busy.set(true);
    this.status.set('Uploading…');
    try {
      const upload = await firstValueFrom(
        this.documentApi.uploadDocument(file).pipe(
          filter((e): e is HttpResponse<CreateDocumentResponse> => e.type === HttpEventType.Response),
          map((e) => e.body!),
        ),
      );
      this.status.set('Processing document…');
      const uploadMsg = await this.processingHub.waitForJob(upload.jobId);
      const documentId = uploadMsg.entityId;
      if (!documentId) throw new Error('Upload did not return a document.');
      await this.parseAndNavigate(documentId, accountId);
    } catch (err) {
      this.fail(err);
    }
  }

  private async runParse(documentId: string, accountId: string): Promise<void> {
    this.busy.set(true);
    try {
      await this.parseAndNavigate(documentId, accountId);
    } catch (err) {
      this.fail(err);
    }
  }

  private async parseAndNavigate(documentId: string, accountId: string): Promise<void> {
    this.status.set('Reading statement…');
    const doc = await firstValueFrom(this.documentApi.getDocument({ id: documentId }));
    const versionId = this.currentVersionId(doc);
    if (!versionId) throw new Error('Document has no version to import.');

    const parse = await firstValueFrom(
      this.statementApi.initiate({ body: { accountId, documentVersionId: versionId } }),
    );
    // waitForJob throws on Failed (e.g. AI unavailable) — the catch surfaces it as a toast.
    await this.processingHub.waitForJob(parse.jobId);

    this.busy.set(false);
    this.dialog()?.close();
    await this.router.navigate(['/finances/accounts', accountId, 'statement-import', parse.jobId]);
  }

  private currentVersionId(doc: GetDocumentResponse): string | null {
    const versions = doc.versions ?? [];
    if (versions.length === 0) return null;
    return [...versions].sort((a, b) => b.versionNumber - a.versionNumber)[0].id;
  }

  private fail(err: unknown): void {
    this.busy.set(false);
    this.status.set('');
    toast.error(this.errorHandler.getErrorMessage(err, 'Statement import failed.'));
  }
}
