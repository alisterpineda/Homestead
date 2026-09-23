import { test, expect } from '../../fixtures/base.fixture';
import { KEY_PAGES } from '../../helpers/key-pages';

test.describe('Layout', () => {
  for (const { name, open, ready } of KEY_PAGES) {
    // Wide tables and long names must scroll inside their own container; the page itself never
    // scrolls sideways — the failure mode behind clipped tables on phones.
    test(`${name} fits the viewport width`, async ({ page, request, uid }) => {
      await page.goto(await open(request, uid));
      await ready(page, uid).waitFor();

      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
      expect(overflow, 'horizontal page overflow in px').toBeLessThanOrEqual(0);
    });
  }

  test('the Documents sidebar lists its sections in order', async ({ page, isMobile }) => {
    test.skip(isMobile, 'On phones the sidebar is an off-canvas sheet');
    await page.goto('/documents');
    // Partial match: only the module's own navigation, in order, has to be present.
    await expect(page.locator('body')).toMatchAriaSnapshot(`
      - list:
        - listitem:
          - button "Switch module": Documents
      - list:
        - listitem:
          - link "Documents"
      - text: Manage
      - list:
        - listitem:
          - link "Parties"
        - listitem:
          - link "Tags"
      - list:
        - listitem:
          - link "Trash"
    `);
  });
});
