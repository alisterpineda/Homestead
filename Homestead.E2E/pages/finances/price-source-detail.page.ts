import { type Locator, type Page, expect } from '@playwright/test';
import { step } from '../../helpers/step';
import { expectOk } from '../../helpers/playwright.helpers';

/** `/finances/settings/price-sources/:source` — one provider's status, watch list and schedule. */
export class PriceSourceDetailPage {
  readonly watchInput: Locator;
  readonly addWatchButton: Locator;
  /** The watch list's own Save (the schedule section has "Save schedule"). */
  readonly saveWatchListButton: Locator;

  constructor(readonly page: Page) {
    this.watchInput = page.getByRole('textbox', { name: 'Symbol to watch' });
    this.addWatchButton = page.getByRole('button', { name: 'Add', exact: true });
    this.saveWatchListButton = page.getByRole('button', { name: 'Save', exact: true });
  }

  heading(): Locator {
    return this.page.getByRole('heading', { level: 2 });
  }

  /** A watched symbol's chip, found through its remove button. */
  removeButton(symbol: string): Locator {
    return this.page.getByRole('button', { name: `Remove ${symbol}`, exact: true });
  }

  @step()
  async watch(symbol: string): Promise<void> {
    await this.watchInput.fill(symbol);
    await this.addWatchButton.click();
    await expect(this.removeButton(symbol)).toBeVisible();
  }

  @step()
  async saveWatchList(): Promise<void> {
    const saved = this.page.waitForResponse(
      (resp) => /\/finances\/settings\/price-sources\/\w+$/.test(resp.url()) && resp.request().method() === 'PATCH',
    );
    await this.saveWatchListButton.click();
    await expectOk(await saved);
    await expect(this.saveWatchListButton).toBeDisabled();
  }
}
