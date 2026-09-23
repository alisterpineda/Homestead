import { type Locator, type Page, expect } from '@playwright/test';
import { DISPLAY_CURRENCY } from '../../e2e.config';
import { step } from '../../helpers/step';
import { escapeRegExp, fillDatePicker } from '../../helpers/playwright.helpers';
import type { FlowDirection } from '../../helpers/api/finances';
import { FlowSheet } from './flow-sheet';
import { StatementImportDialog } from './statement-import-dialog';

/** An amount as the account balance shows it: display-currency symbol, two decimals (e.g. CA$1,000.00). */
export function formatBalance(value: number): string {
  return new Intl.NumberFormat('en-US', { style: 'currency', currency: DISPLAY_CURRENCY }).format(value);
}

export interface RecordBalanceInput {
  amount: string;
  date: string;
  memo?: string;
}

export interface StartTransferInput {
  destinationName: string;
  amount: string;
  date: string;
  memo?: string;
}

export class AccountDetailPage {
  readonly editButton: Locator;
  readonly newTransactionButton: Locator;
  readonly flowSheet: FlowSheet;
  readonly statementImport: StatementImportDialog;

  constructor(readonly page: Page) {
    this.editButton = page.getByRole('button', { name: 'Edit', exact: true });
    this.newTransactionButton = page.getByRole('button', { name: 'New transaction' });
    this.flowSheet = new FlowSheet(page);
    this.statementImport = new StatementImportDialog(page);
  }

  async gotoById(id: string): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) =>
        resp.url().includes(`/api/v1/finances/accounts/${id}`) &&
        resp.request().method() === 'GET' &&
        resp.status() === 200,
    );
    await this.page.goto(`/finances/accounts/${id}`);
    await responsePromise;
    await expect(this.editButton).toBeVisible();
  }

  balanceText(): Locator {
    return this.page.getByTestId('account-balance');
  }

  nameHeading(): Locator {
    return this.page.getByRole('main').getByRole('heading', { level: 1 });
  }

  activityRows(): Locator {
    return this.page.locator('app-account-activity-tab tbody tr');
  }

  /** The Activity row whose payee or memo includes `text`. */
  activityRow(text: string): Locator {
    return this.activityRows().filter({ hasText: text });
  }

  /** New transaction → Expense / Income; resolves once the sheet is open. */
  @step()
  async openNewFlow(direction: FlowDirection): Promise<FlowSheet> {
    await this.newTransactionButton.click();
    await this.page.getByRole('menuitem', { name: direction, exact: true }).click();
    await expect(this.flowSheet.heading).toHaveText(`New ${direction.toLowerCase()}`);
    return this.flowSheet;
  }

  /** Clicks a flow's Activity row, which opens it in the Expense / Income sheet for editing. */
  @step()
  async openFlow(text: string): Promise<FlowSheet> {
    await this.activityRow(text).click();
    await expect(this.flowSheet.heading).toHaveText(/^Edit (expense|income)$/);
    return this.flowSheet;
  }

  @step()
  async recordBalance(input: RecordBalanceInput): Promise<void> {
    await this.newTransactionButton.click();
    await this.page.getByRole('menuitem', { name: 'Record balance' }).click();

    const sheet = this.page.getByRole('dialog', { name: 'Record balance' });
    // The dialog role sits on a zero-size CDK container; its heading is what actually renders.
    await expect(sheet.getByRole('heading', { name: 'Record balance' })).toBeVisible();

    await sheet.getByLabel('Amount', { exact: true }).fill(input.amount);
    await fillDatePicker(sheet, 'As-of date', input.date);
    if (input.memo !== undefined) {
      await sheet.getByLabel('Memo (optional)').fill(input.memo);
    }

    const responsePromise = this.page.waitForResponse(
      (resp) =>
        /\/api\/v1\/finances\/accounts\/[0-9a-f-]+\/balance-entries/.test(resp.url()) &&
        resp.request().method() === 'POST' &&
        resp.status() === 201,
    );
    await sheet.getByRole('button', { name: 'Record balance' }).click();
    await responsePromise;
  }

  @step()
  async startTransfer(input: StartTransferInput): Promise<void> {
    await this.newTransactionButton.click();
    await this.page.getByRole('menuitem', { name: 'Transfer' }).click();

    const sheet = this.page.getByRole('dialog', { name: 'New transfer' });
    // The dialog role sits on a zero-size CDK container; its heading is what actually renders.
    await expect(sheet.getByRole('heading', { name: 'New transfer' })).toBeVisible();

    // Direction defaults to outgoing — destination account picker
    await sheet.getByRole('button', { name: 'Destination account' }).click();
    await this.page.getByRole('textbox', { name: 'Search accounts' }).fill(input.destinationName);
    await this.page
      .getByRole('button', { name: new RegExp(escapeRegExp(input.destinationName)) })
      .first()
      .click();

    await sheet.getByLabel(/Amount sent/).fill(input.amount);
    await fillDatePicker(sheet, 'Date', input.date, { exact: true });
    if (input.memo !== undefined) {
      await sheet.getByLabel('Memo (optional)').fill(input.memo);
    }

    const responsePromise = this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/finances/transactions/transfers') &&
        resp.request().method() === 'POST' &&
        resp.status() === 201,
    );
    await sheet.getByRole('button', { name: 'Create transfer' }).click();
    await responsePromise;
  }
}
