import { Component } from '@angular/core';
import { type CellContext, injectFlexRenderContext } from '@tanstack/angular-table';
import { formatBytes } from '@/shared/utils';
import { TagBadgeComponent } from '../tag-badge/tag-badge.component';
import { PartyBadgeComponent } from '../party-badge/party-badge.component';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';

@Component({
  selector: 'app-tags-cell',
  imports: [TagBadgeComponent],
  template: `
    <div class="flex flex-wrap gap-1">
      @for (tag of context.row.original.tags; track tag.id) {
        <app-tag-badge [path]="tag.path" />
      }
    </div>
  `,
})
export class TagsCell {
  protected readonly context = injectFlexRenderContext<CellContext<GetDocumentsResponse, unknown>>();
}

@Component({
  selector: 'app-parties-cell',
  imports: [PartyBadgeComponent],
  template: `
    <div class="flex flex-wrap gap-1">
      @for (party of context.row.original.parties; track party.id) {
        <app-party-badge [name]="party.name" />
      }
    </div>
  `,
})
export class PartiesCell {
  protected readonly context = injectFlexRenderContext<CellContext<GetDocumentsResponse, unknown>>();
}

@Component({
  selector: 'app-file-size-cell',
  template: `<span class="text-muted-foreground">{{ formatted }}</span>`,
})
export class FileSizeCell {
  protected readonly context = injectFlexRenderContext<CellContext<GetDocumentsResponse, unknown>>();
  get formatted(): string {
    return formatBytes(+this.context.row.original.fileSize);
  }
}
