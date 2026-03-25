import type { Locator } from '@playwright/test';

/**
 * Types into an Angular signal-bound input using pressSequential to ensure
 * each character triggers Angular change detection and any debounced observables.
 * Use this instead of `fill()` for inputs that use
 * `[value]="signal()" (input)="signal.set(...)"` pattern.
 */
export async function fillSignalInput(locator: Locator, value: string): Promise<void> {
  await locator.click();
  await locator.fill('');
  await locator.pressSequentially(value, { delay: 30 });
}
