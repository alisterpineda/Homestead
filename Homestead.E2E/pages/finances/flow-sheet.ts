import { type Locator, type Page, expect } from '@playwright/test';
import { step } from '../../helpers/step';
import { expectOk, fillDatePicker } from '../../helpers/playwright.helpers';

export interface FillFlowInput {
  amount?: string;
  date?: string;
  description?: string;
}

/**
 * The Expense / Income sheet (`app-flow-sheet`) on an account's detail page — one editor for both
 * directions, opened from "New transaction" (create) or by clicking a flow's Activity row (edit).
 */
export class FlowSheet {
  /** The dialog role sits on a zero-size CDK container; assert on {@link heading} instead. */
  readonly dialog: Locator;
  readonly heading: Locator;
  readonly payeeInput: Locator;
  readonly categoryTrigger: Locator;
  readonly amountInput: Locator;
  readonly descriptionInput: Locator;
  readonly submitButton: Locator;
  readonly deleteButton: Locator;

  constructor(readonly page: Page) {
    this.dialog = page.getByRole('dialog', { name: /^(New|Edit) (expense|income)$/ });
    this.heading = this.dialog.getByRole('heading', { name: /^(New|Edit) (expense|income)$/ });
    this.payeeInput = this.dialog.getByRole('combobox', { name: 'Select payee' });
    this.categoryTrigger = this.dialog.getByRole('button', { name: 'Select category' });
    this.amountInput = this.dialog.getByRole('textbox', { name: /^Amount/ });
    this.descriptionInput = this.dialog.getByLabel('Description (optional)');
    this.submitButton = this.dialog.getByRole('button', { name: /^(Create expense|Create income|Save changes)$/ });
    this.deleteButton = this.dialog.getByRole('button', { name: 'Delete', exact: true });
  }

  /** Picks an existing payee through the picker's server search. */
  @step()
  async pickPayee(name: string): Promise<void> {
    await this.payeeInput.fill(name);
    await this.page.getByRole('option', { name, exact: true }).click();
    await expect(this.payeeInput).toHaveValue(name);
  }

  @step()
  async pickCategory(name: string): Promise<void> {
    await this.categoryTrigger.click();
    await this.page.getByRole('textbox', { name: 'Search categories' }).fill(name);
    await this.page.getByRole('button', { name, exact: true }).click();
    await expect(this.categoryTrigger).toContainText(name);
  }

  @step()
  async fill(input: FillFlowInput): Promise<void> {
    if (input.amount !== undefined) {
      await this.amountInput.fill(input.amount);
    }
    if (input.date !== undefined) {
      await fillDatePicker(this.dialog, 'Date', input.date, { exact: true });
    }
    if (input.description !== undefined) {
      await this.descriptionInput.fill(input.description);
    }
  }

  /** Saves (create or edit) and waits for the sheet to close on success. */
  @step()
  async submit(): Promise<void> {
    const saved = this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/finances/transactions/flows') &&
        ['POST', 'PATCH'].includes(resp.request().method()),
    );
    await this.submitButton.click();
    await expectOk(await saved);
    await expect(this.heading).toBeHidden();
  }

  /** Deletes the flow being edited — the sheet's Delete button acts straight away, with no confirm. */
  @step()
  async delete(): Promise<void> {
    const deleted = this.page.waitForResponse(
      (resp) =>
        /\/api\/v1\/finances\/transactions\/[0-9a-f-]+$/.test(resp.url()) && resp.request().method() === 'DELETE',
    );
    await this.deleteButton.click();
    await expectOk(await deleted);
    await expect(this.heading).toBeHidden();
  }
}
