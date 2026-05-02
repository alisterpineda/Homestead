// Cross-module fixture aggregator. Specs that only need one module's page
// objects should import that module's fixture file directly; this entry point
// exists for tests that legitimately span modules (e.g. an auth flow that ends
// on a documents page).
import { mergeTests } from '@playwright/test';
import { test as authTest } from './auth.fixture';
import { test as documentsTest } from './documents.fixture';

export const test = mergeTests(authTest, documentsTest);
export { expect } from '@playwright/test';
