import type { Page } from '@playwright/test';

// These rely on `serviceWorkers: 'block'` (set in playwright.config.ts): requests made by the
// app's service worker would not pass through `page.route`.

/**
 * Serves every GET matching `url` as an empty page of results, keeping the real response's
 * status, headers and paging fields.
 *
 * Tests share one database, so "nothing exists yet" can't be arranged by deleting everything —
 * that would pull the rug from under tests running alongside. Empty states are exercised by
 * emptying the list response instead.
 */
export async function stubEmptyList(page: Page, url: string | RegExp): Promise<void> {
  await page.route(url, async (route) => {
    if (route.request().method() !== 'GET') return route.fallback();
    const response = await route.fetch();
    const body = await response.json();
    await route.fulfill({ response, json: { ...body, items: [], totalCount: 0, totalPages: 0 } });
  });
}

/** Fails every request matching `url` and `method` with `status` and an RFC 7807 problem body. */
export async function stubFailure(
  page: Page,
  url: string | RegExp,
  { method = 'GET', status = 500 }: { method?: string; status?: number } = {},
): Promise<void> {
  await page.route(url, async (route) => {
    if (route.request().method() !== method) return route.fallback();
    await route.fulfill({
      status,
      contentType: 'application/problem+json',
      json: { type: 'about:blank', title: 'Injected failure', status },
    });
  });
}
