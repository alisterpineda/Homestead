import * as path from 'path';
import { test, expect } from '../../fixtures/app.fixture';
import { TEST_DATA } from '../../e2e.config';
import { waitForProcessingComplete } from '../../helpers/api.helpers';

const PDF_PATH = path.resolve(__dirname, '../../fixtures/files/test-document.pdf');
const IMAGE_PATH = path.resolve(__dirname, '../../fixtures/files/test-image.png');

test.describe.configure({ mode: 'serial' });

test.describe('Document upload and search workflow', () => {
  test('shows empty state when no documents exist', async ({ documentListPage }) => {
    await documentListPage.goto();
    await expect(documentListPage.noDocsEmpty).toBeVisible();
    await expect(documentListPage.uploadButton).toBeVisible();
  });

  test('can upload a PDF document via the upload popover', async ({ page, request, documentListPage }) => {
    await documentListPage.goto();

    const fileInput = page.locator('input[type="file"]').first();
    await fileInput.setInputFiles(PDF_PATH);
    await waitForProcessingComplete(request, 60_000);

    await documentListPage.goto();
    await documentListPage.waitForDocumentVisible('test-document', 15_000);
  });

  test('can upload an image document', async ({ page, request, documentListPage }) => {
    await documentListPage.goto();

    const fileInput = page.locator('input[type="file"]').first();
    await fileInput.setInputFiles(IMAGE_PATH);
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

    await documentListPage.search(TEST_DATA.documents.searchableText);
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

  test('can open a document detail page', async ({ page, documentListPage }) => {
    await documentListPage.goto();
    await page.getByText('test-document').first().click();
    await page.waitForURL(/\/documents\/[0-9a-f-]+/);

    await expect(page.getByLabel('Name')).toBeVisible({ timeout: 15_000 });
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
