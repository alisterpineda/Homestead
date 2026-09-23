import { type Locator, type Page, expect } from '@playwright/test';
import { step } from '../../helpers/step';
import { expectOk } from '../../helpers/playwright.helpers';

/** `/finances/commodities/:id` — a commodity's header and its price history. */
export class CommodityDetailPage {
  readonly newPriceButton: Locator;
  /** The dialog role sits on a zero-size CDK container; assert on the heading instead. */
  readonly newPriceDialog: Locator;
  readonly newPriceHeading: Locator;

  constructor(readonly page: Page) {
    this.newPriceButton = page.getByRole('button', { name: 'New manual price' });
    this.newPriceDialog = page.getByRole('dialog', { name: 'New manual price' });
    this.newPriceHeading = this.newPriceDialog.getByRole('heading', { name: 'New manual price' });
  }

  async gotoById(id: string): Promise<void> {
    const loaded = this.page.waitForResponse(
      (resp) => resp.url().includes(`/api/v1/finances/commodities/${id}`) && resp.status() === 200,
    );
    await this.page.goto(`/finances/commodities/${id}`);
    await loaded;
  }

  symbolHeading(): Locator {
    return this.page.getByRole('main').getByRole('heading', { level: 1 });
  }

  /** The commodity's header card: symbol, exchange / kind badges and name. */
  header(): Locator {
    return this.page.locator('app-commodity-detail-page header');
  }

  priceRows(): Locator {
    return this.page.locator('app-commodity-prices-table tbody tr');
  }

  /** Records a manual price for today in `quoteCurrency` (e.g. `CAD`). */
  @step()
  async addManualPrice(quoteCurrency: string, rate: string): Promise<void> {
    await this.newPriceButton.click();
    await expect(this.newPriceHeading).toBeVisible();

    await this.newPriceDialog.getByRole('button', { name: 'Quote currency' }).click();
    await this.page.getByRole('textbox', { name: 'Search currencies' }).fill(quoteCurrency);
    await this.page.getByRole('button', { name: new RegExp(`^${quoteCurrency}\\b`) }).first().click();
    await this.newPriceDialog.getByLabel('Rate', { exact: true }).fill(rate);

    const saved = this.page.waitForResponse(
      (resp) => resp.url().endsWith('/api/v1/finances/commodity-prices') && resp.request().method() === 'POST',
    );
    await this.newPriceDialog.getByRole('button', { name: 'Save price' }).click();
    await expectOk(await saved);
    await expect(this.newPriceHeading).toBeHidden();
  }
}
