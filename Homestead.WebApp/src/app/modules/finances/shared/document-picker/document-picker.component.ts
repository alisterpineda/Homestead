import { Component, ChangeDetectionStrategy, inject, model, output } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { of } from 'rxjs';
import { map, debounceTime, switchMap, catchError } from 'rxjs/operators';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFileText } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { SearchInputComponent } from '@/shared/components/search-input/search-input.component';
import { DocumentApiService } from '@/api/services/document-api.service';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';

/**
 * Debounced document search + result list, extracted from the statement-import dialog so any
 * flow that needs to pick an existing document (statement import, transaction document links)
 * shares one implementation. Trashed documents never appear: the backend's global query filter
 * excludes them from the documents list/search endpoint.
 */
@Component({
  selector: 'app-document-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports, SearchInputComponent],
  providers: [provideIcons({ lucideFileText })],
  template: `
    <app-search-input [(value)]="search" placeholder="Search documents…" />
    <div class="mt-3 max-h-64 overflow-y-auto rounded-md border border-border">
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
                (click)="documentSelected.emit(doc)"
              >
                <ng-icon hlm name="lucideFileText" size="sm" class="text-muted-foreground" />
                <span class="truncate">{{ doc.name }}</span>
              </button>
            </li>
          }
        </ul>
      }
    </div>
  `,
})
export class DocumentPickerComponent {
  /** Two-way so hosts can reset the term when their dialog (re)opens. */
  search = model('');
  documentSelected = output<GetDocumentsResponse>();

  private documentApi = inject(DocumentApiService);

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
}
