import { Component, ChangeDetectionStrategy } from '@angular/core';
import { type CellContext, injectFlexRenderContext } from '@tanstack/angular-table';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmDatePicker, HlmDatePickerInput } from '@/shared/components/spartan/date-picker';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { toIsoDateString } from '@/shared/utils';
import { PayeePickerComponent, type PayeePickerValue } from '../shared/payee-picker/payee-picker.component';
import type { FlowDirection } from '@/api/generated/models/flow-direction';
import type { PayeeResponse } from '@/api/generated/models/payee-response';

// Client-side edit state for one proposed line — the review page owns this until commit. `mappable` echoes
// the server's un-mappable flag (missing amount/direction); `amount`/`date` are raw strings for uncontrolled
// inputs (never reformatted while typing). Acceptance is NOT held here — it lives in the table's rowSelection.
export interface LineItemEdit {
  index: number;
  amount: string;
  description: string;
  direction: FlowDirection;
  date: string;
  // Memoized Date for hlm-date-picker's [date] binding: re-derived only when `date` changes (in patchRow),
  // so change detection never hands the picker a fresh Date reference and snaps its open calendar back.
  dateObj: Date | undefined;
  mappable: boolean;
  duplicateSuspected: boolean;
  // Client-side payee pick, lost on reload (not staged server-side) — accepted per the plan. Held as a
  // single stable object so the per-row picker's [value] doesn't get a new identity every change detection.
  payee: PayeePickerValue | null;
}

// Table meta contract — the review page supplies the row-edit handlers, reached via ctx.table.options.meta.
// All handlers key by the STABLE server line index (row.original.index), never array position.
export interface StatementLineTableMeta {
  patchRow(index: number, patch: Partial<LineItemEdit>): void;
  setDirection(index: number, direction: FlowDirection): void;
  onPayeeChange(index: number, payee: PayeeResponse | null): void;
}

@Component({
  selector: 'app-line-date-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmDatePicker, HlmDatePickerInput],
  template: `
    <hlm-date-picker
      class="w-full"
      captionLayout="dropdown"
      [date]="row.dateObj"
      (dateChange)="onDateChange($event)"
    >
      <hlm-date-picker-input [inputId]="inputId" placeholder="Select date" />
    </hlm-date-picker>
  `,
})
export class LineDateCell {
  private readonly ctx = injectFlexRenderContext<CellContext<LineItemEdit, unknown>>();
  protected get row(): LineItemEdit { return this.ctx.row.original; }
  protected get inputId(): string { return `import-line-date-${this.row.index}`; }
  private get meta(): StatementLineTableMeta { return this.ctx.table.options.meta as StatementLineTableMeta; }

  protected onDateChange(date: Date | null): void {
    this.meta.patchRow(this.row.index, { date: date ? (toIsoDateString(date) ?? '') : '' });
  }
}

@Component({
  selector: 'app-line-description-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmInput],
  template: `
    <input
      hlmInput
      class="w-full"
      maxlength="512"
      [value]="row.description"
      (input)="onInput($event)"
      aria-label="Transaction description"
    />
  `,
})
export class LineDescriptionCell {
  private readonly ctx = injectFlexRenderContext<CellContext<LineItemEdit, unknown>>();
  protected get row(): LineItemEdit { return this.ctx.row.original; }
  private get meta(): StatementLineTableMeta { return this.ctx.table.options.meta as StatementLineTableMeta; }

  protected onInput(event: Event): void {
    this.meta.patchRow(this.row.index, { description: (event.target as HTMLInputElement).value });
  }
}

@Component({
  selector: 'app-line-type-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSelectImports],
  template: `
    <hlm-select [value]="row.direction" (valueChange)="onChange($any($event))">
      <hlm-select-trigger class="w-28" aria-label="Transaction type">
        <hlm-select-value />
      </hlm-select-trigger>
      <hlm-select-content *hlmSelectPortal>
        <hlm-select-group>
          <hlm-select-item value="Expense">Expense</hlm-select-item>
          <hlm-select-item value="Income">Income</hlm-select-item>
        </hlm-select-group>
      </hlm-select-content>
    </hlm-select>
  `,
})
export class LineTypeCell {
  private readonly ctx = injectFlexRenderContext<CellContext<LineItemEdit, unknown>>();
  protected get row(): LineItemEdit { return this.ctx.row.original; }
  private get meta(): StatementLineTableMeta { return this.ctx.table.options.meta as StatementLineTableMeta; }

  protected onChange(value: unknown): void {
    if (value === 'Expense' || value === 'Income') this.meta.setDirection(this.row.index, value);
  }
}

@Component({
  selector: 'app-line-payee-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PayeePickerComponent],
  template: `
    <app-payee-picker
      [value]="row.payee"
      ariaLabel="Select payee for this line"
      (valueChange)="onChange($event)"
    />
  `,
})
export class LinePayeeCell {
  private readonly ctx = injectFlexRenderContext<CellContext<LineItemEdit, unknown>>();
  protected get row(): LineItemEdit { return this.ctx.row.original; }
  private get meta(): StatementLineTableMeta { return this.ctx.table.options.meta as StatementLineTableMeta; }

  protected onChange(payee: PayeeResponse | null): void {
    this.meta.onPayeeChange(this.row.index, payee);
  }
}

@Component({
  selector: 'app-line-amount-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmInput],
  template: `
    <input
      hlmInput
      type="number"
      step="0.01"
      min="0"
      class="w-24 text-right"
      [value]="row.amount"
      (input)="onInput($event)"
      aria-label="Transaction amount"
    />
  `,
})
export class LineAmountCell {
  private readonly ctx = injectFlexRenderContext<CellContext<LineItemEdit, unknown>>();
  protected get row(): LineItemEdit { return this.ctx.row.original; }
  private get meta(): StatementLineTableMeta { return this.ctx.table.options.meta as StatementLineTableMeta; }

  // Raw string, no Number() normalization — reformatting mid-type eats trailing '.'/'0'.
  protected onInput(event: Event): void {
    this.meta.patchRow(this.row.index, { amount: (event.target as HTMLInputElement).value });
  }
}
