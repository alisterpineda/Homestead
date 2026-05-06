import { test as setup } from '@playwright/test';
import { deleteAllDocuments } from '../../helpers/api/documents';

setup('clear documents before mobile content tests', async ({ request }) => {
  await deleteAllDocuments(request);
});
