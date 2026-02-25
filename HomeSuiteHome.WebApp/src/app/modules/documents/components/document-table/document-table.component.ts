import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { formatBytes } from '@/shared/utils';
import { TagBadgeComponent } from '../tag-badge/tag-badge.component';
import { PartyBadgeComponent } from '../party-badge/party-badge.component';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';

@Component({
  selector: 'app-document-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TagBadgeComponent, PartyBadgeComponent],
  template: `
    <!-- Mobile card layout -->
    <div class="flex flex-col gap-3 md:hidden">
      @for (doc of documents(); track doc.id) {
        <button
          type="button"
          (click)="documentSelect.emit(doc.id)"
          [class]="
            'w-full cursor-pointer rounded-lg border border-border p-4 text-left transition-colors ' +
            (selectedDocumentId() === doc.id ? 'bg-accent' : 'hover:bg-muted')
          "
        >
          <p class="font-medium text-foreground">{{ doc.name }}</p>
          <div class="mt-2 flex items-center gap-3 text-xs text-muted-foreground">
            <span>{{ doc.contentType }}</span>
            <span>{{ formatBytes(+doc.fileSize) }}</span>
          </div>
          @if (doc.tags && doc.tags.length > 0) {
            <div class="mt-2 flex flex-wrap gap-1">
              @for (tag of doc.tags; track tag.id) {
                <app-tag-badge [path]="tag.path" />
              }
            </div>
          }
          @if (doc.parties && doc.parties.length > 0) {
            <div class="mt-2 flex flex-wrap gap-1">
              @for (party of doc.parties; track party.id) {
                <app-party-badge [name]="party.name" />
              }
            </div>
          }
        </button>
      }
    </div>

    <!-- Desktop table layout -->
    <div class="hidden overflow-hidden rounded-lg border border-border md:block">
      <table class="w-full text-left text-sm">
        <thead>
          <tr class="border-b border-border bg-muted">
            <th class="px-4 py-3 font-medium text-muted-foreground">Name</th>
            <th class="px-4 py-3 font-medium text-muted-foreground">Tags</th>
            <th class="px-4 py-3 font-medium text-muted-foreground">Parties</th>
            <th class="px-4 py-3 font-medium text-muted-foreground">Content Type</th>
            <th class="px-4 py-3 font-medium text-muted-foreground text-right">File Size</th>
          </tr>
        </thead>
        <tbody>
          @for (doc of documents(); track doc.id) {
            <tr
              tabindex="0"
              (click)="documentSelect.emit(doc.id)"
              (keydown.enter)="documentSelect.emit(doc.id)"
              [class]="
                'border-b border-border last:border-0 cursor-pointer transition-colors ' +
                (selectedDocumentId() === doc.id ? 'bg-accent' : 'hover:bg-muted')
              "
            >
              <td class="px-4 py-3 font-medium text-foreground">{{ doc.name }}</td>
              <td class="px-4 py-3">
                <div class="flex flex-wrap gap-1">
                  @for (tag of doc.tags; track tag.id) {
                    <app-tag-badge [path]="tag.path" />
                  }
                </div>
              </td>
              <td class="px-4 py-3">
                <div class="flex flex-wrap gap-1">
                  @for (party of doc.parties; track party.id) {
                    <app-party-badge [name]="party.name" />
                  }
                </div>
              </td>
              <td class="px-4 py-3 text-muted-foreground">{{ doc.contentType }}</td>
              <td class="px-4 py-3 text-muted-foreground text-right">
                {{ formatBytes(+doc.fileSize) }}
              </td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
})
export class DocumentTableComponent {
  documents = input.required<GetDocumentsResponse[]>();
  selectedDocumentId = input<string | null>(null);
  documentSelect = output<string>();

  formatBytes = formatBytes;
}
