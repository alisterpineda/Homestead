import { type Locator, type Page, expect } from '@playwright/test';
import { expectOk } from '../../helpers/playwright.helpers';
import { step } from '../../helpers/step';

export class TrashListPage {
  readonly trashEmpty: Locator;

  constructor(readonly page: Page) {
    this.trashEmpty = page.getByText('Trash is empty');
  }

  async goto(): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/documents?') &&
        resp.url().includes('Deleted=true') &&
        resp.request().method() === 'GET' &&
        resp.status() === 200,
    );
    await this.page.goto('/documents/trash');
    await responsePromise;
  }

  /** A trashed document's name as listed. */
  document(name: string): Locator {
    return this.page.getByRole('main').getByText(name, { exact: true });
  }

  @step()
  async select(name: string): Promise<void> {
    const checkbox = this.page.getByRole('checkbox', { name: `Select ${name}`, exact: true });
    await checkbox.click();
    await expect(checkbox).toBeChecked();
  }

  @step()
  async restoreSelected(): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) => resp.url().endsWith('/api/v1/documents/bulk-restore') && resp.request().method() === 'POST',
    );
    await this.page.getByRole('button', { name: /^(Restore|Restoring)/ }).click();
    await expectOk(await responsePromise);
  }

  @step()
  async deleteSelectedPermanently(): Promise<void> {
    await this.page.getByRole('button', { name: /Delete Permanently/ }).click();
    const confirm = this.page.getByRole('alertdialog');
    // Wait for the dialog to finish opening before confirming, so the click never lands on the
    // trigger button behind the backdrop.
    await expect(confirm.getByRole('heading')).toBeVisible();
    const deleteResponse = this.page.waitForResponse(
      (resp) =>
        resp.url().endsWith('/api/v1/documents/bulk-permanently-delete') && resp.request().method() === 'POST',
    );
    await confirm.getByRole('button', { name: /Delete Permanently/ }).click();
    await expectOk(await deleteResponse);
  }

  @step()
  async emptyTrash(): Promise<void> {
    await this.page.getByRole('button', { name: 'Empty Trash' }).first().click();
    const confirm = this.page.getByRole('alertdialog');
    await expect(confirm.getByRole('heading')).toBeVisible();
    const emptyResponse = this.page.waitForResponse(
      (resp) => resp.url().endsWith('/api/v1/documents/empty-trash') && resp.request().method() === 'POST',
    );
    await confirm.getByRole('button', { name: /Empty Trash/ }).click();
    await expectOk(await emptyResponse);
  }
}
