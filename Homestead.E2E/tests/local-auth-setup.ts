import { test as setup } from '@playwright/test';
import { LOCAL_ADMIN, LOCAL_USER, AUTH_STATE } from '../e2e.config';

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
      isAdmin: false,
    },
  });

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
