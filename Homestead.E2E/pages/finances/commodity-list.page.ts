import { type Locator, type Page, expect } from '@playwright/test';
import { step } from '../../helpers/step';
import { expectOk } from '../../helpers/playwright.helpers';

export interface CreateSecurityInput {
  symbol: string;
  exchange: string;
  name: string;
}

/** `/finances/commodities` — securities (by default) and the seeded currencies. */
export class CommodityListPage {
  readonly newSecurityButton: Locator;
  readonly searchInput: Locator;
  /** The dialog role sits on a zero-size CDK container; assert on the heading instead. */
  readonly createDialog: Locator;
  readonly createHeading: Locator;

  constructor(readonly page: Page) {
    this.newSecurityButton = page.getByRole('button', { name: 'New security' });
    this.searchInput = page.getByRole('textbox', { name: 'Search commodities' });
    this.createDialog = page.getByRole('dialog', { name: 'New security' });
    this.createHeading = this.createDialog.getByRole('heading', { name: 'New security' });
  }

  async goto(): Promise<void> {
    const loaded = this.page.waitForResponse(
      (resp) =>
        new URL(resp.url()).pathname === '/api/v1/finances/commodities' &&
        resp.request().method() === 'GET' &&
        resp.status() === 200,
    );
    await this.page.goto('/finances/commodities');
    await loaded;
  }

  @step()
  async search(term: string): Promise<void> {
    const searched = this.page.waitForResponse(
      (resp) =>
        new URL(resp.url()).pathname === '/api/v1/finances/commodities' &&
        new URL(resp.url()).searchParams.get('Search') === term &&
        resp.status() === 200,
    );
    await this.searchInput.fill(term);
    await searched;
  }

  row(symbol: string): Locator {
    return this.page.getByRole('row').filter({ has: this.page.getByText(symbol, { exact: true }) });
  }

  @step()
  async openCreateDialog(): Promise<void> {
    await this.newSecurityButton.click();
    await expect(this.createHeading).toBeVisible();
  }

  /** Fills the open dialog and clicks Create; resolves to the POST's HTTP status. */
  @step()
  async submitCreate(input: CreateSecurityInput): Promise<number> {
    await this.createDialog.getByLabel('Symbol', { exact: true }).fill(input.symbol);
    await this.createDialog.getByLabel('Exchange', { exact: true }).fill(input.exchange);
    await this.createDialog.getByLabel('Name', { exact: true }).fill(input.name);
    const created = this.page.waitForResponse(
      (resp) => resp.url().endsWith('/api/v1/finances/commodities') && resp.request().method() === 'POST',
    );
    await this.createDialog.getByRole('button', { name: 'Create security' }).click();
    return (await created).status();
  }

  @step()
  async create(input: CreateSecurityInput): Promise<void> {
    await this.openCreateDialog();
    expect(await this.submitCreate(input), `create security ${input.symbol}`).toBe(201);
    await expect(this.createHeading).toBeHidden();
  }

  /** Clicks the commodity's row, which opens its detail page. */
  @step()
  async open(symbol: string): Promise<void> {
    const loaded = this.page.waitForResponse(
      (resp) => /\/api\/v1\/finances\/commodities\/[0-9a-f-]+$/.test(resp.url()) && resp.request().method() === 'GET',
    );
    await this.row(symbol).click();
    await expectOk(await loaded);
    await expect(this.page).toHaveURL(/\/finances\/commodities\/[0-9a-f-]+$/);
  }
}
