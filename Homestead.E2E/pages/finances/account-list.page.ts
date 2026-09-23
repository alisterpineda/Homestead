import { type Locator, type Page, expect } from '@playwright/test';
import { step } from '../../helpers/step';
import type { AccountKind } from '../../helpers/api/finances';
import { fillDatePicker } from '../../helpers/playwright.helpers';

export const KIND_LABELS: Record<AccountKind, string> = {
  Cash: 'Cash',
  Investment: 'Investment',
  OtherAsset: 'Other Asset',
  CreditCard: 'Credit Card',
  Loan: 'Loan',
  OtherLiability: 'Other Liability',
};

export interface SubmitCreateInput {
  kind: AccountKind;
  name: string;
  currencyCode: string;
  institution?: string;
  initialAmount?: string;
  asOfDate?: string;
}

export class AccountListPage {
  readonly newAccountButton: Locator;
  readonly noAccountsEmpty: Locator;
  readonly createDialog: Locator;

  constructor(readonly page: Page) {
    this.newAccountButton = page.getByRole('button', { name: 'New account' });
    this.noAccountsEmpty = page.getByText('No accounts yet');
    this.createDialog = page.getByRole('dialog', { name: 'New account' });
  }

  async goto(): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/finances/accounts') &&
        !/\/finances\/accounts\/[0-9a-f-]/.test(resp.url()) &&
        resp.request().method() === 'GET' &&
        resp.status() === 200,
    );
    await this.page.goto('/finances/accounts');
    await responsePromise;
  }

  /** The account's table row (desktop) or card (phones) — whichever layout is on screen. */
  entryFor(name: string): Locator {
    return this.page
      .getByRole('row')
      .or(this.page.locator('app-account-card-grid').getByRole('link'))
      .filter({ hasText: name })
      .visible();
  }

  @step()
  async openCreateDialog(): Promise<void> {
    await this.newAccountButton.first().click();
    await expect(this.createDialog.getByRole('heading', { name: 'New account' })).toBeVisible();
  }

  @step()
  async submitCreate(input: SubmitCreateInput): Promise<void> {
    await this.createDialog.getByRole('radio', { name: KIND_LABELS[input.kind], exact: false }).click();

    await this.createDialog.getByLabel('Name', { exact: true }).fill(input.name);

    await this.createDialog.getByRole('button', { name: 'Reporting currency' }).click();
    await this.page.getByRole('textbox', { name: 'Search currencies' }).fill(input.currencyCode);
    await this.page
      .getByRole('button', { name: new RegExp(`^${input.currencyCode}\\b`) })
      .first()
      .click();

    if (input.institution !== undefined) {
      await this.createDialog.getByLabel('Institution (optional)').fill(input.institution);
    }

    if (input.initialAmount !== undefined && input.kind !== 'Investment') {
      await this.createDialog.getByLabel('Amount', { exact: true }).fill(input.initialAmount);
    }

    if (input.asOfDate !== undefined && input.kind !== 'Investment') {
      await fillDatePicker(this.createDialog, 'As-of date', input.asOfDate);
    }

    const createResponse = this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/finances/accounts') &&
        resp.request().method() === 'POST' &&
        resp.status() === 201,
    );
    await this.createDialog.getByRole('button', { name: 'Create account' }).click();
    await createResponse;
    await this.page.waitForURL(/\/finances\/accounts\/[0-9a-f-]+/);
  }
}
