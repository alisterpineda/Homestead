import { type Locator, type Page, expect } from '@playwright/test';
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

  /** An item's name in the visible rows, whichever view (table / cards) is showing. */
  item(name: string): Locator {
    return this.page
      .locator('app-inventory-table, app-inventory-card-grid')
      .getByText(name, { exact: true });
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
  async toggleTagFilter(path: string): Promise<void> {
    const popover = await this.openFilters();
    await popover.getByRole('textbox', { name: 'Search tags' }).fill(path);
    await popover.getByRole('button', { name: path, exact: true }).click();
    await this.page.keyboard.press('Escape');
    await expect(popover).toBeHidden();
  }

  async setIncludeSubTags(include: boolean): Promise<void> {
    const popover = await this.openFilters();
    const checkbox = popover.getByRole('checkbox', { name: 'Include sub-tags' });
    if ((await checkbox.getAttribute('aria-checked')) !== String(include)) {
      await checkbox.click();
    }
    await this.page.keyboard.press('Escape');
    await expect(popover).toBeHidden();
  }
}
