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
    await this.page.goto('/documents');
    await this.page.waitForLoadState('networkidle');
  }

  async search(term: string) {
    await this.searchInput.fill(term);
    // Wait for debounce (300ms) and URL update
    await this.page.waitForURL(/search=/, { timeout: 5_000 });
    await this.page.waitForLoadState('networkidle');
  }

  async clearSearch() {
    await this.searchInput.fill('');
    await this.page.waitForTimeout(500);
    await this.page.waitForLoadState('networkidle');
  }

  async clickDocument(name: string) {
    await this.page.getByRole('cell', { name }).first().click();
  }

  async waitForDocumentVisible(name: string, timeout = 15_000) {
    await expect(this.page.getByText(name).first()).toBeVisible({ timeout });
  }

  async switchToTableView() {
    // The view toggle is hidden when the document list is empty (showInitialEmpty).
    // Wait for it to appear after data loads; skip if the list is genuinely empty.
    await this.tableViewButton.waitFor({ state: 'visible', timeout: 10_000 }).catch(() => {});
    if (await this.tableViewButton.isVisible()) {
      await this.tableViewButton.click();
      await this.page.waitForLoadState('networkidle');
    }
  }

  async enterSelectionMode() {
    // In table view, clicking a row checkbox enters selection mode automatically
    // In grid view, click the "Select" button
    const selectBtn = this.selectButton;
    if (await selectBtn.isVisible()) {
      await selectBtn.click();
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
    // Target the Tags button inside the bulk edit popover component (selection toolbar)
    return this.page.locator('app-bulk-edit-tags-popover button');
  }

  bulkPartiesButton() {
    return this.page.locator('app-bulk-edit-parties-popover button');
  }
}
