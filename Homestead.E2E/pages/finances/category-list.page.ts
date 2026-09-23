import { type Locator, type Page, expect } from '@playwright/test';
import { step } from '../../helpers/step';
import { expectOk } from '../../helpers/playwright.helpers';
import type { FlowDirection } from '../../helpers/api/finances';

/** `/finances/categories` — the nested Income / Expense category tree. */
export class CategoryListPage {
  readonly newCategoryButton: Locator;
  /** The dialog role sits on a zero-size CDK container; assert on the heading instead. */
  readonly createDialog: Locator;
  readonly createHeading: Locator;
  readonly editSheet: Locator;
  readonly editHeading: Locator;

  constructor(readonly page: Page) {
    this.newCategoryButton = page.getByRole('button', { name: 'New category' });
    this.createDialog = page.getByRole('dialog', { name: 'New category' });
    this.createHeading = this.createDialog.getByRole('heading', { name: 'New category' });
    this.editSheet = page.getByRole('dialog', { name: 'Edit category' });
    this.editHeading = this.editSheet.getByRole('heading', { name: 'Edit category' });
  }

  async goto(): Promise<void> {
    const loaded = this.page.waitForResponse(
      (resp) =>
        new URL(resp.url()).pathname === '/api/v1/finances/categories' &&
        resp.request().method() === 'GET' &&
        resp.status() === 200,
    );
    await this.page.goto('/finances/categories');
    await loaded;
  }

  /**
   * A category's row in the tree, matched on its exact name. The tree has no row/treeitem roles,
   * so the node component is the scope.
   */
  node(name: string): Locator {
    return this.page.locator('app-category-tree-node > div').filter({ has: this.page.getByText(name, { exact: true }) });
  }

  @step()
  async openCreateDialog(): Promise<void> {
    await this.newCategoryButton.click();
    await expect(this.createHeading).toBeVisible();
  }

  /** Fills the open create dialog and submits it; the caller asserts on the outcome. */
  @step()
  async submitCreate(input: { type: FlowDirection; name: string }): Promise<void> {
    await this.createDialog.getByRole('radio', { name: new RegExp(`^${input.type}\\b`) }).click();
    await this.createDialog.getByLabel('Name', { exact: true }).fill(input.name);
    await this.createDialog.getByRole('button', { name: 'Create category' }).click();
  }

  @step()
  async create(input: { type: FlowDirection; name: string }): Promise<void> {
    await this.openCreateDialog();
    const created = this.page.waitForResponse(
      (resp) => resp.url().endsWith('/api/v1/finances/categories') && resp.request().method() === 'POST',
    );
    await this.submitCreate(input);
    await expectOk(await created);
    await expect(this.createHeading).toBeHidden();
  }

  @step()
  private async openRowAction(name: string, action: 'Edit' | 'Delete'): Promise<void> {
    await this.node(name).getByRole('button', { name: 'Category actions' }).click();
    await this.page.getByRole('menuitem', { name: action }).click();
  }

  @step()
  async rename(name: string, newName: string): Promise<void> {
    await this.openRowAction(name, 'Edit');
    await expect(this.editHeading).toBeVisible();
    await this.editSheet.getByLabel('Name', { exact: true }).fill(newName);
    const saved = this.page.waitForResponse(
      (resp) => /\/api\/v1\/finances\/categories\/[0-9a-f-]+$/.test(resp.url()) && resp.request().method() === 'PATCH',
    );
    await this.editSheet.getByRole('button', { name: 'Save', exact: true }).click();
    await expectOk(await saved);
    await expect(this.editHeading).toBeHidden();
  }

  @step()
  async delete(name: string): Promise<void> {
    await this.openRowAction(name, 'Delete');
    const confirm = this.page.getByRole('alertdialog', { name: `Delete ${name}?` });
    await expect(confirm.getByRole('heading', { name: `Delete ${name}?` })).toBeVisible();
    const deleted = this.page.waitForResponse(
      (resp) => /\/api\/v1\/finances\/categories\/[0-9a-f-]+$/.test(resp.url()) && resp.request().method() === 'DELETE',
    );
    await confirm.getByRole('button', { name: 'Delete', exact: true }).click();
    await expectOk(await deleted);
    await expect(confirm.getByRole('heading')).toBeHidden();
  }
}
