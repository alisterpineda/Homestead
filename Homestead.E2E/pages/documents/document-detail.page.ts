import { type Locator, type Page, expect } from '@playwright/test';
import { isMobileViewport } from '../../helpers/playwright.helpers';

export class DocumentDetailPage {
  readonly nameInput: Locator;
  readonly saveButton: Locator;
  readonly downloadButton: Locator;

  constructor(readonly page: Page) {
    this.nameInput = page.getByLabel('Name');
    this.saveButton = page.getByRole('button', { name: /^Save$|^Saving/ });
    this.downloadButton = page.getByRole('button', { name: 'Download' });
  }

  async goto(documentId: string) {
    await this.page.goto(`/documents/${documentId}`);
    await this.waitForLoaded();
  }

  async waitForLoaded() {
    // Mobile renders a tabbed layout opening on Preview; Name lives in the Info tab.
    if (isMobileViewport(this.page)) {
      await this.page.getByRole('tab', { name: 'Info' }).click();
    }
    await expect(this.nameInput).toBeVisible({ timeout: 15_000 });
  }

  async getName(): Promise<string> {
    return this.nameInput.inputValue();
  }

  async setName(name: string) {
    await this.nameInput.fill(name);
  }

  async save() {
    // Wait for the save button to become enabled (changes detected)
    await expect(this.saveButton).toBeEnabled({ timeout: 5_000 });
    await this.saveButton.click();
    // Wait for save to complete (button re-enables after request)
    await expect(this.saveButton).toBeEnabled({ timeout: 10_000 });
  }

  async openMoreMenu() {
    await this.page.getByRole('button', { name: /more/i }).or(
      this.page.locator('button[aria-label]').filter({ has: this.page.locator('ng-icon[name="lucideEllipsisVertical"]') })
    ).click();
  }

  async delete() {
    await this.openMoreMenu();
    await this.page.getByText('Delete').click();
    // Confirm in alert dialog
    await this.page.getByRole('alertdialog').getByRole('button', { name: 'Delete' }).click();
  }
}
