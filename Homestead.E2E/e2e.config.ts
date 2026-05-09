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
      pdfName: 'IRS W-9 Form',
      imageName: 'E2E Test Image',
      searchableText: 'Taxpayer',
    },
  },
  finances: {
    currency: 'CAD',
    accounts: {
      chequing: 'E2E Chequing',
      savings: 'E2E Savings',
      visa: 'E2E Visa',
    },
    amounts: {
      chequingOpening: 3000,
      savingsOpening: 1000,
      cashOpening: 1000,
      creditCardOpening: 500,
      balanceEntryOpening: 2000,
      balanceEntryAsserted: 2500,
      transferAmount: 500,
    },
  },
};
