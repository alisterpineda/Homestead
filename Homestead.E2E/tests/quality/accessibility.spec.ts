import { test } from '../../fixtures/base.fixture';
import { expectNoA11yViolations } from '../../helpers/a11y';
import { KEY_PAGES } from '../../helpers/key-pages';

test.describe('Accessibility (WCAG 2.1 AA)', { tag: '@a11y' }, () => {
  for (const { name, open, ready, known } of KEY_PAGES) {
    test(`${name} has no accessibility violations`, async ({ page, request, makeAxeBuilder, isMobile, uid }) => {
      await page.goto(await open(request, uid));
      await ready(page, uid).waitFor();
      await expectNoA11yViolations(makeAxeBuilder(), { known, isMobile });
    });
  }
});
