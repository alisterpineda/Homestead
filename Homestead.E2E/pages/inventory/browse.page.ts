import { type Locator, type Page, expect } from '@playwright/test';
import { step } from '../../helpers/step';
import { escapeRegExp, expectOk } from '../../helpers/playwright.helpers';
import { pickLocation } from './inventory-pickers';

/** `/inventory/browse` — the place grid at root, and the drilled area / container views. */
export class BrowsePage {
  readonly needsAttentionToggle: Locator;

  constructor(readonly page: Page) {
    this.needsAttentionToggle = page.getByRole('button', { name: /Needs attention/ });
  }

  /** Opens Browse — or any route that lands there, such as the module root `/inventory`. */
  async goto(path = '/inventory/browse'): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/inventory/overview') &&
        resp.request().method() === 'GET' &&
        resp.status() === 200,
    );
    await this.page.goto(path);
    await responsePromise;
  }

  /** A place card (area or container) — a link whose name starts with the place's name. */
  placeCard(name: string): Locator {
    return this.page
      .locator('app-place-card')
      .getByRole('link', { name: new RegExp(`^${escapeRegExp(name)}\\b`) });
  }

  @step()
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

  /** The count shown on a place card (resolved semantics: everything that lives there, however deep). */
  placeCardCount(name: string): Locator {
    // The trailing figure; a low-stock badge's own number, when present, comes before it.
    return this.placeCard(name).getByText(/^\d+$/).last();
  }

  /** The amber "N low stock" badge on a place card. */
  placeCardLowStockBadge(name: string): Locator {
    return this.placeCard(name).getByLabel(/ low stock$/);
  }

  @step()
  async expandNeedsAttention(): Promise<void> {
    await this.needsAttentionToggle.click();
    await expect(this.needsAttentionToggle).toHaveAttribute('aria-expanded', 'true');
  }

  /** A tile in the needs-attention region, by its heading ("Low stock", "Lent out", "Warranty expiring"). */
  warningSection(label: string): Locator {
    return this.page.locator('app-inventory-warning-section').filter({
      has: this.page.getByRole('heading', { name: label, exact: true }),
    });
  }

  lowStockSection(): Locator {
    return this.warningSection('Low stock');
  }

  /** Opens an item's quick panel from the drilled place's contents; resolves to the panel. */
  @step()
  async openQuickPanel(name: string): Promise<Locator> {
    await this.looseItem(name).click();
    const panel = this.page.getByRole('dialog', { name });
    await expect(panel.getByRole('heading', { name })).toBeVisible();
    return panel;
  }

  /** Moves a durable from its open quick panel (Location → Move) and closes the panel. */
  @step()
  async moveFromQuickPanel(panel: Locator, locationName: string): Promise<void> {
    await panel.getByRole('button', { name: 'Move', exact: true }).click();
    await pickLocation(this.page, panel, 'Move to', locationName);
    await expect(panel.getByRole('button', { name: 'Move to', exact: true })).toContainText(locationName);

    const responsePromise = this.page.waitForResponse(
      (resp) =>
        /\/api\/v1\/inventory\/durables\/[0-9a-f-]+$/.test(resp.url()) && resp.request().method() === 'PATCH',
    );
    await panel.getByRole('button', { name: 'Move', exact: true }).click();
    await expectOk(await responsePromise);
    await expect(panel.locator('app-location-path')).toContainText(locationName);

    await this.page.keyboard.press('Escape');
    await expect(panel.getByRole('heading')).toBeHidden();
  }
}
