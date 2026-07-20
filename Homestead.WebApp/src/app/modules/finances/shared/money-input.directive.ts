import { Directive, ElementRef, inject, input, output } from '@angular/core';
import { formatAmountInput } from './format-currency';

/**
 * The money/quantity text input used across Finances. Replaces `type="number"` on every field that
 * holds an amount, a quantity, a per-unit price, or an FX rate.
 *
 * Why not `type="number"`:
 *
 * 1. **Spinners.** The webkit spin buttons sit *inside* the field and eat ~1.5rem of it, which is
 *    what made the statement-import amount column read as simultaneously cramped and too wide.
 *    Nobody increments a dollar figure one cent at a time.
 * 2. **It silently eats input.** A number input's `.value` is `''` for anything the browser can't
 *    parse, so a half-typed "1." or a stray character doesn't just fail validation — it reads back
 *    as empty, and the bound signal is cleared out from under the typist.
 *
 * `type="text"` + `inputmode="decimal"` keeps the mobile numeric keypad, drops the spinners, and
 * lets the raw string through untouched while the user is mid-edit.
 *
 * Formatting happens on **blur only**, never on input — reformatting mid-type would eat a trailing
 * "." or "0" the moment it was typed. Bind the decimal places to settle on:
 *
 *   <input hlmInput [appMoneyInput]="currencyInputPrecision(reportingCcy.value())"
 *          [value]="amount()" (amountInput)="amount.set($event)" />
 *
 * A null/absent precision means "leave the digits alone" — spinner removal without rounding, which
 * is what per-unit prices and FX rates want. See `currencyInputPrecision` for why that distinction
 * is load-bearing rather than a stylistic choice.
 *
 * `[value]` stays a plain one-way binding (exactly as the `type="number"` version was), so the
 * sheets' `seededLinkedSignal` re-seeds keep flowing into the DOM with no extra wiring. Blur writes
 * the normalized text to the element directly *and* emits it, so the binding and the DOM agree even
 * when the signal value doesn't change.
 */
@Directive({
  selector: 'input[appMoneyInput]',
  host: {
    type: 'text',
    inputmode: 'decimal',
    autocomplete: 'off',
    '(input)': 'onInput()',
    '(blur)': 'onBlur()',
  },
})
export class MoneyInputDirective {
  /**
   * Decimal places to settle on at blur; null leaves the typed digits untouched.
   *
   * The transform exists so the bare-attribute form (`<input appMoneyInput>` — the free-precision
   * price/rate fields) is first-class: a valueless attribute binds the empty string, which normalizes
   * to null here rather than being a type error at every such call site.
   */
  readonly precision = input<number | null, number | null | undefined | ''>(null, {
    alias: 'appMoneyInput',
    transform: (value) => (value === '' || value == null ? null : value),
  });

  /** The field's text, emitted on every keystroke raw and once more normalized at blur. */
  readonly amountInput = output<string>();

  private readonly el = inject<ElementRef<HTMLInputElement>>(ElementRef);

  protected onInput(): void {
    this.amountInput.emit(this.el.nativeElement.value);
  }

  protected onBlur(): void {
    const el = this.el.nativeElement;
    const formatted = formatAmountInput(el.value, this.precision());
    // Emit ONLY when formatting actually rewrote the text. If it didn't, the model already holds this
    // exact string (the last input event put it there, or nothing was typed at all) and re-emitting
    // would be a pure-noise write: the statement-import consumer rebuilds its whole rows array on every
    // patch, so an unconditional emit re-renders the entire table just for tabbing through a cell.
    if (formatted === el.value) return;
    el.value = formatted;
    this.amountInput.emit(formatted);
  }
}
