import { type Locator, type Page, expect } from '@playwright/test';
import { step } from '../../helpers/step';
import { expectOk } from '../../helpers/playwright.helpers';

/** `/finances/payees` — the household payee catalogue. */
export class PayeeListPage {
  readonly newPayeeButton: Locator;
  readonly searchInput: Locator;
  /** The dialog role sits on a zero-size CDK container; assert on the heading instead. */
  readonly createDialog: Locator;
  readonly createHeading: Locator;
  readonly editDialog: Locator;
  readonly editHeading: Locator;

  constructor(readonly page: Page) {
    this.newPayeeButton = page.getByRole('button', { name: 'New payee' });
    this.searchInput = page.getByRole('textbox', { name: 'Search payees' });
    this.createDialog = page.getByRole('dialog', { name: 'Create payee' });
    this.createHeading = this.createDialog.getByRole('heading', { name: 'Create payee' });
    this.editDialog = page.getByRole('dialog', { name: 'Edit payee' });
    this.editHeading = this.editDialog.getByRole('heading', { name: 'Edit payee' });
  }

  async goto(): Promise<void> {
    const loaded = this.page.waitForResponse(
      (resp) =>
        new URL(resp.url()).pathname === '/api/v1/finances/payees' &&
        resp.request().method() === 'GET' &&
        resp.status() === 200,
    );
    await this.page.goto('/finances/payees');
    await loaded;
  }

  /** Narrows the (shared, growing) list to payees whose name contains `term`. */
  @step()
  async search(term: string): Promise<void> {
    const searched = this.page.waitForResponse(
      (resp) =>
        new URL(resp.url()).pathname === '/api/v1/finances/payees' &&
        new URL(resp.url()).searchParams.get('Search') === term &&
        resp.status() === 200,
    );
    await this.searchInput.fill(term);
    await searched;
  }

  /** A payee's table row, matched on its exact name. */
  row(name: string): Locator {
    return this.page.getByRole('row').filter({ has: this.page.getByText(name, { exact: true }) });
  }

  @step()
  async openCreateDialog(): Promise<void> {
    await this.newPayeeButton.click();
    await expect(this.createHeading).toBeVisible();
  }

  @step()
  async create(name: string): Promise<void> {
    await this.openCreateDialog();
    await this.createDialog.getByLabel('Name', { exact: true }).fill(name);
    const created = this.page.waitForResponse(
      (resp) => resp.url().endsWith('/api/v1/finances/payees') && resp.request().method() === 'POST',
    );
    await this.createDialog.getByRole('button', { name: 'Create', exact: true }).click();
    await expectOk(await created);
    await expect(this.createHeading).toBeHidden();
  }

  /** Opens the row's ⋮ menu; resolves to the menu item `action` without clicking it. */
  @step()
  async rowAction(name: string, action: 'Edit' | 'Merge into…' | 'Delete'): Promise<Locator> {
    await this.row(name).getByRole('button', { name: 'Payee actions' }).click();
    const item = this.page.getByRole('menuitem', { name: action });
    await expect(item).toBeVisible();
    return item;
  }

  /** Edit → new name → Save, leaving the dialog open; resolves to the PATCH's HTTP status. */
  @step()
  async submitRename(name: string, newName: string): Promise<number> {
    await (await this.rowAction(name, 'Edit')).click();
    await expect(this.editHeading).toBeVisible();
    await this.editDialog.getByLabel('Name', { exact: true }).fill(newName);
    const saved = this.page.waitForResponse(
      (resp) => /\/api\/v1\/finances\/payees\/[0-9a-f-]+$/.test(resp.url()) && resp.request().method() === 'PATCH',
    );
    await this.editDialog.getByRole('button', { name: 'Save', exact: true }).click();
    return (await saved).status();
  }

  @step()
  async rename(name: string, newName: string): Promise<void> {
    expect(await this.submitRename(name, newName), `rename ${name}`).toBe(200);
    await expect(this.editHeading).toBeHidden();
  }

  @step()
  async delete(name: string): Promise<void> {
    await (await this.rowAction(name, 'Delete')).click();
    const confirm = this.page.getByRole('alertdialog', { name: `Delete ${name}?` });
    await expect(confirm.getByRole('heading', { name: `Delete ${name}?` })).toBeVisible();
    const deleted = this.page.waitForResponse(
      (resp) => /\/api\/v1\/finances\/payees\/[0-9a-f-]+$/.test(resp.url()) && resp.request().method() === 'DELETE',
    );
    await confirm.getByRole('button', { name: 'Delete', exact: true }).click();
    await expectOk(await deleted);
    await expect(confirm.getByRole('heading')).toBeHidden();
  }
}
