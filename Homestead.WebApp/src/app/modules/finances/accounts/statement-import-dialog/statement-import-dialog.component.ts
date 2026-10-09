import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { HttpEventType, HttpResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { filter, map } from 'rxjs/operators';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFileUp, lucideFileText } from '@ng-icons/lucide';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmTabsImports } from '@/shared/components/spartan/tabs';
import { DocumentPickerComponent } from '../../shared/document-picker/document-picker.component';
import { UploadDropzoneComponent } from '../../../documents/list/upload-dropzone/upload-dropzone.component';
import { DocumentApiService } from '@/api/services/document-api.service';
import { FinancesStatementApiService } from '@/api/services/finances-statement-api.service';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { JobFailedError } from '@/shared/types/job-failed-error';
import type { CreateDocumentResponse } from '@/api/generated/models/create-document-response';
import type { GetDocumentResponse } from '@/api/generated/models/get-document-response';

const IMPORT_FAILED = 'Statement import failed.';

// The initiate guard tags a re-import rejection with this stable code (a document is already imported).
// It is recoverable in place — surfaced as a warning with a Roll back action — unlike every other 400.
const STATEMENT_ALREADY_IMPORTED = 'statement_already_imported';

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
    NgIcon,
    DocumentPickerComponent,
    UploadDropzoneComponent,
    RouterLink,
  ],
  providers: [provideIcons({ lucideFileUp, lucideFileText })],
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
          @if (importError(); as err) {
            @if (err.alreadyImported) {
              <!-- Recoverable: this document was imported before. Warn (amber, not a red error) and offer the
                   in-place remedy — roll back the prior import, then proceed with this one. -->
              <div
                class="rounded-md border border-amber-500/50 bg-amber-500/10 px-3 py-2.5"
                role="alert"
              >
                <p class="text-sm text-amber-600">{{ err.message }}</p>
                <div class="mt-2 flex flex-wrap items-center gap-x-4 gap-y-2">
                  <button
                    hlmBtn
                    type="button"
                    [disabled]="rollingBack()"
                    (click)="rollbackPreviousImport()"
                  >
                    {{ rollingBack() ? 'Rolling back…' : 'Roll back previous import' }}
                  </button>
                  @if (err.document; as errDoc) {
                    <a
                      [routerLink]="['/documents', errDoc.id]"
                      (click)="dialog()?.close()"
                      class="text-sm font-medium text-primary underline underline-offset-4"
                    >
                      Open {{ errDoc.name }}
                    </a>
                  }
                </div>
              </div>
            } @else {
              <div
                class="rounded-md border border-destructive/50 bg-destructive/10 px-3 py-2.5"
                role="alert"
              >
                <p class="text-sm text-destructive">{{ err.message }}</p>
                @if (err.document; as errDoc) {
                  <a
                    [routerLink]="['/documents', errDoc.id]"
                    (click)="dialog()?.close()"
                    class="mt-1.5 inline-block text-sm font-medium text-primary underline underline-offset-4"
                  >
                    Open {{ errDoc.name }}
                  </a>
                }
              </div>
            }
          }

          <!-- The busy branch destroys these tabs, so the active one is tracked here to survive the flip;
               otherwise an error raised from Existing renders above the Upload tab's empty dropzone.
               Synced via tabActivated (not per-trigger clicks): automatic activation mode switches tabs on
               arrow-key focus without any click. -->
          <hlm-tabs [tab]="activeTab()" (tabActivated)="onTabActivated($event)" class="w-full">
            <hlm-tabs-list class="w-full" aria-label="Statement source">
              <button hlmTabsTrigger="upload" class="flex-1">
                <ng-icon name="lucideFileUp" class="text-[length:--spacing(4)]" />
                Upload
              </button>
              <button hlmTabsTrigger="existing" class="flex-1">
                <ng-icon name="lucideFileText" class="text-[length:--spacing(4)]" />
                Existing
              </button>
            </hlm-tabs-list>

            <div hlmTabsContent="upload" class="flex flex-col gap-4 pt-4">
              <app-upload-dropzone [file]="file()" (fileSelect)="onFileSelect($event)" />
              <div hlmDialogFooter>
                <button hlmBtn variant="secondary" type="button" hlmDialogClose>Cancel</button>
                <!-- Disabled only while an alert from THIS tab's flow stands: the selected file was already
                     uploaded, so re-importing it would re-upload identical bytes and fail as a duplicate.
                     An error raised from the Existing tab must not block a file that was never tried. -->
                <button
                  hlmBtn
                  type="button"
                  [disabled]="!file() || (importError()?.fromUpload ?? false)"
                  (click)="startUpload()"
                >
                  Import
                </button>
              </div>
            </div>

            <div hlmTabsContent="existing" class="flex flex-col gap-3 pt-4">
              <app-document-picker [(search)]="search" (documentSelected)="startExisting($event.id)" />
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
  triggerSize = input<'default' | 'sm' | 'lg' | 'icon' | 'icon-sm' | 'icon-lg'>('default');
  triggerVariant = input<'default' | 'secondary' | 'outline' | 'ghost'>('outline');

  file = signal<File | null>(null);
  search = signal('');
  busy = signal(false);
  status = signal('');

  activeTab = signal<'upload' | 'existing'>('upload');

  /**
   * A failure raised once a document is on file, rendered in the dialog rather than a toast so it survives
   * long enough to act on. `document` is set only when opening it is the actual remedy — a rejected
   * precondition, or a text_unavailable job failure. Runtime job failures (AI unavailable) get the message
   * without a link, since opening the document would fix nothing. `fromUpload` scopes the Import-button
   * disable to errors from the Upload flow, whose file genuinely must not be re-uploaded. `alreadyImported`
   * flips the presentation to a recoverable warning with a Roll back action (see `rollbackPreviousImport`).
   */
  importError = signal<{
    message: string;
    document: { id: string; name: string } | null;
    fromUpload: boolean;
    alreadyImported: boolean;
  } | null>(null);

  // A rollback of the prior import is in flight (triggered from the already-imported warning).
  rollingBack = signal(false);

  // Whether the dialog is currently open — an inline alert set after close would render nowhere, and the
  // hub's own failure toast is suppressed for managed jobs, so showImportError falls back to a toast then.
  private dialogOpen = false;

  protected dialog = viewChild(BrnDialog);
  private documentApi = inject(DocumentApiService);
  private statementApi = inject(FinancesStatementApiService);
  private processingHub = inject(ProcessingHubService);
  private errorHandler = inject(ErrorHandlerService);
  private router = inject(Router);

  onStateChanged(state: 'open' | 'closed'): void {
    this.dialogOpen = state === 'open';
    if (state === 'open') {
      this.file.set(null);
      this.search.set('');
      this.busy.set(false);
      this.status.set('');
      this.importError.set(null);
      this.activeTab.set('upload');
    }
  }

  onTabActivated(tab: string): void {
    this.activeTab.set(tab === 'existing' ? 'existing' : 'upload');
  }

  onFileSelect(file: File | null): void {
    this.file.set(file);
    // Choosing a different file is the way back from an errored import — it re-enables the Import button.
    this.importError.set(null);
  }

  startUpload(): void {
    const f = this.file();
    if (!f) return;
    this.importError.set(null);
    void this.runUpload(f, this.accountId());
  }

  startExisting(documentId: string): void {
    this.importError.set(null);
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
      await this.parseAndNavigate(documentId, accountId, true);
    } catch (err) {
      this.fail(err);
    }
  }

  private async runParse(documentId: string, accountId: string): Promise<void> {
    this.busy.set(true);
    try {
      await this.parseAndNavigate(documentId, accountId, false);
    } catch (err) {
      this.fail(err);
    }
  }

  private async parseAndNavigate(documentId: string, accountId: string, fromUpload: boolean): Promise<void> {
    this.status.set('Reading statement…');
    const doc = await firstValueFrom(this.documentApi.getDocument({ id: documentId }));
    const versionId = this.currentVersionId(doc);
    if (!versionId) throw new Error('Document has no version to import.');

    let parse: { jobId: string };
    try {
      parse = await firstValueFrom(
        this.statementApi.initiate({ body: { accountId, documentVersionId: versionId } }),
      );
    } catch (err) {
      // 5xx/network failures are already toasted by the error interceptor, and the document is not the
      // problem — surfacing them inline with an "Open document" remedy would double-report and misdiagnose.
      if (this.errorHandler.isServerOrNetworkError(err)) {
        this.busy.set(false);
        this.status.set('');
        return;
      }
      // A rejected precondition — the document exists but isn't importable. "Already imported" is recoverable
      // in place (roll back, then retry), so it renders as a warning; every other precondition (no extracted
      // text, …) is a hard error whose remedy is opening the document. Keep both on screen rather than toasting.
      const alreadyImported = this.errorHandler.getErrorCode(err) === STATEMENT_ALREADY_IMPORTED;
      this.showImportError(err, { id: documentId, name: doc.name }, fromUpload, alreadyImported);
      return;
    }

    try {
      // Throws JobFailedError on Failed — e.g. AI unavailable, or the version's text emptied after enqueue.
      await this.processingHub.waitForJob(parse.jobId);
    } catch (err) {
      // Link only when the document is the remedy: text_unavailable means its text is gone and reprocessing
      // in Documents is the fix. Any other job failure (AI unavailable) gets the message without a link.
      const document =
        err instanceof JobFailedError && err.errorCode === 'text_unavailable'
          ? { id: documentId, name: doc.name }
          : null;
      this.showImportError(err, document, fromUpload);
      return;
    }

    this.busy.set(false);
    this.dialog()?.close();
    await this.router.navigate(['/finances/accounts', accountId, 'statement-import', parse.jobId]);
  }

  private currentVersionId(doc: GetDocumentResponse): string | null {
    const versions = doc.versions ?? [];
    if (versions.length === 0) return null;
    return [...versions].sort((a, b) => b.versionNumber - a.versionNumber)[0].id;
  }

  private showImportError(
    err: unknown,
    document: { id: string; name: string } | null,
    fromUpload: boolean,
    alreadyImported = false,
  ): void {
    this.busy.set(false);
    this.status.set('');
    const message = this.errorHandler.getErrorMessage(err, IMPORT_FAILED);
    // The hub suppresses its own toast for managed jobs and the inline alert unmounts with the dialog, so
    // a failure landing after the user closed the dialog must fall back to a toast or it is lost entirely.
    if (!this.dialogOpen) {
      toast.error(message);
      return;
    }
    this.importError.set({ message, document, fromUpload, alreadyImported });
  }

  // From the already-imported warning: delete the prior import's transactions (and un-learn the payee
  // mappings it taught), then — the document now clean — proceed with the import that was blocked. The
  // document id is the same one the warning links to, and is exactly what the rollback endpoint keys on.
  async rollbackPreviousImport(): Promise<void> {
    const documentId = this.importError()?.document?.id;
    if (!documentId || this.rollingBack()) return;
    this.rollingBack.set(true);
    try {
      await firstValueFrom(this.statementApi.rollback({ documentId }));
    } catch (err) {
      this.rollingBack.set(false);
      // 5xx/network failures are already toasted by the error interceptor; only surface 4xx here, mirroring
      // parseAndNavigate — otherwise the user sees the failure twice. The warning stays up so they can retry.
      if (!this.errorHandler.isServerOrNetworkError(err)) {
        this.errorHandler.handleError(err, 'Failed to roll back the previous import.');
      }
      return;
    }
    this.rollingBack.set(false);
    this.importError.set(null);
    toast.success('Previous import rolled back.');
    await this.runParse(documentId, this.accountId());
  }

  // Toast, not inline: these fail before a document exists to talk about (the upload itself, or fetching
  // it), so there is nothing to keep on screen and nothing to link to.
  private fail(err: unknown): void {
    this.busy.set(false);
    this.status.set('');
    toast.error(this.errorHandler.getErrorMessage(err, IMPORT_FAILED));
  }
}
