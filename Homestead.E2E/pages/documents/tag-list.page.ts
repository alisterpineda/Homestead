import { type Locator, type Page, expect } from '@playwright/test';
import { expectOk } from '../../helpers/playwright.helpers';
import { step } from '../../helpers/step';

export class TagListPage {
  readonly searchInput: Locator;
  readonly newTagButton: Locator;

  constructor(readonly page: Page) {
    this.searchInput = page.getByRole('textbox', { name: 'Search tags' });
    this.newTagButton = page.getByRole('button', { name: /New Tag/ });
  }

  async goto(): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) => resp.url().includes('/api/v1/documents/tags?') && resp.request().method() === 'GET' && resp.status() === 200,
    );
    await this.page.goto('/documents/tags');
    await responsePromise;
  }

  /** A tag's path as listed. */
  tag(path: string): Locator {
    return this.page.getByRole('main').getByText(path, { exact: true });
  }

  @step()
  async createTag(path: string, options?: { description?: string }): Promise<void> {
    await this.newTagButton.click();
    const dialog = this.page.getByRole('dialog', { name: 'Create Tag' });
    const heading = dialog.getByRole('heading', { name: 'Create Tag' });
    await expect(heading).toBeVisible();

    await dialog.getByLabel('Path').fill(path);
    if (options?.description) {
      await dialog.getByLabel('Description').fill(options.description);
    }

    const createResponse = this.page.waitForResponse(
      (resp) => resp.url().endsWith('/api/v1/documents/tags') && resp.request().method() === 'POST',
    );
    await dialog.getByRole('button', { name: 'Create' }).click();
    expect((await createResponse).status()).toBe(201);
    await expect(heading).toBeHidden();
  }

  @step()
  async search(term: string): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/documents/tags?') &&
        new URL(resp.url()).searchParams.get('Search') === term &&
        resp.status() === 200,
    );
    await this.searchInput.fill(term);
    await responsePromise;
  }

  @step()
  async select(path: string): Promise<void> {
    const checkbox = this.page.getByRole('checkbox', { name: `Select ${path}`, exact: true });
    await checkbox.click();
    await expect(checkbox).toBeChecked();
  }

  @step()
  async deleteSelected(): Promise<void> {
    await this.page.getByRole('button', { name: 'Delete', exact: true }).click();
    const confirm = this.page.getByRole('alertdialog');
    await expect(confirm.getByRole('heading')).toBeVisible();
    const deleteResponse = this.page.waitForResponse(
      (resp) => resp.url().endsWith('/api/v1/documents/tags/bulk-delete') && resp.request().method() === 'POST',
    );
    await confirm.getByRole('button', { name: 'Delete', exact: true }).click();
    await expectOk(await deleteResponse);
  }
}
