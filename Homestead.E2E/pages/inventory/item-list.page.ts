import { type Locator, type Page, expect } from '@playwright/test';
import { escapeRegExp } from '../../helpers/playwright.helpers';
import { step } from '../../helpers/step';
import { CreateItemDialog } from './create-item-dialog';

/** `/inventory/items` — the unified durables + consumables list (table on desktop, cards on phones). */
export class ItemListPage {
  readonly newItemButton: Locator;
  readonly searchInput: Locator;
  readonly filterButton: Locator;
  readonly createItemDialog: CreateItemDialog;

  constructor(readonly page: Page) {
    this.newItemButton = page.getByRole('button', { name: 'New item' });
    this.searchInput = page.getByRole('textbox', { name: 'Search items' });
    this.filterButton = page.getByRole('button', { name: /^Filter\b/ });
    this.createItemDialog = new CreateItemDialog(page);
  }

  async goto(query = ''): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/inventory/consumables') &&
        resp.request().method() === 'GET' &&
        resp.status() === 200,
    );
    await this.page.goto(`/inventory/items${query}`);
    await responsePromise;
  }

  /**
   * Searches and waits for the server's filtered answers for both kinds. While a search is in
   * flight the page hides non-matching rows client-side, so a negative assertion alone could pass
   * before (or without) the backend ever applying the search.
   */
  @step()
  async search(term: string): Promise<void> {
    const searched = (resource: 'durables' | 'consumables') =>
      this.page.waitForResponse(
        (resp) =>
          resp.url().includes(`/api/v1/inventory/${resource}`) &&
          new URL(resp.url()).searchParams.get('Search') === term &&
          resp.request().method() === 'GET' &&
          resp.status() === 200,
      );
    const responses = Promise.all([searched('durables'), searched('consumables')]);
    await this.searchInput.fill(term);
    await responses;
  }

  /** An item's name in the visible rows, whichever view (table / cards) is showing. */
  item(name: string): Locator {
    return this.page.locator('app-inventory-table, app-inventory-card-grid').getByText(name, { exact: true });
  }

  filtersPopover(): Locator {
    return this.page.locator('[hlmPopoverContent]').filter({ hasText: 'Filters' });
  }

  async openFilters(): Promise<Locator> {
    await this.filterButton.click();
    const popover = this.filtersPopover();
    await expect(popover).toBeVisible();
    return popover;
  }

  /** Toggles a tag in the Filters popover's Tags facet (searching for it first). */
  @step()
  async toggleTagFilter(path: string): Promise<void> {
    const popover = await this.openFilters();
    await popover.getByRole('textbox', { name: 'Search tags' }).fill(path);
    await popover.getByRole('button', { name: path, exact: true }).click();
    await this.page.keyboard.press('Escape');
    await expect(popover).toBeHidden();
  }

  /** Turns on "Include sub-tags" (off by default). */
  @step()
  async includeSubTags(): Promise<void> {
    const popover = await this.openFilters();
    const checkbox = popover.getByRole('checkbox', { name: 'Include sub-tags' });
    // Spartan updates aria-checked asynchronously, which `setChecked()` reads too early.
    await checkbox.click();
    await expect(checkbox).toBeChecked();
    await this.page.keyboard.press('Escape');
    await expect(popover).toBeHidden();
  }

  /** Turns on "Show inactive", which brings archived items back into the list. */
  @step()
  async showInactive(): Promise<void> {
    const popover = await this.openFilters();
    const checkbox = popover.getByRole('checkbox', { name: 'Show inactive' });
    await checkbox.click();
    await expect(checkbox).toBeChecked();
    await this.page.keyboard.press('Escape');
    await expect(popover).toBeHidden();
  }

  /** Picks an option in the single-choice Warranty facet ("Expiring soon", "None", …). */
  @step()
  async filterByWarranty(label: string): Promise<void> {
    const popover = await this.openFilters();
    await popover.getByRole('button', { name: new RegExp(`^${escapeRegExp(label)} \\(\\d+\\)$`) }).click();
    await this.page.keyboard.press('Escape');
    await expect(popover).toBeHidden();
  }
}
