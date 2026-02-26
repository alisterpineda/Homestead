import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { formatBytes } from '@/shared/utils';
import { HlmTableImports } from '@/shared/components/spartan/table';
import { TagBadgeComponent } from '../tag-badge/tag-badge.component';
import { PartyBadgeComponent } from '../party-badge/party-badge.component';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';

@Component({
  selector: 'app-document-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmTableImports, TagBadgeComponent, PartyBadgeComponent],
  template: `
    <div hlmTableContainer class="overflow-hidden rounded-md border">
      <table hlmTable>
        <thead hlmTHead>
          <tr hlmTr>
            <th hlmTh>Name</th>
            <th hlmTh>Tags</th>
            <th hlmTh class="hidden xl:table-cell">Parties</th>
            <th hlmTh class="hidden lg:table-cell">Content Type</th>
            <th hlmTh class="text-right">File Size</th>
          </tr>
        </thead>
        <tbody hlmTBody>
          @for (doc of documents(); track doc.id) {
            <tr
              hlmTr
              tabindex="0"
              (click)="documentSelect.emit(doc.id)"
              (keydown.enter)="documentSelect.emit(doc.id)"
              class="cursor-pointer"
            >
              <td hlmTd class="font-medium">{{ doc.name }}</td>
              <td hlmTd>
                <div class="flex flex-wrap gap-1">
                  @for (tag of doc.tags; track tag.id) {
                    <app-tag-badge [path]="tag.path" />
                  }
                </div>
              </td>
              <td hlmTd class="hidden xl:table-cell">
                <div class="flex flex-wrap gap-1">
                  @for (party of doc.parties; track party.id) {
                    <app-party-badge [name]="party.name" />
                  }
                </div>
              </td>
              <td hlmTd class="hidden lg:table-cell text-muted-foreground">{{ doc.contentType }}</td>
              <td hlmTd class="text-right text-muted-foreground">
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
  documentSelect = output<string>();

  formatBytes = formatBytes;
}
