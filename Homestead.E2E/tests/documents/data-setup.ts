import { test as setup } from '@playwright/test';
import { TEST_DATA } from '../../e2e.config';
import { ensureFixtureFiles } from '../../helpers/generate-fixtures';
import { createTagViaApi, createPartyViaApi } from '../../helpers/api/documents';

setup('generate test fixture files', async () => {
  await ensureFixtureFiles();
});

setup('seed tags for business tests', async ({ request }) => {
  // finance must exist before finance/invoices (parent path)
  await createTagViaApi(request, TEST_DATA.documents.tags.finance);
  await Promise.all([
    createTagViaApi(request, TEST_DATA.documents.tags.invoices),
    createTagViaApi(request, TEST_DATA.documents.tags.contracts),
    createTagViaApi(request, TEST_DATA.documents.tags.bulkTag1),
    createTagViaApi(request, TEST_DATA.documents.tags.bulkTag2),
  ]);
});

setup('seed parties for business tests', async ({ request }) => {
  await createPartyViaApi(request, TEST_DATA.documents.parties.acmeCorp);
  await createPartyViaApi(request, TEST_DATA.documents.parties.johnDoe);
});
