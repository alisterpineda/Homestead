import { Component, ChangeDetectionStrategy, signal, type Signal } from '@angular/core';
import { type CellContext, injectFlexRenderContext } from '@tanstack/angular-table';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideSparkles, lucideCheck, lucidePlus, lucideX,
  lucideEllipsisVertical, lucideBan, lucideCircleCheck, lucideRotateCcw, lucideTrash2,
} from '@ng-icons/lucide';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmButtonGroup, HlmButtonGroupText } from '@/shared/components/spartan/button-group';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { HlmInput } from '@/shared/components/spartan/input';
import { MoneyInputDirective } from '../shared/money-input.directive';
import { HlmDatePicker, HlmDatePickerInput } from '@/shared/components/spartan/date-picker';
import { toIsoDateString } from '@/shared/utils';
import { PayeePickerComponent, type PayeePickerValue } from '../shared/payee-picker/payee-picker.component';
import {
  LineCategoryComboboxComponent,
  type LineCategorySelection,
} from '../shared/line-category-combobox/line-category-combobox.component';
import type { FlowDirection } from '@/api/generated/models/flow-direction';
import type { PayeeResponse } from '@/api/generated/models/payee-response';
import type { PayeeSuggestionSource } from '@/api/generated/models/payee-suggestion-source';
import type { AccountType } from '@/api/generated/models/account-type';
import type { CategoryResponse } from '@/api/generated/models/category-response';
import type { StatementLineTableFeatures } from './statement-import-review-page.component';

/**
 * A category as carried on a line — id (picker key + commit payload), plus name/type for display. Sourced
 * from a payee's default (`PayeeResponse.defaultCategory*`), a suggestion, or an inline pick/create.
 */
export interface LineCategory {
  id: string;
  name: string;
  type: AccountType;
}

// An immutable snapshot of a parsed row's reset-able fields, captured at seed time. "Reset to original"
// splats this back over the row (leaving the orthogonal `excluded` axis untouched). Manual rows have no
// snapshot (`original: undefined`) — there is nothing to reset to.
export interface LineItemOriginal {
  amount: string;
  description: string;
  direction: FlowDirection;
  date: string;
  payee: PayeePickerValue | null;
  suggestionSource: PayeeSuggestionSource;
  suggestedPayeeId: string | null;
  suggestedPayeeName: string | null;
  // The server-suggested/ghost payee's raw default category (used at accept time).
  suggestedPayeeDefaultCategory: LineCategory | null;
  // The applied payee's raw default category (feeds the live Suggested group).
  payeeDefaultCategory: LineCategory | null;
  category: LineCategory | null;
  // Always false at seed — a fresh parse has no reviewer category edit yet.
  categoryTouched: boolean;
  learn: boolean;
  dismissed: boolean;
}

// Client-side edit state for one row — the review page owns this until commit. `mappable` echoes the
// server's un-mappable flag (missing amount/direction); `amount`/`date` are raw strings for uncontrolled
// inputs (never reformatted while typing). Three orthogonal axes ride here alongside the edit fields:
// `excluded` (the include/exclude manifest axis), `manual` (user-added rows with no staged origin), and
// selection — which is NOT held here (it lives in the table's rowSelection map, purely transient).
export interface LineItemEdit {
  // Client row key: parsed rows keep their staged index (≥ 0); manual rows get a negative key from a
  // decrementing counter. The staged-vs-manual distinction at commit is `manual ? null : index`, so the
  // negative key never leaves the browser.
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
  // True for a user-added row the parser missed. Drives commit `index: null`, `learn: false`, the "Manual"
  // badge, and Delete-instead-of-Exclude. Manual rows are always `excluded: false` and carry no `original`.
  manual: boolean;
  // The include/exclude axis (default false = included). An excluded row stays in the manifest greyed +
  // struck-through and does NOT commit. Recoverable via the ⋮ menu. Always false for manual rows.
  excluded: boolean;
  // Immutable snapshot of the reset-able fields at seed time; `undefined` for manual rows. Powers "Reset
  // to original" and the isDirty check.
  original?: LineItemOriginal;
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
  // The server-suggested/ghost payee's raw default category — used at accept time to pre-fill `category`.
  suggestedPayeeDefaultCategory: LineCategory | null;
  // The APPLIED payee's raw (un-filtered) default category — feeds the combobox's live Suggested group.
  // Held separately from `payee` (narrowed to {id,name}) so Suggested survives a direction flip.
  payeeDefaultCategory: LineCategory | null;
  // True once the reviewer explicitly set this payee this session (pick / create / accept / override /
  // bulk-assign). Drives the commit `learn` flag. False for an untouched Learned pre-fill and for the
  // warning-dialog apply-but-don't-learn path.
  learn: boolean;
  // The APPLIED category (what commits), null when Uncategorised. Tracks the payee default while
  // pristine; once the reviewer edits it (categoryTouched), it's sticky.
  category: LineCategory | null;
  // True once the reviewer explicitly edited the category (inline pick, explicit Uncategorised, bulk set).
  // Auto-derive from the payee fires only while this is false (the pristine/sticky gate).
  categoryTouched: boolean;
}

// Table meta contract — the review page supplies the row-edit handlers, reached via ctx.table.options.meta.
// All handlers key by the STABLE client row key (row.original.index), never array position.
export interface StatementLineTableMeta {
  patchRow(index: number, patch: Partial<LineItemEdit>): void;
  // A category pick subsumes direction — sets direction + category + marks categoryTouched.
  setCategory(index: number, sel: LineCategorySelection): void;
  // An inline-created category — merged into `categories` so it's reusable on every other row at once.
  onCategoryCreated(category: CategoryResponse): void;
  // All categories (both directions), read reactively so a late fetch re-renders the cell's combobox.
  categories: Signal<CategoryResponse[]>;
  onPayeeChange(index: number, payee: PayeeResponse | null): void;
  acceptSuggestion(index: number): void;
  dismissSuggestion(index: number): void;
  // Manifest axis: toggle a parsed row's included/excluded state; reset its edits; delete a manual row.
  toggleExcluded(index: number): void;
  resetRow(index: number): void;
  deleteManualRow(index: number): void;
  // Whether a parsed row differs from its captured original (gates the "Reset to original" action).
  // Takes the row directly — the cell already holds it, so no re-lookup is needed.
  isDirty(row: LineItemEdit): boolean;
  // Decimal places every amount cell settles on at blur, from the account's reporting currency. Read
  // reactively so a late currency fetch re-renders the cells (null until it lands = no rounding).
  amountPrecision: Signal<number | null>;
}

@Component({
  selector: 'app-line-date-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmDatePicker, HlmDatePickerInput],
  template: `
    <div class="flex flex-col gap-1">
      @if (row.manual) {
        <span class="w-fit rounded bg-muted px-1 py-0.5 text-[10px] font-medium uppercase tracking-wide text-muted-foreground">
          Manual
        </span>
      }
      <hlm-date-picker
        captionLayout="dropdown"
        [date]="row.dateObj"
        (dateChange)="onDateChange($event)"
      >
        <hlm-date-picker-input [inputId]="inputId" placeholder="Select date" />
      </hlm-date-picker>
    </div>
  `,
})
export class LineDateCell {
  private readonly ctx = injectFlexRenderContext<CellContext<StatementLineTableFeatures, LineItemEdit, unknown>>();
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
  private readonly ctx = injectFlexRenderContext<CellContext<StatementLineTableFeatures, LineItemEdit, unknown>>();
  protected get row(): LineItemEdit { return this.ctx.row.original; }
  private get meta(): StatementLineTableMeta { return this.ctx.table.options.meta as StatementLineTableMeta; }

  protected onInput(event: Event): void {
    this.meta.patchRow(this.row.index, { description: (event.target as HTMLInputElement).value });
  }
}

// The Category cell — one searchable combobox that subsumes the old Expense/Income select. Picking a typed
// category sets the line's direction too (correct-by-construction); the two Uncategorised entries carry a
// direction with no category. Bound one-way to the row; edits flow back through meta.setCategory.
@Component({
  selector: 'app-line-category-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LineCategoryComboboxComponent],
  template: `
    <app-line-category-combobox
      [direction]="row.direction"
      [category]="row.category"
      [payeeDefaultCategory]="row.payeeDefaultCategory"
      [categories]="meta.categories()"
      ariaLabel="Select category for this line"
      (categoryChange)="onChange($event)"
      (categoryCreated)="meta.onCategoryCreated($event)"
    />
  `,
})
export class LineCategoryCell {
  private readonly ctx = injectFlexRenderContext<CellContext<StatementLineTableFeatures, LineItemEdit, unknown>>();
  protected get row(): LineItemEdit { return this.ctx.row.original; }
  protected get meta(): StatementLineTableMeta { return this.ctx.table.options.meta as StatementLineTableMeta; }

  protected onChange(sel: LineCategorySelection): void {
    this.meta.setCategory(this.row.index, sel);
  }
}

@Component({
  selector: 'app-line-payee-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PayeePickerComponent, NgIcon, HlmButton, HlmButtonGroup, HlmButtonGroupText],
  providers: [provideIcons({ lucideSparkles, lucideCheck, lucidePlus, lucideX })],
  template: `
    <div class="flex w-full flex-col gap-1">
      @if (isGhost && !overriding()) {
        <!-- Un-accepted AI suggestion rendered as ONE inline control so it sits level with the other cells
             in the row: [✨ suggested name (click to change)] · [accept] · [dismiss]. The name segment is a
             button — clicking it swaps in the live picker (seeded with the name) to override. -->
        <div hlmButtonGroup class="w-full">
          <button
            type="button"
            hlmButtonGroupText
            class="min-w-0 flex-1 cursor-pointer justify-start gap-1.5 hover:bg-accent"
            [attr.aria-label]="'Change payee, suggested: ' + (row.suggestedPayeeName ?? '')"
            [title]="'Change payee (suggested: ' + (row.suggestedPayeeName ?? '') + ')'"
            (click)="startOverride()"
          >
            <ng-icon name="lucideSparkles" class="text-[length:--spacing(3)] shrink-0 text-violet-500" />
            <span class="min-w-0 truncate font-normal italic text-muted-foreground">{{ row.suggestedPayeeName }}</span>
          </button>
          <button
            hlmBtn
            size="icon"
            variant="outline"
            [attr.aria-label]="acceptLabel"
            [title]="acceptLabel"
            (click)="onAccept()"
          >
            <ng-icon [name]="isNew ? 'lucidePlus' : 'lucideCheck'" class="text-[length:--spacing(4)]" />
          </button>
          <button
            hlmBtn
            size="icon"
            variant="outline"
            aria-label="Dismiss suggestion"
            title="Dismiss suggestion"
            (click)="onDismiss()"
          >
            <ng-icon name="lucideX" class="text-[length:--spacing(4)]" />
          </button>
        </div>
      } @else {
        <app-payee-picker
          [value]="row.payee"
          ariaLabel="Select payee for this line"
          [autoOpen]="overriding()"
          [initialQuery]="row.suggestedPayeeName"
          (valueChange)="onChange($event)"
          (closed)="onPickerClosed()"
        />

        @if (isLearned) {
          <div class="flex items-center gap-1.5 text-xs text-muted-foreground">
            <span class="rounded bg-muted px-1 py-0.5 text-[10px] font-medium uppercase tracking-wide">Learned</span>
          </div>
        }
      }
    </div>
  `,
})
export class LinePayeeCell {
  private readonly ctx = injectFlexRenderContext<CellContext<StatementLineTableFeatures, LineItemEdit, unknown>>();
  protected get row(): LineItemEdit { return this.ctx.row.original; }
  private get meta(): StatementLineTableMeta { return this.ctx.table.options.meta as StatementLineTableMeta; }

  // The reviewer clicked the suggestion name to pick a different payee: swap the segment for the live picker.
  // Cleared again if they back out without committing (onPickerClosed).
  protected readonly overriding = signal(false);

  // An un-accepted, un-dismissed AI suggestion (tier ②/③): nothing applied yet, but a suggestion is waiting.
  protected get isGhost(): boolean {
    return this.row.payee === null
      && !this.row.dismissed
      && this.isAiSource;
  }
  protected get isNew(): boolean {
    return this.row.suggestionSource === 'AiNewPayee';
  }
  private get isAiSource(): boolean {
    return this.row.suggestionSource === 'AiExistingPayee' || this.row.suggestionSource === 'AiNewPayee';
  }
  // A deterministic learned tier-① hit, pre-applied and not yet overridden.
  protected get isLearned(): boolean {
    return this.row.payee !== null
      && !this.row.learn
      && this.row.suggestionSource === 'Learned'
      && this.row.payee.id === this.row.suggestedPayeeId;
  }
  protected get acceptLabel(): string {
    return this.isNew ? 'Create & use suggested payee' : 'Accept suggested payee';
  }

  // Enter override: reveal the picker, auto-opened and seeded with the suggested name (see the picker's
  // [autoOpen]/[initialQuery]).
  protected startOverride(): void {
    this.overriding.set(true);
  }

  protected onChange(payee: PayeeResponse | null): void {
    this.overriding.set(false);
    if (payee) {
      this.meta.onPayeeChange(this.row.index, payee);
      return;
    }
    if (this.isAiSource) {
      // Clearing an AI-suggested line = reviewed → no payee. Record it as dismissed rather than leaving it
      // null, which (for an AI source) would re-surface it as an un-reviewed ghost that gates the import.
      // Drop the live Suggested source (no payee → no suggestion); only re-derive the category (to null)
      // while pristine — a sticky reviewer override survives the payee clear.
      this.meta.patchRow(this.row.index, {
        payee: null,
        payeeDefaultCategory: null,
        learn: false,
        dismissed: true,
        ...(this.row.categoryTouched ? {} : { category: null }),
      });
      return;
    }
    this.meta.onPayeeChange(this.row.index, null);
  }

  // The picker closed without committing anything → the reviewer backed out of an override, so restore the
  // suggestion segment (a value pick / clear already flipped `overriding` off in onChange first).
  protected onPickerClosed(): void {
    if (this.overriding() && this.row.payee === null && !this.row.dismissed) {
      this.overriding.set(false);
    }
  }

  protected onAccept(): void {
    this.meta.acceptSuggestion(this.row.index);
  }

  protected onDismiss(): void {
    this.meta.dismissSuggestion(this.row.index);
  }
}

// Fills the cell — the amount column's width (and its floor) is set by its column def, so a resize
// carries through to the field instead of leaving it pinned at a fixed px width.
@Component({
  selector: 'app-line-amount-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmInput, MoneyInputDirective],
  template: `
    <input
      hlmInput
      [appMoneyInput]="meta.amountPrecision()"
      class="w-full text-right"
      [class.line-through]="row.excluded"
      [value]="row.amount"
      (amountInput)="onAmountInput($event)"
      aria-label="Transaction amount"
    />
  `,
})
export class LineAmountCell {
  private readonly ctx = injectFlexRenderContext<CellContext<StatementLineTableFeatures, LineItemEdit, unknown>>();
  protected get row(): LineItemEdit { return this.ctx.row.original; }
  protected get meta(): StatementLineTableMeta { return this.ctx.table.options.meta as StatementLineTableMeta; }

  // Raw string while typing; appMoneyInput settles it to the account currency's precision at blur.
  protected onAmountInput(value: string): void {
    this.meta.patchRow(this.row.index, { amount: value });
  }
}

// Trailing ⋮ overflow menu — the per-row manifest controls, orthogonal to selection. A parsed row toggles
// Exclude/Include and (when edited) offers Reset to original; a manual row offers Delete instead.
@Component({
  selector: 'app-line-actions-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmButton, HlmDropdownMenuImports],
  providers: [provideIcons({ lucideEllipsisVertical, lucideBan, lucideCircleCheck, lucideRotateCcw, lucideTrash2 })],
  template: `
    <button
      hlmBtn
      variant="ghost"
      size="icon-sm"
      type="button"
      [hlmDropdownMenuTrigger]="menu"
      aria-label="Line actions"
    >
      <ng-icon name="lucideEllipsisVertical" class="text-[length:--spacing(4)]" />
    </button>
    <ng-template #menu>
      <hlm-dropdown-menu class="w-48">
        <hlm-dropdown-menu-group>
          @if (row.manual) {
            <button hlmDropdownMenuItem variant="destructive" (triggered)="onDelete()">
              <ng-icon name="lucideTrash2" class="text-[length:--spacing(4)]" />
              Delete row
            </button>
          } @else {
            <button hlmDropdownMenuItem (triggered)="onToggleExcluded()">
              @if (row.excluded) {
                <ng-icon name="lucideCircleCheck" class="text-[length:--spacing(4)]" />
                Include
              } @else {
                <ng-icon name="lucideBan" class="text-[length:--spacing(4)]" />
                Exclude
              }
            </button>
            @if (isDirty) {
              <button hlmDropdownMenuItem (triggered)="onReset()">
                <ng-icon name="lucideRotateCcw" class="text-[length:--spacing(4)]" />
                Reset to original
              </button>
            }
          }
        </hlm-dropdown-menu-group>
      </hlm-dropdown-menu>
    </ng-template>
  `,
})
export class LineActionsCell {
  private readonly ctx = injectFlexRenderContext<CellContext<StatementLineTableFeatures, LineItemEdit, unknown>>();
  protected get row(): LineItemEdit { return this.ctx.row.original; }
  private get meta(): StatementLineTableMeta { return this.ctx.table.options.meta as StatementLineTableMeta; }

  protected get isDirty(): boolean { return this.meta.isDirty(this.row); }

  protected onToggleExcluded(): void { this.meta.toggleExcluded(this.row.index); }
  protected onReset(): void { this.meta.resetRow(this.row.index); }
  protected onDelete(): void { this.meta.deleteManualRow(this.row.index); }
}
