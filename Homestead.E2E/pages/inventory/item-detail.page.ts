import { type Locator, type Page, expect } from '@playwright/test';
import { CreateItemDialog } from './create-item-dialog';
import { pickLocation } from './inventory-pickers';

/**
 * `/inventory/items/:id` — one route for both kinds; the page renders the durable or consumable
 * detail view depending on what the record turns out to be.
 */
export class ItemDetailPage {
  readonly createItemDialog: CreateItemDialog;

  constructor(readonly page: Page) {
    this.createItemDialog = new CreateItemDialog(page);
  }

  async gotoById(id: string): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) =>
        resp.url().includes(`/api/v1/inventory/items/${id}`) &&
        resp.request().method() === 'GET' &&
        resp.status() === 200,
    );
    await this.page.goto(`/inventory/items/${id}`);
    await responsePromise;
    await expect(this.nameHeading()).toBeVisible();
  }

  /** The durable / consumable detail view — scopes out the app shell's own page-title `<h1>`. */
  private detailView(): Locator {
    return this.page.locator('app-durable-detail-page, app-consumable-detail-page');
  }

  nameHeading(): Locator {
    return this.detailView().getByRole('heading', { level: 1 }).first();
  }

  // --- Durable ---

  /** The headline "where does it live" path under a durable's name. */
  locationPath(): Locator {
    return this.page.locator('p:has(> app-location-path)').first();
  }

  contentsSection(): Locator {
    return this.page.locator('section').filter({
      has: this.page.getByRole('heading', { name: 'Contents', exact: true }),
    });
  }

  async storeItemsInside(): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) =>
        /\/api\/v1\/inventory\/durables\/[0-9a-f-]+$/.test(resp.url()) &&
        resp.request().method() === 'PATCH' &&
        resp.status() === 200,
    );
    await this.page.getByRole('button', { name: 'Store items inside' }).click();
    await responsePromise;
    await expect(this.contentsSection()).toBeVisible();
  }

  /** Opens the Contents section's "Add item" dialog, which pre-fills this container as the location. */
  async openAddItemToContainer(): Promise<CreateItemDialog> {
    await this.createItemDialog.open(this.contentsSection().getByRole('button', { name: 'Add item' }));
    return this.createItemDialog;
  }

  async moveDurable(locationName: string): Promise<void> {
    await this.page.getByRole('button', { name: 'Edit', exact: true }).click();
    const sheet = this.page.locator('hlm-sheet-content').filter({
      has: this.page.getByRole('heading', { name: 'Edit durable' }),
    });
    await expect(sheet).toBeVisible();

    await pickLocation(this.page, sheet, 'Location', locationName);
    await expect(sheet.getByRole('button', { name: 'Location', exact: true })).toContainText(locationName);

    const responsePromise = this.page.waitForResponse(
      (resp) =>
        /\/api\/v1\/inventory\/durables\/[0-9a-f-]+$/.test(resp.url()) &&
        resp.request().method() === 'PATCH' &&
        resp.status() === 200,
    );
    await sheet.getByRole('button', { name: 'Save', exact: true }).click();
    await responsePromise;
    await expect(sheet).toBeHidden();
  }

  // --- Consumable ---

  /** The big "N <unit> on hand" figure. */
  onHandText(): Locator {
    return this.detailView().locator('p').filter({ hasText: /\bon hand\s*$/ }).first();
  }

  /** The "Low" pill beside a consumable's name, shown at or below its restock threshold. */
  lowBadge(): Locator {
    // The innermost element holding the name heading is the header row the pill sits in.
    return this.detailView()
      .locator('div')
      .filter({ has: this.page.getByRole('heading', { level: 1 }) })
      .last()
      .getByText('Low', { exact: true });
  }

  stockRow(locationName: string): Locator {
    return this.page.locator('section').filter({
      has: this.page.getByRole('heading', { name: 'By location' }),
    }).locator('tbody tr').filter({ hasText: locationName });
  }

  movementRows(): Locator {
    return this.page.locator('section').filter({
      has: this.page.getByRole('heading', { name: 'Movements' }),
    }).locator('tbody tr');
  }

  async useStock(locationName: string, quantity: string): Promise<void> {
    await this.stockRow(locationName).getByRole('button', { name: 'Use', exact: true }).click();
    const sheet = this.page.locator('hlm-sheet-content').filter({
      has: this.page.getByRole('heading', { name: 'Use stock' }),
    });
    await expect(sheet).toBeVisible();
    await sheet.getByLabel('Quantity', { exact: true }).fill(quantity);

    const responsePromise = this.page.waitForResponse(
      (resp) =>
        /\/api\/v1\/inventory\/consumables\/[0-9a-f-]+\/consume$/.test(resp.url()) &&
        resp.request().method() === 'POST' &&
        resp.status() === 200,
    );
    await sheet.getByRole('button', { name: 'Use', exact: true }).click();
    await responsePromise;
    await expect(sheet).toBeHidden();
  }

  /** Carves one unit out of the stock at `locationName`; resolves to the new durable's id. */
  async trackOne(locationName: string, durableName: string): Promise<string> {
    await this.stockRow(locationName).getByRole('button', { name: 'Track one', exact: true }).click();
    const sheet = this.page.locator('hlm-sheet-content').filter({
      has: this.page.getByRole('heading', { name: 'Track one individually' }),
    });
    await expect(sheet).toBeVisible();
    await sheet.getByLabel('Name', { exact: true }).fill(durableName);

    const responsePromise = this.page.waitForResponse(
      (resp) =>
        /\/api\/v1\/inventory\/consumables\/[0-9a-f-]+\/track-one$/.test(resp.url()) &&
        resp.request().method() === 'POST' &&
        resp.ok(),
    );
    await sheet.getByRole('button', { name: 'Track individually' }).click();
    const response = await responsePromise;
    await expect(sheet).toBeHidden();
    const body = await response.json();
    return body.item.id as string;
  }
}
