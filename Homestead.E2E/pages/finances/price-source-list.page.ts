import type { Locator, Page } from '@playwright/test';
import { step } from '../../helpers/step';

/** `/finances/settings/price-sources` — the automated price providers (admin only). */
export class PriceSourceListPage {
  constructor(readonly page: Page) {}

  async goto(): Promise<void> {
    const loaded = this.page.waitForResponse(
      (resp) => new URL(resp.url()).pathname.endsWith('/finances/settings/price-sources') && resp.status() === 200,
    );
    await this.page.goto('/finances/settings/price-sources');
    await loaded;
  }

  row(displayName: string): Locator {
    return this.page.getByRole('row').filter({ has: this.page.getByText(displayName, { exact: true }) });
  }

  /** Clicks a source's row, which opens its detail page. */
  @step()
  async open(displayName: string): Promise<void> {
    const loaded = this.page.waitForResponse(
      (resp) => /\/finances\/settings\/price-sources\/\w+$/.test(new URL(resp.url()).pathname) && resp.status() === 200,
    );
    await this.row(displayName).click();
    await loaded;
  }
}
