import { type Locator, type Page, expect } from '@playwright/test';
import { step } from '../../helpers/step';

/** `/finances/settings/general` — the household display currency. */
export class GeneralSettingsPage {
  readonly displayCurrencyPicker: Locator;
  readonly saveButton: Locator;

  constructor(readonly page: Page) {
    this.displayCurrencyPicker = page.getByRole('button', { name: 'Display currency' });
    this.saveButton = page.getByRole('main').getByRole('button', { name: 'Save', exact: true });
  }

  async goto(): Promise<void> {
    const loaded = this.page.waitForResponse(
      (resp) => new URL(resp.url()).pathname === '/api/v1/finances/settings' && resp.status() === 200,
    );
    await this.page.goto('/finances/settings/general');
    await loaded;
  }

  @step()
  async chooseDisplayCurrency(code: string): Promise<void> {
    await this.displayCurrencyPicker.click();
    await this.page.getByRole('textbox', { name: 'Search currencies' }).fill(code);
    await this.page.getByRole('button', { name: new RegExp(`^${code}\\b`) }).first().click();
    await expect(this.displayCurrencyPicker).toContainText(`${code} —`);
  }
}
