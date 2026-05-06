import * as path from 'path';
import { test, expect } from '../../../fixtures/documents.fixture';
import { TEST_DATA } from '../../../e2e.config';
import { waitForProcessingComplete } from '../../../helpers/api/processing';
import { deleteAllDocuments } from '../../../helpers/api/documents';

const PDF_PATH = path.resolve(__dirname, '../../../fixtures/files/test-document.pdf');
const IMAGE_PATH = path.resolve(__dirname, '../../../fixtures/files/test-image.png');

test.describe.configure({ mode: 'serial' });

test.describe('Document upload and search workflow', () => {
  // Clean up documents from previous runs so retries start from a clean state
  test.beforeAll(async ({ request }) => {
    await deleteAllDocuments(request);
  });

  test.afterAll(async ({ request }) => {
    await deleteAllDocuments(request);
  });

  test('shows empty state when no documents exist', async ({ documentListPage }) => {
    await documentListPage.goto();
    await expect(documentListPage.noDocsEmpty).toBeVisible();
    await expect(documentListPage.uploadButton).toBeVisible();
  });

  test('can upload a PDF document via the upload popover', async ({ page, request, documentListPage }) => {
    await documentListPage.goto();

    const uploadResponse = page.waitForResponse(
      (resp) => resp.url().includes('/api/v1/documents') && resp.request().method() === 'POST',
    );
    const fileInput = page.locator('input[type="file"]').first();
    await fileInput.setInputFiles(PDF_PATH);
    await uploadResponse;
    await waitForProcessingComplete(request, 60_000);

    await documentListPage.goto();
    await documentListPage.waitForDocumentVisible('test-document', 15_000);
  });

  test('can upload an image document', async ({ page, request, documentListPage }) => {
    await documentListPage.goto();

    // Open the upload popover so its file input is rendered in the DOM
    // (the empty-state file input is hidden when documents already exist)
    await page.getByRole('button', { name: 'Upload', exact: true }).click();
    await page.getByRole('button', { name: 'Choose files' }).waitFor({ state: 'visible' });
    const popoverInput = page.locator('[data-state="open"] input[type="file"]');

    const uploadResponse = page.waitForResponse(
      (resp) => resp.url().includes('/api/v1/documents') && resp.request().method() === 'POST',
    );
    await popoverInput.setInputFiles(IMAGE_PATH);
    await uploadResponse;
    await waitForProcessingComplete(request, 60_000);

    await documentListPage.goto();
    await documentListPage.waitForDocumentVisible('test-image', 15_000);
  });

  test('uploaded documents appear in the document list', async ({ documentListPage }) => {
    await documentListPage.goto();
    await expect(documentListPage.noDocsEmpty).not.toBeVisible();
    await documentListPage.waitForDocumentVisible('test-document');
    await documentListPage.waitForDocumentVisible('test-image');
  });

  test('can search documents by extracted text content', async ({ documentListPage }) => {
    await documentListPage.goto();

    await documentListPage.search(TEST_DATA.documents.files.searchableText);
    await documentListPage.waitForDocumentVisible('test-document');
    await expect(documentListPage.page.getByText('test-image').first()).not.toBeVisible();

    await documentListPage.clearSearch();
    await documentListPage.waitForDocumentVisible('test-document');
  });

  test('can search documents by name', async ({ documentListPage }) => {
    await documentListPage.goto();
    await documentListPage.search('test-document');
    await documentListPage.waitForDocumentVisible('test-document');
  });

  test('can search documents by attached tag name', async ({ request, documentListPage }) => {
    await documentListPage.goto();

    const listResponse = await request.get('/api/v1/documents?Page=1&PageSize=10&IncludeChildTags=false');
    expect(listResponse.ok()).toBeTruthy();
    const listBody = await listResponse.json();
    const docId = listBody.items.find((d: { name: string }) => d.name.includes('test-document'))?.id;
    expect(docId).toBeDefined();

    const patchResponse = await request.patch(`/api/v1/documents/${docId}`, {
      headers: { 'Content-Type': 'application/json-patch+json' },
      data: [{ op: 'replace', path: '/tagPaths', value: ['ZebraTax'] }],
    });
    expect(patchResponse.ok()).toBeTruthy();

    await documentListPage.goto();
    await documentListPage.search('zebra');
    await documentListPage.waitForDocumentVisible('test-document');
  });

  test('can open a document detail page', async ({ page, documentListPage, documentDetailPage }) => {
    await documentListPage.goto();
    await page.getByText('test-document').first().click();
    await page.waitForURL(/\/documents\/[0-9a-f-]+/);

    await documentDetailPage.waitForLoaded();
    await expect(page.getByRole('button', { name: 'Download' })).toBeVisible();
  });

  test('can edit document name and save', async ({ page, request, documentListPage, documentDetailPage }) => {
    if (!page.url().match(/\/documents\/[0-9a-f-]+/)) {
      await documentListPage.goto();
      await page.getByText('test-document').first().click();
      await page.waitForURL(/\/documents\/[0-9a-f-]+/);
    }

    await documentDetailPage.waitForLoaded();

    const docId = page.url().match(/\/documents\/([0-9a-f-]+)/)?.[1];
    expect(docId).toBeDefined();

    const patchResponse = await request.patch(`/api/v1/documents/${docId}`, {
      headers: { 'Content-Type': 'application/json-patch+json' },
      data: [{ op: 'replace', path: '/name', value: 'test-document-renamed' }],
    });
    expect(patchResponse.ok()).toBeTruthy();

    await page.reload();
    await documentDetailPage.waitForLoaded();
    await expect(documentDetailPage.nameInput).toHaveValue('test-document-renamed');

    // Restore so other test suites see the original name
    await request.patch(`/api/v1/documents/${docId}`, {
      headers: { 'Content-Type': 'application/json-patch+json' },
      data: [{ op: 'replace', path: '/name', value: 'test-document' }],
    });
  });
});
