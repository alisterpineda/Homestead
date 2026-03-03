import { test, expect } from '../../fixtures/app.fixture';
import { LOCAL_USER } from '../../e2e.config';

test.describe('Local regular user login', () => {
  test('can log in as regular user', async ({ page, loginPage }) => {
    await loginPage.goto();
    await loginPage.login(LOCAL_USER.email, LOCAL_USER.password);

    await page.waitForURL('**/documents');
    await expect(page.getByRole('heading', { name: 'Documents' })).toBeVisible();
  });

  test('cannot access admin section', async ({ page, loginPage }) => {
    await loginPage.goto();
    await loginPage.login(LOCAL_USER.email, LOCAL_USER.password);

    await page.waitForURL('**/documents');
    await page.goto('/admin');
    await expect(page).toHaveURL(/\/documents/);
  });
});
