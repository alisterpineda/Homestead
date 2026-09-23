import { type Locator, type Page, expect } from '@playwright/test';
import { expectOk } from '../../helpers/playwright.helpers';
import { step } from '../../helpers/step';

const TAGS_API = '/api/v1/inventory/tags';

/** The name cell renders a path's segments joined by " / ". */
function tagLinkName(path: string): string {
  return path.split('/').join(' / ');
}

/** `/inventory/tags` — the Inventory tag table (create, edit in a side sheet, select + bulk delete). */
export class InventoryTagListPage {
  readonly searchInput: Locator;
  readonly newTagButton: Locator;
  readonly editSheet: Locator;

  constructor(readonly page: Page) {
    this.searchInput = page.getByRole('textbox', { name: 'Search tags' });
    this.newTagButton = page.getByRole('button', { name: /New Tag/ });
    this.editSheet = page.getByRole('dialog', { name: 'Edit Tag' });
  }

  async goto(): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) => resp.url().includes(`${TAGS_API}?`) && resp.request().method() === 'GET' && resp.status() === 200,
    );
    await this.page.goto('/inventory/tags');
    await responsePromise;
  }

  @step()
  async search(term: string): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) =>
        resp.url().includes(`${TAGS_API}?`) &&
        new URL(resp.url()).searchParams.get('Search') === term &&
        resp.status() === 200,
    );
    await this.searchInput.fill(term);
    await responsePromise;
  }

  /**
   * A tag's name cell, which reads as its path with " / " separators ("garden / hoses") and links
   * into the Items list filtered to that tag.
   */
  tagLink(path: string): Locator {
    return this.page.locator('app-tag-table').getByRole('link', { name: tagLinkName(path), exact: true });
  }

  row(path: string): Locator {
    // `has` resolves relative to the row, so the link is matched without the table scope.
    return this.page
      .locator('app-tag-table')
      .getByRole('row')
      .filter({ has: this.page.getByRole('link', { name: tagLinkName(path), exact: true }) });
  }

  @step()
  async createTag(path: string): Promise<void> {
    await this.newTagButton.click();
    const dialog = this.page.getByRole('dialog', { name: 'Create Tag' });
    const heading = dialog.getByRole('heading', { name: 'Create Tag' });
    await expect(heading).toBeVisible();
    await dialog.getByLabel('Path').fill(path);

    const responsePromise = this.page.waitForResponse(
      (resp) => resp.url().endsWith(TAGS_API) && resp.request().method() === 'POST',
    );
    await dialog.getByRole('button', { name: 'Create', exact: true }).click();
    await expectOk(await responsePromise);
    await expect(heading).toBeHidden();
  }

  /** Renames the tag at `path` to the leaf name `newName` in the edit sheet (keeps its parent). */
  @step()
  async rename(path: string, newName: string): Promise<void> {
    // The row itself opens the sheet; its name cell is a link elsewhere, so use the keyboard route.
    await this.row(path).press('Enter');
    const heading = this.editSheet.getByRole('heading', { name: 'Edit Tag' });
    await expect(heading).toBeVisible();
    await this.editSheet.getByLabel('Name', { exact: true }).fill(newName);

    const responsePromise = this.page.waitForResponse(
      (resp) => resp.url().includes(`${TAGS_API}/`) && resp.request().method() === 'PATCH',
    );
    await this.editSheet.getByRole('button', { name: 'Save', exact: true }).click();
    await expectOk(await responsePromise);
    await expect(heading).toBeHidden();
  }

  /** Ticks a row's selection checkbox, labelled with the tag's leaf name. */
  @step()
  async select(name: string): Promise<void> {
    const checkbox = this.page.getByRole('checkbox', { name: `Select ${name}`, exact: true });
    await checkbox.click();
    await expect(checkbox).toBeChecked();
  }

  @step()
  async deleteSelected(): Promise<void> {
    await this.page.getByRole('button', { name: 'Delete', exact: true }).click();
    const confirm = this.page.getByRole('alertdialog');
    await expect(confirm.getByRole('heading', { name: /^Delete \d+ tags?\?$/ })).toBeVisible();
    const responsePromise = this.page.waitForResponse(
      (resp) => resp.url().endsWith(`${TAGS_API}/bulk-delete`) && resp.request().method() === 'POST',
    );
    await confirm.getByRole('button', { name: 'Delete', exact: true }).click();
    await expectOk(await responsePromise);
    await expect(confirm.getByRole('heading')).toBeHidden();
  }
}
