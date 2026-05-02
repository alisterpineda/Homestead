export const KEYCLOAK_ADMIN = { email: 'admin@test.com', password: 'admin123' };
export const KEYCLOAK_USER = { email: 'user@test.com', password: 'user123' };

export const KEYCLOAK_URL_PATTERN = /.*localhost:8888.*/;

export const AUTH_STATE = {
  adminPath: '.auth/admin.json',
  userPath: '.auth/user.json',
};

export const TEST_DATA = {
  documents: {
    tags: {
      finance: 'e2e-finance',
      invoices: 'e2e-finance/invoices',
      contracts: 'e2e-contracts',
      bulkTag1: 'e2e-bulk-tag-1',
      bulkTag2: 'e2e-bulk-tag-2',
    },
    parties: {
      acmeCorp: 'Acme Corporation',
      johnDoe: 'John Doe',
    },
    files: {
      pdfName: 'E2E Test Document',
      imageName: 'E2E Test Image',
      searchableText: 'Invoice #12345',
    },
  },
};
