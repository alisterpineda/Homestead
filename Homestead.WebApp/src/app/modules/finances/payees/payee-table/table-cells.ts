import { Component, ChangeDetectionStrategy, inject, LOCALE_ID } from '@angular/core';
import { formatDate } from '@angular/common';
import { type CellContext, injectFlexRenderContext } from '@tanstack/angular-table';
import { PayeeRowActionsComponent } from '../payee-row-actions/payee-row-actions.component';
import type { PayeeResponse } from '@/api/generated/models/payee-response';
import type { PayeeTableFeatures } from './payee-table.component';

/** Table meta contract — the list page supplies the row-action handlers. */
export interface PayeeTableMeta {
  onEdit: (payee: PayeeResponse) => void;
  onMerge: (payee: PayeeResponse) => void;
  onDelete: (payee: PayeeResponse) => void;
}

@Component({
  selector: 'app-payee-name-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex min-w-0 items-center gap-2">
      <span class="min-w-0 truncate text-sm font-medium">{{ payee.name }}</span>
      @if (payee.isArchived) {
        <span class="shrink-0 rounded-full bg-muted px-2 py-0.5 text-xs text-muted-foreground">Archived</span>
      }
    </div>
  `,
})
export class PayeeNameCell {
  private readonly ctx = injectFlexRenderContext<CellContext<PayeeTableFeatures, PayeeResponse, unknown>>();
  protected get payee(): PayeeResponse { return this.ctx.row.original; }
}

@Component({
  selector: 'app-payee-updated-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="text-muted-foreground">{{ formatted }}</span>`,
})
export class PayeeUpdatedCell {
  private readonly locale = inject(LOCALE_ID);
  private readonly ctx = injectFlexRenderContext<CellContext<PayeeTableFeatures, PayeeResponse, unknown>>();
  protected get formatted(): string {
    const value = this.ctx.row.original.updatedAt;
    return value ? formatDate(value, 'mediumDate', this.locale) : '—';
  }
}

@Component({
  selector: 'app-payee-actions-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PayeeRowActionsComponent],
  template: `
    <app-payee-row-actions
      [payee]="payee"
      (edit)="meta.onEdit($event)"
      (merge)="meta.onMerge($event)"
      (delete)="meta.onDelete($event)"
    />
  `,
})
export class PayeeActionsCell {
  private readonly ctx = injectFlexRenderContext<CellContext<PayeeTableFeatures, PayeeResponse, unknown>>();
  protected get meta(): PayeeTableMeta { return this.ctx.table.options.meta as PayeeTableMeta; }
  protected get payee(): PayeeResponse { return this.ctx.row.original; }
}
