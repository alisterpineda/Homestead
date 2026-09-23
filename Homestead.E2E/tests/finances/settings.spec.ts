import { test, expect } from '../../fixtures/finances.fixture';
import { DISPLAY_CURRENCY, LOCKS } from '../../e2e.config';
import {
  createSecurityViaApi,
  findCurrencyId,
  getDisplayCurrencyIdViaApi,
  getPriceSourceWatchListViaApi,
} from '../../helpers/api/finances';
import { stubFailure } from '../../helpers/network';

test.describe('Finances — settings', () => {
  test.describe('display currency', () => {
    test('a failed save reports the error and leaves the display currency unchanged', async ({
      page,
      request,
      generalSettingsPage,
    }) => {
      // Stubbed before the page loads, so the household-wide setting is never actually written.
      await stubFailure(page, /\/api\/v1\/finances\/settings$/, { method: 'PATCH' });
      await generalSettingsPage.goto();
      await expect(generalSettingsPage.displayCurrencyPicker).toContainText(`${DISPLAY_CURRENCY} —`);
      await expect(generalSettingsPage.saveButton).toBeDisabled();

      await generalSettingsPage.chooseDisplayCurrency('USD');
      await generalSettingsPage.saveButton.click();

      await expect(page.getByText('An unexpected server error occurred. Please try again later.')).toBeVisible();
      await expect(generalSettingsPage.saveButton).toBeEnabled();

      await generalSettingsPage.goto();
      await expect(generalSettingsPage.displayCurrencyPicker).toContainText(`${DISPLAY_CURRENCY} —`);
      expect(await getDisplayCurrencyIdViaApi(request)).toBe(await findCurrencyId(request, DISPLAY_CURRENCY));
    });
  });

  // A price source's watch list is household-wide; the test restores it before letting go of the lock.
  test.describe('price sources', { lock: LOCKS.financesSettings }, () => {
    test('a watched symbol must be a known commodity, persists once saved, and can be removed again', async ({
      page,
      request,
      priceSourceListPage,
      priceSourceDetailPage,
      uid,
    }) => {
      const original = await getPriceSourceWatchListViaApi(request, 'AlphaVantage');
      const security = await createSecurityViaApi(request, {
        symbol: `W${uid.toUpperCase()}`,
        exchange: 'TSX',
        name: `Watched ETF ${uid}`,
      });
      const unknown = `X${uid.toUpperCase()}`;

      await priceSourceListPage.goto();
      // External providers are deployment config, and the test stack configures none.
      await expect(priceSourceListPage.row('Alpha Vantage')).toContainText('External');
      await expect(priceSourceListPage.row('Alpha Vantage')).toContainText('Disabled');
      await expect(priceSourceListPage.row('Manual')).toContainText('System');

      await priceSourceListPage.open('Alpha Vantage');
      await expect(priceSourceDetailPage.heading()).toHaveText('Alpha Vantage');
      await expect(page.getByRole('button', { name: 'Run now' })).toBeDisabled();

      await priceSourceDetailPage.watch(unknown);
      await priceSourceDetailPage.saveWatchListButton.click();
      await expect(page.getByText(`Pair allow-list contains unknown symbols: ${unknown}.`)).toBeVisible();
      expect(await getPriceSourceWatchListViaApi(request, 'AlphaVantage')).toEqual(original);

      await priceSourceDetailPage.removeButton(unknown).click();
      await priceSourceDetailPage.watch(security.symbol);
      await priceSourceDetailPage.saveWatchList();
      await page.reload();
      await expect(priceSourceDetailPage.removeButton(security.symbol)).toBeVisible();
      expect(await getPriceSourceWatchListViaApi(request, 'AlphaVantage')).toContain(security.symbol);

      await priceSourceDetailPage.removeButton(security.symbol).click();
      await expect(priceSourceDetailPage.removeButton(security.symbol)).toHaveCount(0);
      await priceSourceDetailPage.saveWatchList();
      expect(await getPriceSourceWatchListViaApi(request, 'AlphaVantage')).toEqual(original);
    });
  });
});
