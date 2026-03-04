import { test as setup, expect } from '@playwright/test';
import { LOCAL_ADMIN, LOCAL_USER, LOCAL_INACTIVE_USER, AUTH_STATE } from '../e2e.config';

setup('seed admin user and save storageState', async ({ request }) => {
  // Register admin (first user becomes admin)
  await request.post('/api/auth/register', {
    data: {
      email: LOCAL_ADMIN.email,
      displayName: LOCAL_ADMIN.displayName,
      password: LOCAL_ADMIN.password,
    },
  });

  // Log in as admin
  await request.post('/api/auth/login?useCookies=true', {
    data: {
      email: LOCAL_ADMIN.email,
      password: LOCAL_ADMIN.password,
    },
  });

  // Create regular user via admin endpoint
  await request.post('/api/v1/admin/users', {
    data: {
      email: LOCAL_USER.email,
      displayName: LOCAL_USER.displayName,
      password: LOCAL_USER.password,
      role: 'Contributor',
    },
  });

  // Create inactive user via admin endpoint, then deactivate
  const createResponse = await request.post('/api/v1/admin/users', {
    data: {
      email: LOCAL_INACTIVE_USER.email,
      displayName: LOCAL_INACTIVE_USER.displayName,
      password: LOCAL_INACTIVE_USER.password,
      role: 'Contributor',
    },
  });
  expect(createResponse.ok()).toBeTruthy();
  const createdUser = await createResponse.json();

  // Deactivate the user
  const deactivateResponse = await request.put(`/api/v1/admin/users/${createdUser.id}`, {
    data: {
      email: LOCAL_INACTIVE_USER.email,
      displayName: LOCAL_INACTIVE_USER.displayName,
      role: 'Contributor',
      isActive: false,
    },
  });
  expect(deactivateResponse.ok()).toBeTruthy();

  // Verify the user is actually deactivated
  const verifyResponse = await request.get('/api/v1/admin/users?Page=1&PageSize=50');
  const users = await verifyResponse.json();
  const inactiveUser = users.items.find(
    (u: { email: string }) => u.email === LOCAL_INACTIVE_USER.email,
  );
  expect(inactiveUser.isActive).toBe(false);

  // Save admin storageState
  await request.storageState({ path: AUTH_STATE.adminPath });
});

setup('save regular user storageState', async ({ request }) => {
  // Log in as regular user
  await request.post('/api/auth/login?useCookies=true', {
    data: {
      email: LOCAL_USER.email,
      password: LOCAL_USER.password,
    },
  });

  // Save user storageState
  await request.storageState({ path: AUTH_STATE.userPath });
});

setup('save inactive user storageState', async ({ request }) => {
  // Log in as inactive user (login itself still works, they get a cookie)
  await request.post('/api/auth/login?useCookies=true', {
    data: {
      email: LOCAL_INACTIVE_USER.email,
      password: LOCAL_INACTIVE_USER.password,
    },
  });

  // Save inactive user storageState
  await request.storageState({ path: AUTH_STATE.inactivePath });
});
