export const LOCAL_ADMIN = {
  email: 'admin@test.com',
  displayName: 'Admin User',
  password: 'Password123!',
};

export const LOCAL_USER = {
  email: 'user@test.com',
  displayName: 'Regular User',
  password: 'Password123!',
};

export const LOCAL_INACTIVE_USER = {
  email: 'inactive@test.com',
  displayName: 'Inactive User',
  password: 'Password123!',
};

export const KEYCLOAK_ADMIN = { email: 'admin@test.com', password: 'admin123' };
export const KEYCLOAK_USER = { email: 'user@test.com', password: 'user123' };

export const KEYCLOAK_URL_PATTERN = /.*localhost:8888.*/;

export const AUTH_STATE = {
  adminPath: '.auth/admin.json',
  userPath: '.auth/user.json',
  inactivePath: '.auth/inactive.json',
};
