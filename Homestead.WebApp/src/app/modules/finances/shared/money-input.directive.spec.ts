import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MoneyInputDirective } from './money-input.directive';

@Component({
  imports: [MoneyInputDirective],
  template: `
    <input
      [appMoneyInput]="precision()"
      [value]="amount()"
      (amountInput)="onAmountInput($event)"
    />
  `,
})
class HostComponent {
  readonly precision = signal<number | null>(2);
  readonly amount = signal('');
  /** Counts writes reaching the consumer — each one costs a rows-array rebuild in statement import. */
  writes = 0;

  onAmountInput(value: string): void {
    this.writes++;
    this.amount.set(value);
  }
}

@Component({
  imports: [MoneyInputDirective],
  template: `<input appMoneyInput [value]="amount()" (amountInput)="amount.set($event)" />`,
})
class BareHostComponent {
  readonly amount = signal('1.3745');
}

function setup() {
  const fixture = TestBed.createComponent(HostComponent);
  fixture.detectChanges();
  const el = fixture.nativeElement.querySelector('input') as HTMLInputElement;
  const type = (text: string) => {
    el.value = text;
    el.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  };
  const blur = () => {
    el.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
  };
  return { fixture, el, type, blur, host: fixture.componentInstance };
}

describe('MoneyInputDirective', () => {
  it('renders as a text input with a decimal keypad, not a spinner-bearing number input', () => {
    const { el } = setup();
    // type="number" is what draws the webkit spin buttons this directive exists to remove.
    expect(el.getAttribute('type')).toBe('text');
    expect(el.getAttribute('inputmode')).toBe('decimal');
  });

  it('passes keystrokes through untouched — no reformatting mid-type', () => {
    const { type, host, el } = setup();

    // The half-typed states a number input would have reported back as '' instead.
    type('1');
    expect(host.amount()).toBe('1');
    type('1.');
    expect(host.amount()).toBe('1.');
    expect(el.value).toBe('1.');
    type('1.2');
    expect(host.amount()).toBe('1.2');
    // A trailing zero survives, so "1.20" can actually be typed left-to-right.
    type('1.20');
    expect(host.amount()).toBe('1.20');
  });

  it('settles to the bound precision at blur, updating both the model and the DOM', () => {
    const { type, blur, host, el } = setup();

    type('1.2');
    blur();

    expect(host.amount()).toBe('1.20');
    expect(el.value).toBe('1.20');
  });

  it('settles a zero-precision currency with no decimal point', () => {
    const { type, blur, host, fixture } = setup();
    fixture.componentInstance.precision.set(0);
    fixture.detectChanges();

    type('1234.5');
    blur();

    expect(host.amount()).toBe('1235');
  });

  it('leaves an empty field empty at blur', () => {
    const { type, blur, host, el } = setup();

    type('');
    blur();

    expect(host.amount()).toBe('');
    expect(el.value).toBe('');
  });

  it('leaves unparseable text on screen at blur instead of blanking or zeroing it', () => {
    const { type, blur, host, el } = setup();

    type('12x');
    blur();

    expect(host.amount()).toBe('12x');
    expect(el.value).toBe('12x');
  });

  it('does not round when no precision is bound — the bare-attribute price/rate form', () => {
    const fixture = TestBed.createComponent(BareHostComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement.querySelector('input') as HTMLInputElement;

    el.value = '1.3745';
    el.dispatchEvent(new Event('input'));
    el.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // A valueless attribute binds '' — it must read as "free precision", not as precision 0.
    expect(fixture.componentInstance.amount()).toBe('1.3745');
    expect(el.value).toBe('1.3745');
  });

  it('does not emit at blur when formatting changed nothing', () => {
    const { el, blur, host, fixture } = setup();
    host.amount.set('1.20');
    fixture.detectChanges();
    host.writes = 0;

    // Tab into and straight back out of an already-well-formed cell. No write should escape, or the
    // statement-import table rebuilds its entire rows array on a pure focus traversal.
    el.dispatchEvent(new Event('focus'));
    blur();

    expect(host.writes).toBe(0);
    expect(host.amount()).toBe('1.20');
    expect(el.value).toBe('1.20');
  });

  it('emits exactly once at blur when formatting did rewrite the text', () => {
    const { type, blur, host } = setup();

    type('1.2');
    host.writes = 0;
    blur();

    expect(host.writes).toBe(1);
    expect(host.amount()).toBe('1.20');
  });

  it('reflects an external re-seed of the bound value (sheet reopened on another record)', () => {
    const { fixture, el, host } = setup();

    host.amount.set('42.00');
    fixture.detectChanges();

    expect(el.value).toBe('42.00');
  });
});
