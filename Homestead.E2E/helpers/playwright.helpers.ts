import type { Locator, Page } from '@playwright/test';

export const MOBILE_BREAKPOINT_PX = 640; // Tailwind sm

/** Escapes `value` for literal use inside a `RegExp` (e.g. prefix-matching an accessible name). */
export function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

export function isMobileViewport(page: Page): boolean {
  const vp = page.viewportSize();
  return !!vp && vp.width < MOBILE_BREAKPOINT_PX;
}

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

/**
 * Sets a Spartan typed-entry date picker (`hlm-date-picker-input`) to an ISO
 * `yyyy-MM-dd` date.
 *
 * When the picker already holds a value it pre-fills its focus/edit text on focus
 * (`toDateString()`, e.g. "Mon Jul 13 2026"). Playwright's `fill()` races Angular's
 * re-render of that text and ends up *appending* rather than replacing it
 * ("Mon Jul 13 20262026-07-13"); the picker's blur-commit can't parse that string,
 * so it silently clears the bound date and the form's submit button stays disabled
 * forever. Clearing via the picker's own Clear button first empties the input so
 * `fill()` types into a blank field; the trailing `blur()` commits the typed date.
 *
 * `scope` should tightly enclose exactly one date picker (e.g. the open sheet /
 * dialog) so the "Clear date" button and label resolve unambiguously.
 */
export async function fillDatePicker(
  scope: Page | Locator,
  label: string | RegExp,
  isoDate: string,
  options?: { exact?: boolean },
): Promise<void> {
  const input = scope.getByLabel(label, options);
  // `isVisible()` below does not auto-wait, so ensure the picker has rendered
  // first — otherwise, if the picker hasn't painted yet, the Clear-date check
  // returns false, the clear is skipped, and `fill()` reintroduces the append
  // bug this helper exists to prevent.
  await input.waitFor({ state: 'visible' });
  const clearButton = scope.getByRole('button', { name: 'Clear date' }).first();
  if (await clearButton.isVisible().catch(() => false)) {
    await clearButton.click();
  }
  await input.fill(isoDate);
  await input.blur();
}
