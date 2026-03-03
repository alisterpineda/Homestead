import { test, expect } from '../../fixtures/app.fixture';

test.describe('Local-only auth mode (fresh database)', () => {
  test('redirects to setup page', async ({ page }) => {
    await page.goto('/');
    await page.waitForURL('**/setup');
  });

  test('setup page renders correctly', async ({ setupPage }) => {
    await setupPage.goto();

    await expect(setupPage.heading).toBeVisible();
    await expect(setupPage.description).toBeVisible();
    await expect(setupPage.emailInput).toBeVisible();
    await expect(setupPage.displayNameInput).toBeVisible();
    await expect(setupPage.passwordInput).toBeVisible();
    await expect(setupPage.confirmPasswordInput).toBeVisible();
    await expect(setupPage.submitButton).toBeVisible();
  });

  test('shows validation errors on empty submit', async ({ setupPage }) => {
    await setupPage.goto();
    await setupPage.submit();

    await expect(setupPage.page.getByText('Email is required.')).toBeVisible();
    await expect(setupPage.page.getByText('Display name is required.')).toBeVisible();
    await expect(setupPage.page.getByText('Password is required.')).toBeVisible();
    await expect(setupPage.page.getByText('Confirm your password.')).toBeVisible();
  });

  test('API returns correct status', async ({ request }) => {
    const response = await request.get('/api/status');
    expect(response.ok()).toBeTruthy();

    const body = await response.json();
    expect(body.needsSetup).toBe(true);
    expect(body.oidc).toBeNull();
  });
});
