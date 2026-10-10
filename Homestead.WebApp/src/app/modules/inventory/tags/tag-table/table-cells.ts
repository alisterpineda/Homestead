import { Component, inject, LOCALE_ID } from '@angular/core';
import { formatDate } from '@angular/common';
import { RouterLink } from '@angular/router';
import { type CellContext, injectFlexRenderContext } from '@tanstack/angular-table';
import type { GetInventoryTagsResponse } from '@/api/generated/models/get-inventory-tags-response';
import type { InventoryTagTableFeatures } from './tag-table.component';

@Component({
  selector: 'app-tag-color-cell',
  template: `
    @if (context.row.original.effectiveColor) {
      <span class="inline-block h-3 w-3 rounded-full align-middle" [style.background-color]="context.row.original.effectiveColor"></span>
    }
  `,
})
export class TagColorCell {
  protected readonly context = injectFlexRenderContext<CellContext<InventoryTagTableFeatures, GetInventoryTagsResponse, unknown>>();
}

@Component({
  selector: 'app-tag-name-cell',
  imports: [RouterLink],
  template: `
    <a
      [routerLink]="['/inventory/items']"
      [queryParams]="{ tagIds: context.row.original.id }"
      (click)="$event.stopPropagation()"
      class="hover:underline"
    >
      @if (parentPrefix) {
        <span class="text-muted-foreground">{{ parentPrefix }} / </span>
      }
      <span class="font-medium">{{ context.row.original.name }}</span>
    </a>
  `,
})
export class TagNameCell {
  protected readonly context = injectFlexRenderContext<CellContext<InventoryTagTableFeatures, GetInventoryTagsResponse, unknown>>();

  get parentPrefix(): string {
    const path = this.context.row.original.path;
    const name = this.context.row.original.name;
    if (!path || path === name) return '';
    const prefixRaw = path.substring(0, path.length - name.length - 1);
    return prefixRaw.replace(/\//g, ' / ');
  }
}

@Component({
  selector: 'app-tag-date-added-cell',
  template: `<span class="text-muted-foreground">{{ formatted }}</span>`,
})
export class TagDateAddedCell {
  private locale = inject(LOCALE_ID);
  protected readonly context = injectFlexRenderContext<CellContext<InventoryTagTableFeatures, GetInventoryTagsResponse, unknown>>();
  get formatted(): string {
    return formatDate(this.context.row.original.createdAt!, 'mediumDate', this.locale);
  }
}

@Component({
  selector: 'app-tag-modified-cell',
  template: `<span class="text-muted-foreground">{{ formatted }}</span>`,
})
export class TagModifiedCell {
  private locale = inject(LOCALE_ID);
  protected readonly context = injectFlexRenderContext<CellContext<InventoryTagTableFeatures, GetInventoryTagsResponse, unknown>>();
  get formatted(): string {
    return formatDate(this.context.row.original.updatedAt, 'mediumDate', this.locale);
  }
}
