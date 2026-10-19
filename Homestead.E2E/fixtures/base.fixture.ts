import { randomBytes } from 'node:crypto';
import AxeBuilder from '@axe-core/playwright';
import { test as base, type APIRequestContext } from '@playwright/test';
import { AUTH_STATE } from '../e2e.config';

type BaseFixtures = {
  /**
   * A short random id for this test. Suffix every name a test creates with it: tests run in
   * parallel against one shared database (and again on every device project), so data is
   * isolated by name rather than by wiping the database between tests.
   */
  uid: string;
  /** An API client signed in as the admin, whatever `storageState` the page itself uses. */
  adminRequest: APIRequestContext;
  /** An axe-core scanner for the current page, preconfigured for WCAG 2.1 A and AA. */
  makeAxeBuilder: () => AxeBuilder;
  /**
   * Option: start every tab with finance values revealed, as a user who just un-hid them would.
   * Default true, since specs assert on amounts; a spec exercising the privacy mask itself sets it
   * false to get the app's masked-by-default behaviour.
   */
  revealFinances: boolean;
  /** Automatic: applies `revealFinances` (see below). */
  revealFinanceValues: void;
  /** Automatic: lets clicks pass through toasts (see below). */
  clickThroughToasts: void;
};

export const test = base.extend<BaseFixtures>({
  uid: async ({}, use) => {
    await use(randomBytes(3).toString('hex'));
  },
  adminRequest: async ({ playwright, baseURL }, use) => {
    const context = await playwright.request.newContext({ baseURL, storageState: AUTH_STATE.admin });
    await use(context);
    await context.dispose();
  },
  revealFinances: [true, { option: true }],
  // Finances masks values by default and re-masks once its revealed-at anchor is 5 min old, so the
  // reveal is stamped fresh when each tab first loads the app — never carried in storageState,
  // where it would go stale partway through a long run. The sessionStorage marker makes it once
  // per tab, so a reload (or the test toggling the mask) keeps the app's real behaviour.
  revealFinanceValues: [
    async ({ context, baseURL, revealFinances }, use) => {
      if (revealFinances) {
        await context.addInitScript((appOrigin) => {
          if (location.origin !== appOrigin || sessionStorage.getItem('e2e-finances-revealed')) return;
          sessionStorage.setItem('e2e-finances-revealed', 'true');
          localStorage.setItem('homestead-finances-privacy-masked', 'false');
          localStorage.setItem('homestead-finances-privacy-revealed-at', JSON.stringify(Date.now()));
        }, new URL(baseURL!).origin);
      }
      await use();
    },
    { auto: true },
  ],
  // Every test signs in as the same admin, so each page also receives the "processed
  // successfully" toasts for every other test's uploads. On a phone that stack covers the bottom of
  // the screen and swallows clicks on whatever sits under it — noise no real user sees. Toasts stay
  // visible (and assertable); they just stop intercepting pointer events.
  clickThroughToasts: [
    async ({ context }, use) => {
      await context.addInitScript(() => {
        document.addEventListener('DOMContentLoaded', () => {
          const style = document.createElement('style');
          style.textContent = '[data-sonner-toaster], [data-sonner-toast] { pointer-events: none !important; }';
          document.head.append(style);
        });
      });
      await use();
    },
    { auto: true },
  ],
  makeAxeBuilder: async ({ page }, use) => {
    await use(() =>
      new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
        // Toasts (e.g. "upload finished" from a seeded document's job) come and go mid-scan, and
        // ngx-sonner's list is briefly invalid while one animates in. They aren't page content.
        .exclude('hlm-toaster'),
    );
  },
});

export { expect } from '@playwright/test';
