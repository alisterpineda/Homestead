import { type Locator, type Page, expect } from '@playwright/test';

export class TagListPage {
  readonly searchInput: Locator;
  readonly noTagsEmpty: Locator;

  constructor(readonly page: Page) {
    this.searchInput = page.getByLabel('Search tags');
    this.noTagsEmpty = page.getByText('No tags yet');
  }

  async goto() {
    await this.page.goto('/documents/tags');
    await this.page.waitForLoadState('networkidle');
  }

  async createTag(tagPath: string, options?: { description?: string }) {
    // Click the "New Tag" button — it's the dialog trigger
    await this.page.getByRole('button', { name: /New Tag/ }).click();

    // Fill in the dialog form
    await this.page.getByLabel('Path').fill(tagPath);
    if (options?.description) {
      await this.page.getByLabel('Description').fill(options.description);
    }

    // Submit
    await this.page.getByRole('button', { name: 'Create' }).click();

    // Wait for dialog to close and list to refresh
    await expect(this.page.getByRole('heading', { name: 'Create Tag' })).not.toBeVisible({ timeout: 5_000 });
    await this.page.waitForLoadState('networkidle');
  }

  async searchTags(term: string) {
    await this.searchInput.fill(term);
    await this.page.waitForTimeout(500); // debounce
    await this.page.waitForLoadState('networkidle');
  }

  async clearSearch() {
    await this.searchInput.fill('');
    await this.page.waitForTimeout(500);
    await this.page.waitForLoadState('networkidle');
  }

  async waitForTagVisible(tagPath: string, timeout = 10_000) {
    await expect(this.page.getByText(tagPath).first()).toBeVisible({ timeout });
  }

  async selectTagCheckbox(tagPath: string) {
    await this.page.getByLabel(`Select ${tagPath}`).click();
  }

  async deleteSelected() {
    await this.page.getByRole('button', { name: 'Delete' }).click();
    // Confirm in alert dialog
    await this.page.getByRole('button', { name: 'Delete' }).last().click();
    await this.page.waitForLoadState('networkidle');
  }
}
