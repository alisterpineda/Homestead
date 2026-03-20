import { Component, inject, LOCALE_ID } from '@angular/core';
import { formatDate } from '@angular/common';
import { type CellContext, injectFlexRenderContext } from '@tanstack/angular-table';
import type { GetTagsResponse } from '@/api/generated/models/get-tags-response';

@Component({
  selector: 'app-tag-name-cell',
  template: `
    <span class="font-medium">{{ context.row.original.name }}</span>
    @if (context.row.original.effectiveColor) {
      <span class="ml-2 inline-block h-3 w-3 rounded-full" [style.background-color]="context.row.original.effectiveColor"></span>
    }
  `,
})
export class TagNameCell {
  protected readonly context = injectFlexRenderContext<CellContext<GetTagsResponse, unknown>>();
}

@Component({
  selector: 'app-tag-date-added-cell',
  template: `<span class="text-muted-foreground">{{ formatted }}</span>`,
})
export class TagDateAddedCell {
  private locale = inject(LOCALE_ID);
  protected readonly context = injectFlexRenderContext<CellContext<GetTagsResponse, unknown>>();
  get formatted(): string {
    return formatDate(this.context.row.original.createdAt!, 'mediumDate', this.locale);
  }
}
