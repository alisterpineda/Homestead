import { Component, inject, LOCALE_ID } from '@angular/core';
import { formatDate } from '@angular/common';
import { type CellContext, injectFlexRenderContext } from '@tanstack/angular-table';
import type { GetPartiesResponse } from '@/api/generated/models/get-parties-response';

@Component({
  selector: 'app-party-date-added-cell',
  template: `<span class="text-muted-foreground">{{ formatted }}</span>`,
})
export class PartyDateAddedCell {
  private locale = inject(LOCALE_ID);
  protected readonly context = injectFlexRenderContext<CellContext<GetPartiesResponse, unknown>>();
  get formatted(): string {
    return formatDate(this.context.row.original.createdAt!, 'mediumDate', this.locale);
  }
}

@Component({
  selector: 'app-party-modified-cell',
  template: `<span class="text-muted-foreground">{{ formatted }}</span>`,
})
export class PartyModifiedCell {
  private locale = inject(LOCALE_ID);
  protected readonly context = injectFlexRenderContext<CellContext<GetPartiesResponse, unknown>>();
  get formatted(): string {
    return formatDate(this.context.row.original.updatedAt, 'mediumDate', this.locale);
  }
}
