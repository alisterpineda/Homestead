import { type Locator, type Page, expect } from '@playwright/test';

export class TrashListPage {
  readonly trashEmpty: Locator;

  constructor(readonly page: Page) {
    this.trashEmpty = page.getByText('Trash is empty');
  }

  async goto() {
    const responsePromise = this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/documents') &&
        resp.url().includes('Deleted=true') &&
        resp.request().method() === 'GET' &&
        resp.status() === 200,
    );
    await this.page.goto('/documents/trash');
    await responsePromise;
  }

  async selectDocument(name: string) {
    await this.page.getByLabel(`Select ${name}`).click();
  }

  async selectAll() {
    await this.page.getByLabel('Select all').click();
  }

  async restore() {
    const responsePromise = this.page.waitForResponse(
      (resp) => resp.url().includes('/api/v1/documents/bulk-restore') && resp.request().method() === 'POST',
    );
    await this.page.getByRole('button', { name: /^Restore$|^Restoring/ }).click();
    await responsePromise;
  }

  async deletePermanently() {
    await this.page.getByRole('button', { name: /Delete Permanently/ }).click();
    // Confirm in alert dialog and wait for API response
    const deleteResponse = this.page.waitForResponse(
      (resp) => resp.url().includes('/api/v1/documents/bulk-permanently-delete') && resp.request().method() === 'POST',
    );
    await this.page.getByRole('button', { name: /Delete Permanently/ }).last().click();
    await deleteResponse;
  }

  async emptyTrash() {
    // Click the "Empty Trash" toolbar button (not in selection mode)
    await this.page.getByRole('button', { name: 'Empty Trash' }).first().click();
    // Confirm in alert dialog and wait for API response
    const emptyResponse = this.page.waitForResponse(
      (resp) => resp.url().includes('/api/v1/documents/empty-trash') && resp.request().method() === 'POST',
    );
    await this.page.getByRole('button', { name: /Empty Trash/ }).last().click();
    await emptyResponse;
  }

  async getFirstDocumentName(): Promise<string> {
    const firstNameCell = this.page.locator('table tbody tr').first().locator('td').nth(1);
    await expect(firstNameCell).toBeVisible();
    const name = await firstNameCell.textContent();
    return name?.trim() ?? '';
  }

  async waitForDocumentVisible(name: string, timeout = 10_000) {
    await expect(this.page.getByText(name).first()).toBeVisible({ timeout });
  }

  async waitForEmpty(timeout = 10_000) {
    await expect(this.trashEmpty).toBeVisible({ timeout });
  }
}
