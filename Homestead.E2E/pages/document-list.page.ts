import { type Locator, type Page, expect } from '@playwright/test';

export class DocumentListPage {
  readonly searchInput: Locator;
  readonly uploadButton: Locator;
  readonly noDocsEmpty: Locator;
  readonly noMatchingEmpty: Locator;
  readonly tableViewButton: Locator;
  readonly gridViewButton: Locator;
  readonly selectButton: Locator;

  constructor(readonly page: Page) {
    this.searchInput = page.getByLabel('Search documents');
    this.uploadButton = page.getByRole('button', { name: 'Upload Document' });
    this.noDocsEmpty = page.getByText('No documents yet');
    this.noMatchingEmpty = page.getByText('No matching documents');
    this.tableViewButton = page.getByLabel('Table view');
    this.gridViewButton = page.getByLabel('Grid view');
    this.selectButton = page.getByLabel('Enter selection mode');
  }

  async goto() {
    const responsePromise = this.page.waitForResponse(
      (resp) => resp.url().includes('/api/v1/documents') && resp.request().method() === 'GET' && resp.status() === 200,
    );
    await this.page.goto('/documents');
    await responsePromise;
  }

  async search(term: string) {
    const responsePromise = this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/documents') &&
        resp.url().includes('Search=') &&
        resp.request().method() === 'GET' &&
        resp.status() === 200,
    );
    await this.searchInput.fill(term);
    await this.page.waitForURL(/search=/, { timeout: 5_000 });
    await responsePromise;
  }

  async clearSearch() {
    const responsePromise = this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/documents') &&
        !resp.url().includes('Search=') &&
        resp.request().method() === 'GET' &&
        resp.status() === 200,
    );
    await this.searchInput.fill('');
    await this.page.waitForURL((url) => !url.searchParams.has('search'), { timeout: 5_000 });
    await responsePromise;
  }

  async clickDocument(name: string) {
    await this.page.getByRole('cell', { name }).first().click();
  }

  async waitForDocumentVisible(name: string, timeout = 15_000) {
    await expect(this.page.getByText(name).first()).toBeVisible({ timeout });
  }

  async switchToTableView() {
    // Wait for either the table view toggle or the empty state to appear
    await Promise.race([
      this.tableViewButton.waitFor({ state: 'visible', timeout: 10_000 }),
      this.noDocsEmpty.waitFor({ state: 'visible', timeout: 10_000 }),
    ]).catch(() => {});
    if (await this.tableViewButton.isVisible()) {
      await this.tableViewButton.click();
      await expect(this.page.locator('table')).toBeVisible();
    }
  }

  async enterSelectionMode() {
    // In table view, clicking a row checkbox enters selection mode automatically
    // In grid view, click the "Select" button
    try {
      await expect(this.selectButton).toBeVisible({ timeout: 2_000 });
      await this.selectButton.click();
    } catch {
      // Not visible = table view, where checkboxes handle selection directly
    }
  }

  async selectDocumentCheckbox(name: string) {
    await this.page.getByLabel(`Select ${name}`).click();
  }

  async selectAllOnPage() {
    await this.page.getByRole('button', { name: 'Select all' }).click();
  }

  async exitSelectionMode() {
    await this.page.getByLabel('Exit selection mode').click();
  }

  selectionCountText() {
    return this.page.getByText(/\d+ documents? selected/);
  }

  bulkDeleteButton() {
    return this.page.getByRole('button', { name: 'Delete' });
  }

  bulkTagsButton() {
    return this.page.locator('app-bulk-edit-tags-popover button');
  }

  bulkPartiesButton() {
    return this.page.locator('app-bulk-edit-parties-popover button');
  }
}
