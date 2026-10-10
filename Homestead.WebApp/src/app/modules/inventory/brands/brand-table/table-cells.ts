import { Component, ChangeDetectionStrategy, inject, LOCALE_ID } from '@angular/core';
import { formatDate } from '@angular/common';
import { RouterLink } from '@angular/router';
import { type CellContext, injectFlexRenderContext } from '@tanstack/angular-table';
import { BrandRowActionsComponent } from '../brand-row-actions/brand-row-actions.component';
import type { BrandResponse } from '@/api/generated/models/brand-response';
import type { BrandTableFeatures } from './brand-table.component';

/** Table meta contract — the list page supplies the row-action handlers. */
export interface BrandTableMeta {
  onEdit: (brand: BrandResponse) => void;
  onMerge: (brand: BrandResponse) => void;
  onDelete: (brand: BrandResponse) => void;
}

@Component({
  selector: 'app-brand-name-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <a
      routerLink="/inventory/items"
      [queryParams]="{ brand: brand.id }"
      (click)="$event.stopPropagation()"
      class="block min-w-0 truncate text-sm font-medium hover:underline"
    >{{ brand.name }}</a>
  `,
})
export class BrandNameCell {
  private readonly ctx = injectFlexRenderContext<CellContext<BrandTableFeatures, BrandResponse, unknown>>();
  protected get brand(): BrandResponse { return this.ctx.row.original; }
}

@Component({
  selector: 'app-brand-updated-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="text-muted-foreground">{{ formatted }}</span>`,
})
export class BrandUpdatedCell {
  private readonly locale = inject(LOCALE_ID);
  private readonly ctx = injectFlexRenderContext<CellContext<BrandTableFeatures, BrandResponse, unknown>>();
  protected get formatted(): string {
    const value = this.ctx.row.original.updatedAt;
    return value ? formatDate(value, 'mediumDate', this.locale) : '—';
  }
}

@Component({
  selector: 'app-brand-actions-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BrandRowActionsComponent],
  template: `
    <app-brand-row-actions
      [brand]="brand"
      (edit)="meta.onEdit($event)"
      (merge)="meta.onMerge($event)"
      (delete)="meta.onDelete($event)"
    />
  `,
})
export class BrandActionsCell {
  private readonly ctx = injectFlexRenderContext<CellContext<BrandTableFeatures, BrandResponse, unknown>>();
  protected get meta(): BrandTableMeta { return this.ctx.table.options.meta as BrandTableMeta; }
  protected get brand(): BrandResponse { return this.ctx.row.original; }
}
