import { type Locator, type Page, type Response, expect } from '@playwright/test';
import { step } from '../../helpers/step';
import { expectOk } from '../../helpers/playwright.helpers';
import { CreateItemDialog } from './create-item-dialog';
import { brandPickerPopover, pickLocation } from './inventory-pickers';
import { ItemActivityFeed } from './item-activity-feed';
import { ItemWarrantiesSection } from './item-warranties-section';

const DURABLE_PATCH = /\/api\/v1\/inventory\/durables\/[0-9a-f-]+$/;

/**
 * `/inventory/items/:id` — one route for both kinds; the page renders the durable or consumable
 * detail view depending on what the record turns out to be.
 */
export class ItemDetailPage {
  readonly createItemDialog: CreateItemDialog;
  readonly activity: ItemActivityFeed;
  readonly warranties: ItemWarrantiesSection;
  /** The confirm dialogs behind the ⋮ menu's Lend… / Archive / Delete. */
  readonly confirmDialog: Locator;

  constructor(readonly page: Page) {
    this.createItemDialog = new CreateItemDialog(page);
    this.activity = new ItemActivityFeed(page);
    this.warranties = new ItemWarrantiesSection(page);
    this.confirmDialog = page.getByRole('alertdialog');
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

  /** The status pill beside the name plus its note ("Lent" · "to Dave"); empty while Active. */
  statusBadge(): Locator {
    return this.detailView().locator('app-item-status-badge').first();
  }

  /** The Details card's value for `term` (e.g. "Brand"). */
  detail(term: string): Locator {
    return this.detailView().locator(`dt:text-is("${term}") + dd`);
  }

  // --- Durable ---

  /** Opens the header's ⋮ menu (Lend… / Mark returned, Archive / Restore, Delete). */
  @step()
  async openActionsMenu(): Promise<Locator> {
    await this.page.getByRole('button', { name: 'Durable actions' }).click();
    const menu = this.page.getByRole('menu');
    await expect(menu).toBeVisible();
    return menu;
  }

  /** ⋮ → Lend… and fills in the borrower, leaving the confirm dialog open. */
  @step()
  async startLend(borrower: string): Promise<void> {
    const menu = await this.openActionsMenu();
    await menu.getByRole('menuitem', { name: /^Lend/ }).click();
    await expect(this.confirmDialog.getByRole('heading', { name: /^Lend / })).toBeVisible();
    await this.confirmDialog.getByLabel('Lent to').fill(borrower);
  }

  /** Confirms the open Lend dialog; resolves to the PATCH response whatever its status. */
  @step()
  async submitLend(): Promise<Response> {
    const responsePromise = this.page.waitForResponse(
      (resp) => DURABLE_PATCH.test(resp.url()) && resp.request().method() === 'PATCH',
    );
    await this.confirmDialog.getByRole('button', { name: 'Lend', exact: true }).click();
    return responsePromise;
  }

  @step()
  async lend(borrower: string): Promise<void> {
    await this.startLend(borrower);
    await expectOk(await this.submitLend());
    await expect(this.confirmDialog.getByRole('heading')).toBeHidden();
  }

  @step()
  async markReturned(): Promise<void> {
    const menu = await this.openActionsMenu();
    const responsePromise = this.page.waitForResponse(
      (resp) => DURABLE_PATCH.test(resp.url()) && resp.request().method() === 'PATCH',
    );
    await menu.getByRole('menuitem', { name: 'Mark returned' }).click();
    await expectOk(await responsePromise);
  }

  @step()
  async archive(note?: string): Promise<void> {
    const menu = await this.openActionsMenu();
    await menu.getByRole('menuitem', { name: 'Archive' }).click();
    const heading = this.confirmDialog.getByRole('heading', { name: /^Archive / });
    await expect(heading).toBeVisible();
    if (note !== undefined) {
      await this.confirmDialog.getByLabel('Note (optional)').fill(note);
    }
    const responsePromise = this.page.waitForResponse(
      (resp) => DURABLE_PATCH.test(resp.url()) && resp.request().method() === 'PATCH',
    );
    await this.confirmDialog.getByRole('button', { name: 'Archive', exact: true }).click();
    await expectOk(await responsePromise);
    await expect(heading).toBeHidden();
  }

  /** Edit → brand picker → "Create \"<name>\"" → Save: attributes the durable to a brand new brand. */
  @step()
  async createAndSetBrand(brandName: string): Promise<void> {
    await this.page.getByRole('button', { name: 'Edit', exact: true }).click();
    const sheet = this.page.getByRole('dialog', { name: 'Edit durable' });
    const heading = sheet.getByRole('heading', { name: 'Edit durable' });
    await expect(heading).toBeVisible();

    await sheet.getByRole('button', { name: 'Brand', exact: true }).click();
    const popover = brandPickerPopover(this.page);
    await popover.getByRole('textbox', { name: 'Search brands' }).fill(brandName);
    const createResponse = this.page.waitForResponse(
      (resp) => resp.url().endsWith('/api/v1/inventory/brands') && resp.request().method() === 'POST',
    );
    await popover.getByRole('button', { name: `Create "${brandName}"` }).click();
    await expectOk(await createResponse);
    await expect(popover).toBeHidden();
    await expect(sheet.getByRole('button', { name: 'Brand', exact: true })).toContainText(brandName);

    const saveResponse = this.page.waitForResponse(
      (resp) => DURABLE_PATCH.test(resp.url()) && resp.request().method() === 'PATCH',
    );
    await sheet.getByRole('button', { name: 'Save', exact: true }).click();
    await expectOk(await saveResponse);
    await expect(heading).toBeHidden();
  }

  /** The headline "where does it live" path under a durable's name. */
  locationPath(): Locator {
    return this.page.locator('p:has(> app-location-path)').first();
  }

  contentsSection(): Locator {
    return this.page.locator('section').filter({
      has: this.page.getByRole('heading', { name: 'Contents', exact: true }),
    });
  }

  @step()
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
  @step()
  async openAddItemToContainer(): Promise<CreateItemDialog> {
    await this.createItemDialog.open(this.contentsSection().getByRole('button', { name: 'Add item' }));
    return this.createItemDialog;
  }

  @step()
  async moveDurable(locationName: string): Promise<void> {
    await this.page.getByRole('button', { name: 'Edit', exact: true }).click();
    const sheet = this.page.getByRole('dialog', { name: 'Edit durable' });
    const heading = sheet.getByRole('heading', { name: 'Edit durable' });
    await expect(heading).toBeVisible();

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
    await expect(heading).toBeHidden();
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

  @step()
  async useStock(locationName: string, quantity: string): Promise<void> {
    await this.stockRow(locationName).getByRole('button', { name: 'Use', exact: true }).click();
    const sheet = this.page.getByRole('dialog', { name: 'Use stock' });
    const heading = sheet.getByRole('heading', { name: 'Use stock' });
    await expect(heading).toBeVisible();
    await sheet.getByLabel('Quantity', { exact: true }).fill(quantity);

    const responsePromise = this.page.waitForResponse(
      (resp) =>
        /\/api\/v1\/inventory\/consumables\/[0-9a-f-]+\/consume$/.test(resp.url()) &&
        resp.request().method() === 'POST' &&
        resp.status() === 200,
    );
    await sheet.getByRole('button', { name: 'Use', exact: true }).click();
    await responsePromise;
    await expect(heading).toBeHidden();
  }

  /** Carves one unit out of the stock at `locationName`; resolves to the new durable's id. */
  @step()
  async trackOne(locationName: string, durableName: string): Promise<string> {
    await this.stockRow(locationName).getByRole('button', { name: 'Track one', exact: true }).click();
    const sheet = this.page.getByRole('dialog', { name: 'Track one individually' });
    const heading = sheet.getByRole('heading', { name: 'Track one individually' });
    await expect(heading).toBeVisible();
    await sheet.getByLabel('Name', { exact: true }).fill(durableName);

    const responsePromise = this.page.waitForResponse(
      (resp) =>
        /\/api\/v1\/inventory\/consumables\/[0-9a-f-]+\/track-one$/.test(resp.url()) &&
        resp.request().method() === 'POST' &&
        resp.ok(),
    );
    await sheet.getByRole('button', { name: 'Track individually' }).click();
    const response = await responsePromise;
    await expect(heading).toBeHidden();
    const body = await response.json();
    return body.item.id as string;
  }
}
