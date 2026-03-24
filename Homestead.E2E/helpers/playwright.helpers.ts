import type { Locator } from '@playwright/test';

/**
 * Sets an input value and dispatches an input event to trigger Angular's
 * signal-based `(input)` binding. Use this instead of `fill()` for inputs
 * that use `[value]="signal()" (input)="signal.set(...)"` pattern.
 */
export async function fillSignalInput(locator: Locator, value: string): Promise<void> {
  await locator.click();
  await locator.evaluate((el: HTMLInputElement, val: string) => {
    el.value = val;
    el.dispatchEvent(new Event('input', { bubbles: true }));
  }, value);
}
