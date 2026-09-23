import { type Locator, type Page, expect } from '@playwright/test';
import { escapeRegExp } from '../../helpers/playwright.helpers';

/** `/inventory/browse` — the place grid at root, and the drilled area / container views. */
export class BrowsePage {
  readonly needsAttentionToggle: Locator;

  constructor(readonly page: Page) {
    this.needsAttentionToggle = page.getByRole('button', { name: /Needs attention/ });
  }

  async goto(): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/inventory/overview') &&
        resp.request().method() === 'GET' &&
        resp.status() === 200,
    );
    await this.page.goto('/inventory/browse');
    await responsePromise;
  }

  /** A place card (area or container) — a link whose name starts with the place's name. */
  placeCard(name: string): Locator {
    return this.page
      .locator('app-place-card')
      .getByRole('link', { name: new RegExp(`^${escapeRegExp(name)}\\b`) });
  }

  async drillInto(name: string): Promise<void> {
    await this.placeCard(name).click();
    await expect(this.placeHeading()).toHaveText(name);
  }

  /** The drilled place's title above its contents. */
  placeHeading(): Locator {
    return this.page.locator('app-place-browser h2').first();
  }

  breadcrumb(): Locator {
    return this.page.locator('nav[hlmBreadcrumb]').first();
  }

  /** An item listed among the drilled place's loose contents (table row or card). */
  looseItem(name: string): Locator {
    return this.page.locator('app-place-browser app-inventory-items-view').getByText(name, { exact: true });
  }

  async expandNeedsAttention(): Promise<void> {
    await this.needsAttentionToggle.click();
    await expect(this.lowStockSection()).toBeVisible();
  }

  lowStockSection(): Locator {
    return this.page.locator('app-inventory-warning-section').filter({
      has: this.page.getByRole('heading', { name: 'Low stock' }),
    });
  }
}
