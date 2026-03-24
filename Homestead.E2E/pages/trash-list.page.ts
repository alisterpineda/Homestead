import { type Locator, type Page, expect } from '@playwright/test';

export class TrashListPage {
  readonly trashEmpty: Locator;

  constructor(readonly page: Page) {
    this.trashEmpty = page.getByText('Trash is empty');
  }

  async goto() {
    await this.page.goto('/documents/trash');
    await this.page.waitForLoadState('networkidle');
  }

  async selectDocument(name: string) {
    await this.page.getByLabel(`Select ${name}`).click();
  }

  async selectAll() {
    await this.page.getByLabel('Select all').click();
  }

  async restore() {
    await this.page.getByRole('button', { name: /^Restore$|^Restoring/ }).click();
    await this.page.waitForLoadState('networkidle');
  }

  async deletePermanently() {
    await this.page.getByRole('button', { name: /Delete Permanently/ }).click();
    // Confirm in alert dialog
    await this.page.getByRole('button', { name: /Delete Permanently/ }).last().click();
    await this.page.waitForLoadState('networkidle');
  }

  async emptyTrash() {
    // Click the "Empty Trash" toolbar button (not in selection mode)
    await this.page.getByRole('button', { name: 'Empty Trash' }).first().click();
    // Confirm in alert dialog
    await this.page.getByRole('button', { name: /Empty Trash/ }).last().click();
    await this.page.waitForLoadState('networkidle');
  }

  async getFirstDocumentName(): Promise<string> {
    const name = await this.page.locator('table tbody tr td:nth-child(2)').first().textContent();
    return name?.trim() ?? '';
  }

  async waitForDocumentVisible(name: string, timeout = 10_000) {
    await expect(this.page.getByText(name).first()).toBeVisible({ timeout });
  }

  async waitForEmpty(timeout = 10_000) {
    await expect(this.trashEmpty).toBeVisible({ timeout });
  }
}
