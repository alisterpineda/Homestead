import { type Locator, type Page, expect } from '@playwright/test';
import { pickParentArea } from './inventory-pickers';

export class AreaListPage {
  readonly newAreaButton: Locator;
  readonly noAreasEmpty: Locator;
  readonly createDialog: Locator;

  constructor(readonly page: Page) {
    this.newAreaButton = page.getByRole('button', { name: 'New area' });
    this.noAreasEmpty = page.getByText('No areas yet');
    this.createDialog = page.locator('hlm-dialog-content').filter({
      has: page.getByRole('heading', { name: 'New area' }),
    });
  }

  async goto(): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/inventory/areas') &&
        resp.request().method() === 'GET' &&
        resp.status() === 200,
    );
    await this.page.goto('/inventory/areas');
    await responsePromise;
  }

  /** The area's name cell — a link into the Items list filtered to that area. */
  rowLink(name: string): Locator {
    return this.page.locator('app-area-table').getByRole('link', { name, exact: true });
  }

  /** The "in <parent>" caption under a nested area's name. */
  parentCaption(parentName: string): Locator {
    return this.page.locator('app-area-table').getByText(`in ${parentName}`, { exact: true });
  }

  async createArea(name: string, parentName?: string): Promise<void> {
    await this.newAreaButton.first().click();
    await expect(this.createDialog).toBeVisible();

    await this.createDialog.getByLabel('Name', { exact: true }).fill(name);
    if (parentName !== undefined) {
      await pickParentArea(this.page, this.createDialog, parentName);
      await expect(this.createDialog.getByRole('button', { name: 'Parent area', exact: true })).toContainText(
        parentName,
      );
    }

    const responsePromise = this.page.waitForResponse(
      (resp) =>
        resp.url().endsWith('/api/v1/inventory/areas') &&
        resp.request().method() === 'POST' &&
        resp.status() === 201,
    );
    await this.createDialog.getByRole('button', { name: 'Create', exact: true }).click();
    await responsePromise;
    await expect(this.createDialog).toBeHidden();
    await expect(this.rowLink(name)).toBeVisible();
  }
}
