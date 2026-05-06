import { Component, ChangeDetectionStrategy, inject, LOCALE_ID } from '@angular/core';
import { formatDate } from '@angular/common';
import { type CellContext, injectFlexRenderContext } from '@tanstack/angular-table';
import { formatBytes } from '@/shared/utils';
import { TagBadgeComponent } from '../../tags/tag-badge/tag-badge.component';
import { PartyBadgeComponent } from '../../parties/party-badge/party-badge.component';
import type { GetDocumentsResponse } from '@/api/generated/models/get-documents-response';

@Component({
  selector: 'app-name-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host ::ng-deep .match-snippet mark {
      background-color: oklch(var(--primary) / 0.18);
      color: inherit;
      padding: 0 1px;
      border-radius: 2px;
      font-weight: 600;
    }
  `,
  template: `
    <div class="font-medium truncate">{{ doc.name }}</div>
    @if (doc.matchSnippet) {
      <div
        class="match-snippet text-xs text-muted-foreground line-clamp-1"
        [innerHTML]="doc.matchSnippet"
      ></div>
    }
  `,
})
export class NameCell {
  protected readonly context = injectFlexRenderContext<CellContext<GetDocumentsResponse, unknown>>();
  protected get doc(): GetDocumentsResponse {
    return this.context.row.original;
  }
}

@Component({
  selector: 'app-tags-cell',
  imports: [TagBadgeComponent],
  template: `
    <div class="flex flex-wrap gap-1">
      @for (tag of context.row.original.tags; track tag.id) {
        <app-tag-badge [path]="tag.path" [color]="tag.effectiveColor" />
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

@Component({
  selector: 'app-date-added-cell',
  template: `<span class="text-muted-foreground">{{ formatted }}</span>`,
})
export class DateAddedCell {
  private locale = inject(LOCALE_ID);
  protected readonly context = injectFlexRenderContext<CellContext<GetDocumentsResponse, unknown>>();
  get formatted(): string {
    return formatDate(this.context.row.original.createdAt!, 'mediumDate', this.locale);
  }
}

@Component({
  selector: 'app-modified-cell',
  template: `<span class="text-muted-foreground">{{ formatted }}</span>`,
})
export class ModifiedCell {
  private locale = inject(LOCALE_ID);
  protected readonly context = injectFlexRenderContext<CellContext<GetDocumentsResponse, unknown>>();
  get formatted(): string {
    return formatDate(this.context.row.original.updatedAt, 'mediumDate', this.locale);
  }
}

@Component({
  selector: 'app-document-date-cell',
  template: `<span class="text-muted-foreground">{{ formatted }}</span>`,
})
export class DocumentDateCell {
  private locale = inject(LOCALE_ID);
  protected readonly context = injectFlexRenderContext<CellContext<GetDocumentsResponse, unknown>>();
  get formatted(): string {
    const value = this.context.row.original.documentDate;
    return value ? formatDate(value, 'mediumDate', this.locale) : '\u2014';
  }
}
