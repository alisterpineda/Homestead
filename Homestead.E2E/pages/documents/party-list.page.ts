import { type Locator, type Page, type Response, expect } from '@playwright/test';
import { expectOk } from '../../helpers/playwright.helpers';
import { step } from '../../helpers/step';

const isPartiesList = (resp: Response) =>
  resp.url().includes('/api/v1/documents/parties?') && resp.request().method() === 'GET' && resp.status() === 200;

export class PartyListPage {
  readonly searchInput: Locator;
  readonly newPartyButton: Locator;

  constructor(readonly page: Page) {
    this.searchInput = page.getByRole('textbox', { name: 'Search parties' });
    this.newPartyButton = page.getByRole('button', { name: 'New Party' });
  }

  async goto(): Promise<void> {
    const responsePromise = this.page.waitForResponse(isPartiesList);
    await this.page.goto('/documents/parties');
    await responsePromise;
  }

  /** A party's name as listed. */
  party(name: string): Locator {
    return this.page.getByRole('main').getByText(name, { exact: true });
  }

  @step()
  async search(term: string): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) => isPartiesList(resp) && new URL(resp.url()).searchParams.get('Search') === term,
    );
    await this.searchInput.fill(term);
    await responsePromise;
  }

  @step()
  async createParty(name: string, options?: { description?: string }): Promise<void> {
    await this.newPartyButton.click();
    const dialog = this.page.getByRole('dialog', { name: 'Create Party' });
    const heading = dialog.getByRole('heading', { name: 'Create Party' });
    await expect(heading).toBeVisible();

    await dialog.getByLabel('Name', { exact: true }).fill(name);
    if (options?.description) {
      await dialog.getByLabel('Description (optional)').fill(options.description);
    }

    const createResponse = this.page.waitForResponse(
      (resp) => resp.url().endsWith('/api/v1/documents/parties') && resp.request().method() === 'POST',
    );
    await dialog.getByRole('button', { name: 'Create' }).click();
    expect((await createResponse).status()).toBe(201);
    await expect(heading).toBeHidden();
  }

  /** Opens a listed party's edit sheet by clicking its row; resolves to the sheet's heading. */
  @step()
  async openParty(name: string): Promise<Locator> {
    await this.party(name).click();
    const heading = this.editSheet.getByRole('heading', { name: 'Edit Party' });
    await expect(heading).toBeVisible();
    return heading;
  }

  get editSheet(): Locator {
    return this.page.getByRole('dialog', { name: 'Edit Party' });
  }

  /** Renames a party through its edit sheet and waits for the save; the sheet closes on success. */
  @step()
  async renameParty(name: string, newName: string, options?: { description?: string }): Promise<void> {
    const heading = await this.openParty(name);
    await this.editSheet.getByLabel('Name', { exact: true }).fill(newName);
    if (options?.description) {
      await this.editSheet.getByLabel('Description').fill(options.description);
    }

    const saveResponse = this.page.waitForResponse(
      (resp) => /\/api\/v1\/documents\/parties\/[0-9a-f-]+$/.test(resp.url()) && resp.request().method() === 'PUT',
    );
    await this.editSheet.getByRole('button', { name: 'Save' }).click();
    await expectOk(await saveResponse);
    await expect(heading).toBeHidden();
  }

  @step()
  async select(name: string): Promise<void> {
    const checkbox = this.page.getByRole('checkbox', { name: `Select ${name}`, exact: true });
    await checkbox.click();
    await expect(checkbox).toBeChecked();
  }

  @step()
  async deleteSelected(): Promise<void> {
    await this.page.getByRole('button', { name: 'Delete', exact: true }).click();
    const confirm = this.page.getByRole('alertdialog', { name: /^Delete \d+ part(y|ies)\?$/ });
    await expect(confirm.getByRole('heading')).toBeVisible();
    const deleteResponse = this.page.waitForResponse(
      (resp) => resp.url().endsWith('/api/v1/documents/parties/bulk-delete') && resp.request().method() === 'POST',
    );
    await confirm.getByRole('button', { name: 'Delete', exact: true }).click();
    await expectOk(await deleteResponse);
  }
}
