import { test, expect } from '../../fixtures/app.fixture';
import { LOCAL_INACTIVE_USER, LOCAL_ADMIN } from '../../e2e.config';

test.describe('Inactive user flow', () => {
  test('inactive user sees pending activation page after login', async ({ page, loginPage }) => {
    await loginPage.goto();
    await loginPage.login(LOCAL_INACTIVE_USER.email, LOCAL_INACTIVE_USER.password);

    await page.waitForURL('**/pending-activation');
    await expect(page.getByRole('heading', { name: 'Account Pending' })).toBeVisible();
    await expect(page.getByText('pending activation')).toBeVisible();
  });

  test('inactive user cannot navigate to documents', async ({ page, loginPage }) => {
    await loginPage.goto();
    await loginPage.login(LOCAL_INACTIVE_USER.email, LOCAL_INACTIVE_USER.password);

    await page.waitForURL('**/pending-activation');

    // Try navigating to documents directly
    await page.goto('/documents');
    await page.waitForURL('**/pending-activation');
    await expect(page.getByRole('heading', { name: 'Account Pending' })).toBeVisible();
  });

  test('inactive user can sign out', async ({ page, loginPage }) => {
    await loginPage.goto();
    await loginPage.login(LOCAL_INACTIVE_USER.email, LOCAL_INACTIVE_USER.password);

    await page.waitForURL('**/pending-activation');

    await page.getByRole('button', { name: 'Sign out' }).click();
    await page.waitForURL('**/login');
  });

  test('activated user can access documents', async ({ page, request, loginPage }) => {
    // First, log in as admin and activate the user
    await request.post('/api/auth/login?useCookies=true', {
      data: {
        email: LOCAL_ADMIN.email,
        password: LOCAL_ADMIN.password,
      },
    });

    // Get the user list to find the inactive user's ID
    const usersResponse = await request.get('/api/v1/admin/users?Page=1&PageSize=50');
    const usersBody = await usersResponse.json();
    const inactiveUser = usersBody.items.find(
      (u: { email: string }) => u.email === LOCAL_INACTIVE_USER.email,
    );

    // Activate the user
    await request.put(`/api/v1/admin/users/${inactiveUser.id}`, {
      data: {
        email: LOCAL_INACTIVE_USER.email,
        displayName: LOCAL_INACTIVE_USER.displayName,
        isAdmin: false,
        isActive: true,
      },
    });

    // Now log in as the previously inactive user
    await loginPage.goto();
    await loginPage.login(LOCAL_INACTIVE_USER.email, LOCAL_INACTIVE_USER.password);

    await page.waitForURL('**/documents');
    await expect(page.getByRole('heading', { name: 'Documents' })).toBeVisible();
  });
});
