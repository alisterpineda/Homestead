import { Component, ChangeDetectionStrategy } from '@angular/core';
import { type CellContext, injectFlexRenderContext } from '@tanstack/angular-table';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSparkles, lucideCheck, lucideX } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmButtonGroup } from '@/shared/components/spartan/button-group';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmDatePicker, HlmDatePickerInput } from '@/shared/components/spartan/date-picker';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { toIsoDateString } from '@/shared/utils';
import { PayeePickerComponent, type PayeePickerValue } from '../shared/payee-picker/payee-picker.component';
import type { FlowDirection } from '@/api/generated/models/flow-direction';
import type { PayeeResponse } from '@/api/generated/models/payee-response';
import type { PayeeSuggestionSource } from '@/api/generated/models/payee-suggestion-source';
import type { AccountType } from '@/api/generated/models/account-type';

/** The payee's default category, surfaced as the read-only auto-category chip under the payee. */
export interface LineCategory {
  name: string;
  type: AccountType;
}

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
  // True once the reviewer explicitly dismisses (rejects) an AI ghost. A dismissed suggestion stops being
  // a ghost (drops from the suggestion count / import-gate) and the line imports as a normal unattributed
  // one, unless the reviewer then picks a payee manually.
  dismissed: boolean;
  // The APPLIED payee (what commits), or null when unattributed. A Learned tier-① hit is pre-filled here;
  // an AI ghost (tier ②/③) stays null until the reviewer accepts it. Held as a single stable object so the
  // per-row picker's [value] doesn't get a new identity every change detection.
  payee: PayeePickerValue | null;
  // The server's original suggestion (for ghost display + accept), independent of `payee`.
  suggestionSource: PayeeSuggestionSource;
  suggestedPayeeId: string | null;
  suggestedPayeeName: string | null;
  // The suggested existing-payee's default category (for the ghosted chip on an unaccepted suggestion).
  suggestedCategory: LineCategory | null;
  // True once the reviewer explicitly set this payee this session (pick / create / accept / override /
  // bulk-assign). Drives the commit `learn` flag. False for an untouched Learned pre-fill and for the
  // warning-dialog apply-but-don't-learn path.
  learn: boolean;
  // The APPLIED payee's default category chip (solid), null when unattributed or the payee has none.
  category: LineCategory | null;
}

// Table meta contract — the review page supplies the row-edit handlers, reached via ctx.table.options.meta.
// All handlers key by the STABLE server line index (row.original.index), never array position.
export interface StatementLineTableMeta {
  patchRow(index: number, patch: Partial<LineItemEdit>): void;
  setDirection(index: number, direction: FlowDirection): void;
  onPayeeChange(index: number, payee: PayeeResponse | null): void;
  acceptSuggestion(index: number): void;
  dismissSuggestion(index: number): void;
}

@Component({
  selector: 'app-line-date-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmDatePicker, HlmDatePickerInput],
  template: `
    <hlm-date-picker
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
  imports: [PayeePickerComponent, NgIcon, HlmIconImports, HlmButton, HlmButtonGroup],
  providers: [provideIcons({ lucideSparkles, lucideCheck, lucideX })],
  template: `
    <div class="flex min-w-[12rem] flex-col gap-1">
      <app-payee-picker
        [value]="row.payee"
        ariaLabel="Select payee for this line"
        (valueChange)="onChange($event)"
      />

      @if (isGhost) {
        <!-- Hint-only AI suggestion: NOT applied until accepted. -->
        <div class="flex items-center gap-1">
          <ng-icon hlm name="lucideSparkles" size="xs" class="shrink-0 text-violet-500" />
          <span class="min-w-0 truncate text-xs italic text-muted-foreground" [title]="row.suggestedPayeeName ?? ''">
            {{ row.suggestedPayeeName }}
          </span>
          <div hlmButtonGroup class="ml-auto shrink-0">
            <button hlmBtn size="sm" variant="outline" (click)="onAccept()">
              <ng-icon hlm name="lucideCheck" size="xs" class="mr-1" />
              {{ isNew ? 'Create & use' : 'Accept' }}
            </button>
            <button hlmBtn size="sm" variant="outline" (click)="onDismiss()">Dismiss</button>
          </div>
        </div>
        @if (row.suggestedCategory) {
          <span class="truncate text-xs italic text-muted-foreground/60">→ {{ row.suggestedCategory.name }}</span>
        }
      } @else if (isLearned) {
        <div class="flex items-center gap-1.5 text-xs text-muted-foreground">
          <span class="rounded bg-muted px-1 py-0.5 text-[10px] font-medium uppercase tracking-wide">Learned</span>
          @if (row.category) { <span class="min-w-0 truncate">→ {{ row.category.name }}</span> }
        </div>
      } @else if (row.category) {
        <span class="truncate text-xs text-muted-foreground">→ {{ row.category.name }}</span>
      }
    </div>
  `,
})
export class LinePayeeCell {
  private readonly ctx = injectFlexRenderContext<CellContext<LineItemEdit, unknown>>();
  protected get row(): LineItemEdit { return this.ctx.row.original; }
  private get meta(): StatementLineTableMeta { return this.ctx.table.options.meta as StatementLineTableMeta; }

  // An un-accepted, un-dismissed AI suggestion (tier ②/③): nothing applied yet, but a suggestion is waiting.
  protected get isGhost(): boolean {
    return this.row.payee === null
      && !this.row.dismissed
      && (this.row.suggestionSource === 'AiExistingPayee' || this.row.suggestionSource === 'AiNewPayee');
  }
  protected get isNew(): boolean {
    return this.row.suggestionSource === 'AiNewPayee';
  }
  // A deterministic learned tier-① hit, pre-applied and not yet overridden.
  protected get isLearned(): boolean {
    return this.row.payee !== null
      && !this.row.learn
      && this.row.suggestionSource === 'Learned'
      && this.row.payee.id === this.row.suggestedPayeeId;
  }

  protected onChange(payee: PayeeResponse | null): void {
    this.meta.onPayeeChange(this.row.index, payee);
  }

  protected onAccept(): void {
    this.meta.acceptSuggestion(this.row.index);
  }

  protected onDismiss(): void {
    this.meta.dismissSuggestion(this.row.index);
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
