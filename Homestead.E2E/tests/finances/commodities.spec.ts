import { test, expect } from '../../fixtures/finances.fixture';
import { DISPLAY_CURRENCY } from '../../e2e.config';

test.describe('Finances — commodities', () => {
  test('creates a security, opens it, and records a manual price', async ({
    commodityListPage,
    commodityDetailPage,
    uid,
  }) => {
    const security = { symbol: `E${uid.toUpperCase()}`, exchange: 'TSX', name: `E2E Equity ETF ${uid}` };

    await commodityListPage.goto();
    await commodityListPage.create(security);
    await commodityListPage.search(security.symbol);
    await expect(commodityListPage.row(security.symbol)).toContainText(security.name);

    await commodityListPage.open(security.symbol);
    await expect(commodityDetailPage.symbolHeading()).toHaveText(security.symbol);
    await expect(commodityDetailPage.header()).toContainText(security.exchange);
    await expect(commodityDetailPage.header()).toContainText('Security');
    await expect(commodityDetailPage.header()).toContainText(security.name);
    await expect(commodityDetailPage.priceRows()).toHaveCount(0);

    await commodityDetailPage.addManualPrice(DISPLAY_CURRENCY, '25.50');

    await expect(commodityDetailPage.priceRows()).toHaveCount(1);
    await expect(commodityDetailPage.priceRows()).toContainText(DISPLAY_CURRENCY);
    await expect(commodityDetailPage.priceRows()).toContainText('Manual');
    await expect(commodityDetailPage.priceRows()).toContainText('25.5');
  });
});
