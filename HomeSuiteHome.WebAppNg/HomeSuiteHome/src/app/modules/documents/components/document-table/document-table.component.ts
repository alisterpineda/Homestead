import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { cn, formatBytes } from '@/shared/utils';
import { TagBadgeComponent } from '../tag-badge/tag-badge.component';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';

@Component({
  selector: 'app-document-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TagBadgeComponent],
  template: `
    <!-- Mobile card layout -->
    <div class="flex flex-col gap-3 md:hidden">
      @for (doc of documents(); track doc.id) {
        <div
          (click)="documentSelect.emit(doc.id)"
          [class]="
            cn(
              'cursor-pointer rounded-lg border border-border p-4 transition-colors',
              selectedDocumentId() === doc.id
                ? 'bg-accent-50'
                : 'hover:bg-surface-raised'
            )
          "
        >
          <p class="font-medium text-text-primary">{{ doc.name }}</p>
          <div class="mt-2 flex items-center gap-3 text-xs text-text-muted">
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
        </div>
      }
    </div>

    <!-- Desktop table layout -->
    <div class="hidden overflow-hidden rounded-lg border border-border md:block">
      <table class="w-full text-left text-sm">
        <thead>
          <tr class="border-b border-border bg-surface-raised">
            <th class="px-4 py-3 font-medium text-text-secondary">Name</th>
            <th class="px-4 py-3 font-medium text-text-secondary">Tags</th>
            <th class="px-4 py-3 font-medium text-text-secondary">Content Type</th>
            <th class="px-4 py-3 font-medium text-text-secondary text-right">File Size</th>
          </tr>
        </thead>
        <tbody>
          @for (doc of documents(); track doc.id) {
            <tr
              (click)="documentSelect.emit(doc.id)"
              [class]="
                cn(
                  'border-b border-border last:border-0 cursor-pointer transition-colors',
                  selectedDocumentId() === doc.id
                    ? 'bg-accent-50'
                    : 'hover:bg-surface-raised'
                )
              "
            >
              <td class="px-4 py-3 font-medium text-text-primary">{{ doc.name }}</td>
              <td class="px-4 py-3">
                <div class="flex flex-wrap gap-1">
                  @for (tag of doc.tags; track tag.id) {
                    <app-tag-badge [path]="tag.path" />
                  }
                </div>
              </td>
              <td class="px-4 py-3 text-text-secondary">{{ doc.contentType }}</td>
              <td class="px-4 py-3 text-text-secondary text-right">
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

  cn = cn;
  formatBytes = formatBytes;
}
