import { type Locator, type Page, type Response, expect } from '@playwright/test';
import { expectOk } from '../../helpers/playwright.helpers';
import { step } from '../../helpers/step';
import { brandPickerPopover } from './inventory-pickers';

const BRANDS_API = '/api/v1/inventory/brands';

/** `/inventory/brands` — the brand management table (rename / merge / delete behind each row's ⋮). */
export class BrandListPage {
  readonly searchInput: Locator;
  readonly renameDialog: Locator;
  readonly mergeDialog: Locator;
  readonly deleteDialog: Locator;

  constructor(readonly page: Page) {
    // The brand picker's popover has a "Search brands" box too; the page's own sits in <main>.
    this.searchInput = page.getByRole('main').getByRole('textbox', { name: 'Search brands' });
    this.renameDialog = page.getByRole('dialog', { name: 'Rename brand' });
    this.mergeDialog = page.getByRole('dialog', { name: 'Merge brand' });
    this.deleteDialog = page.getByRole('alertdialog');
  }

  async goto(): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) => resp.url().includes(`${BRANDS_API}?`) && resp.request().method() === 'GET' && resp.status() === 200,
    );
    await this.page.goto('/inventory/brands');
    await responsePromise;
  }

  /** Searches and waits for the server's filtered page, so later negative assertions mean something. */
  @step()
  async search(term: string): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) =>
        resp.url().includes(`${BRANDS_API}?`) &&
        new URL(resp.url()).searchParams.get('Search') === term &&
        resp.status() === 200,
    );
    await this.searchInput.fill(term);
    await responsePromise;
  }

  /** The brand's name cell — a link into the Items list filtered to that brand. */
  nameLink(name: string): Locator {
    return this.page.locator('app-brand-table').getByRole('link', { name, exact: true });
  }

  row(name: string): Locator {
    // `has` resolves relative to the row, so the link is matched without the table scope.
    return this.page
      .locator('app-brand-table')
      .getByRole('row')
      .filter({ has: this.page.getByRole('link', { name, exact: true }) });
  }

  /** The row's "Items" count cell. */
  itemCount(name: string): Locator {
    return this.row(name).getByRole('cell').nth(1);
  }

  /** Opens the row's ⋮ menu and returns it (Rename / Merge into… / Delete). */
  @step()
  async openRowMenu(name: string): Promise<Locator> {
    await this.row(name).getByRole('button', { name: 'Brand actions' }).click();
    const menu = this.page.getByRole('menu');
    await expect(menu).toBeVisible();
    return menu;
  }

  @step()
  async closeRowMenu(): Promise<void> {
    await this.page.keyboard.press('Escape');
    await expect(this.page.getByRole('menu')).toBeHidden();
  }

  /** Renames through the ⋮ menu and submits; resolves to the PATCH response whatever its status. */
  @step()
  async submitRename(from: string, to: string): Promise<Response> {
    const menu = await this.openRowMenu(from);
    await menu.getByRole('menuitem', { name: 'Rename' }).click();
    await expect(this.renameDialog.getByRole('heading', { name: 'Rename brand' })).toBeVisible();
    await this.renameDialog.getByLabel('Name', { exact: true }).fill(to);

    const responsePromise = this.page.waitForResponse(
      (resp) => resp.url().includes(`${BRANDS_API}/`) && resp.request().method() === 'PATCH',
    );
    await this.renameDialog.getByRole('button', { name: 'Save', exact: true }).click();
    return responsePromise;
  }

  @step()
  async rename(from: string, to: string): Promise<void> {
    await expectOk(await this.submitRename(from, to));
    await expect(this.renameDialog.getByRole('heading', { name: 'Rename brand' })).toBeHidden();
  }

  /** Opens "Merge into…" for `source` from its ⋮ menu. */
  @step()
  async openMerge(source: string): Promise<void> {
    const menu = await this.openRowMenu(source);
    await menu.getByRole('menuitem', { name: 'Merge into…' }).click();
    await expect(this.mergeDialog.getByRole('heading', { name: 'Merge brand' })).toBeVisible();
  }

  /** Opens the merge dialog's target picker; resolves to its popover (the search box may be pre-seeded). */
  @step()
  async openMergeTargetPicker(): Promise<Locator> {
    await this.mergeDialog.getByRole('button', { name: 'Target brand' }).click();
    const popover = brandPickerPopover(this.page);
    await expect(popover).toBeVisible();
    return popover;
  }

  /** Picks `target` in the open target picker (searching for it first unless `search` is false). */
  @step()
  async chooseMergeTarget(target: string, { search = true }: { search?: boolean } = {}): Promise<void> {
    const popover = brandPickerPopover(this.page);
    if (search) {
      await popover.getByRole('textbox', { name: 'Search brands' }).fill(target);
    }
    await popover.getByRole('button', { name: target, exact: true }).click();
    await expect(popover).toBeHidden();
    await expect(this.mergeDialog.getByRole('button', { name: 'Target brand' })).toContainText(target);
  }

  /** Confirms the open merge dialog and waits for the merge to land. */
  @step()
  async confirmMerge(): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) => /\/brands\/[0-9a-f-]+\/merge$/.test(resp.url()) && resp.request().method() === 'POST',
    );
    await this.mergeDialog.getByRole('button', { name: 'Merge', exact: true }).click();
    await expectOk(await responsePromise);
    await expect(this.mergeDialog.getByRole('heading', { name: 'Merge brand' })).toBeHidden();
  }

  /** Deletes through the ⋮ menu's confirm dialog; resolves to the DELETE response whatever its status. */
  @step()
  async submitDelete(name: string): Promise<Response> {
    const menu = await this.openRowMenu(name);
    await menu.getByRole('menuitem', { name: 'Delete' }).click();
    await expect(this.deleteDialog.getByRole('heading', { name: `Delete ${name}?` })).toBeVisible();

    const responsePromise = this.page.waitForResponse(
      (resp) => resp.url().includes(`${BRANDS_API}/`) && resp.request().method() === 'DELETE',
    );
    await this.deleteDialog.getByRole('button', { name: 'Delete', exact: true }).click();
    return responsePromise;
  }
}
