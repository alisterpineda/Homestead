import { type Locator, type Page, expect } from '@playwright/test';

export class TagListPage {
  readonly searchInput: Locator;
  readonly noTagsEmpty: Locator;

  constructor(readonly page: Page) {
    this.searchInput = page.getByLabel('Search tags');
    this.noTagsEmpty = page.getByText('No tags yet');
  }

  async goto() {
    const responsePromise = this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/documents/tags') && resp.request().method() === 'GET' && resp.status() === 200,
    );
    await this.page.goto('/documents/tags');
    await responsePromise;
  }

  async createTag(tagPath: string, options?: { description?: string }) {
    // Click the "New Tag" button — it's the dialog trigger
    await this.page.getByRole('button', { name: /New Tag/ }).click();

    // Fill in the dialog form
    await this.page.getByLabel('Path').fill(tagPath);
    if (options?.description) {
      await this.page.getByLabel('Description').fill(options.description);
    }

    // Submit and wait for the API response
    const createResponse = this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/documents/tags') && resp.request().method() === 'POST' && resp.status() === 201,
    );
    await this.page.getByRole('button', { name: 'Create' }).click();
    await createResponse;

    // Wait for dialog to close
    await expect(this.page.getByRole('heading', { name: 'Create Tag' })).not.toBeVisible({ timeout: 5_000 });
  }

  async searchTags(term: string) {
    const responsePromise = this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/documents/tags') &&
        resp.url().includes('Search=') &&
        resp.request().method() === 'GET' &&
        resp.status() === 200,
    );
    await this.searchInput.fill(term);
    await responsePromise;
  }

  async clearSearch() {
    const responsePromise = this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/documents/tags') &&
        !resp.url().includes('Search=') &&
        resp.request().method() === 'GET' &&
        resp.status() === 200,
    );
    await this.searchInput.fill('');
    await responsePromise;
  }

  async waitForTagVisible(tagPath: string, timeout = 10_000) {
    await expect(this.page.getByText(tagPath).first()).toBeVisible({ timeout });
  }

  async selectTagCheckbox(tagPath: string) {
    await this.page.getByLabel(`Select ${tagPath}`).click();
  }

  async deleteSelected() {
    await this.page.getByRole('button', { name: 'Delete' }).click();
    // Confirm in alert dialog and wait for API response
    const deleteResponse = this.page.waitForResponse(
      (resp) => resp.url().includes('/api/v1/documents/tags/bulk-delete') && resp.request().method() === 'POST',
    );
    await this.page.getByRole('button', { name: 'Delete' }).last().click();
    await deleteResponse;
  }
}
