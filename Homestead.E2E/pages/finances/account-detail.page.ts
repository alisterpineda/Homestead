import { type Locator, type Page, expect } from '@playwright/test';

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

  constructor(readonly page: Page) {
    this.editButton = page.getByRole('button', { name: 'Edit', exact: true });
    this.newTransactionButton = page.getByRole('button', { name: 'New transaction' });
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
    return this.page.locator('header p.text-3xl');
  }

  nameHeading(): Locator {
    return this.page.locator('header h1').first();
  }

  activityRows(): Locator {
    return this.page.locator('app-account-activity-tab tbody tr');
  }

  async recordBalance(input: RecordBalanceInput): Promise<void> {
    await this.newTransactionButton.click();
    await this.page.getByRole('menuitem', { name: 'Record balance' }).click();

    // The Spartan sheet is a cdk-dialog-container; the dropdown menu is too,
    // so target the sheet specifically via its semantic structure.
    const sheet = this.page.locator('hlm-sheet-content').filter({
      has: this.page.getByRole('heading', { name: 'Record balance' }),
    });
    await expect(sheet).toBeVisible();

    await sheet.getByLabel('Amount', { exact: true }).fill(input.amount);
    await sheet.getByLabel('As-of date').fill(input.date);
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

  async startTransfer(input: StartTransferInput): Promise<void> {
    await this.newTransactionButton.click();
    await this.page.getByRole('menuitem', { name: 'Transfer' }).click();

    const sheet = this.page.locator('hlm-sheet-content').filter({
      has: this.page.getByRole('heading', { name: 'New transfer' }),
    });
    await expect(sheet).toBeVisible();

    // Direction defaults to outgoing — destination account picker
    await sheet.getByRole('button', { name: 'Destination account' }).click();
    await this.page.getByRole('textbox', { name: 'Search accounts' }).fill(input.destinationName);
    await this.page
      .getByRole('button', { name: new RegExp(input.destinationName.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')) })
      .first()
      .click();

    await sheet.getByLabel(/Amount sent/).fill(input.amount);
    await sheet.getByLabel('Date', { exact: true }).fill(input.date);
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
