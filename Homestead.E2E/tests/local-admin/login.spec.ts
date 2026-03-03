import { test, expect } from '../../fixtures/app.fixture';
import { LOCAL_ADMIN } from '../../e2e.config';

test.describe('Local admin login', () => {
  test('can log in as admin', async ({ page, loginPage }) => {
    await loginPage.goto();
    await loginPage.login(LOCAL_ADMIN.email, LOCAL_ADMIN.password);

    await page.waitForURL('**/documents');
    await expect(page.getByRole('heading', { name: 'Documents' })).toBeVisible();
  });

  test('can access admin section', async ({ page, loginPage }) => {
    await loginPage.goto();
    await loginPage.login(LOCAL_ADMIN.email, LOCAL_ADMIN.password);

    await page.waitForURL('**/documents');
    await page.goto('/admin');
    await expect(page).toHaveURL(/\/admin/);
  });
});
