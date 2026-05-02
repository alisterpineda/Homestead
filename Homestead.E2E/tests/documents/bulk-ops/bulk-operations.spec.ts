import { test, expect } from '../../../fixtures/documents.fixture';
import { TEST_DATA } from '../../../e2e.config';
import { fillSignalInput } from '../../../helpers/playwright.helpers';
import { deleteAllDocuments, seedFixtureDocuments } from '../../../helpers/api/documents';

test.describe.configure({ mode: 'serial' });

test.describe('Bulk operations', () => {
  test.beforeAll(async ({ request }) => {
    await deleteAllDocuments(request);
    await seedFixtureDocuments(request, { include: 'both' });
  });

  test.afterAll(async ({ request }) => {
    await deleteAllDocuments(request);
  });

  test('can switch to table view and select multiple documents', async ({ page, documentListPage }) => {
    await documentListPage.goto();
    await documentListPage.switchToTableView();
    await expect(page.locator('table tbody tr').first()).toBeVisible();

    await documentListPage.selectDocumentCheckbox('test-image');
    await expect(documentListPage.selectionCountText()).toBeVisible();
  });

  test('can bulk assign tags to selected documents', async ({ page, documentListPage }) => {
    await documentListPage.goto();
    await documentListPage.switchToTableView();

    await documentListPage.selectDocumentCheckbox('test-image');
    await documentListPage.selectDocumentCheckbox('test-document');

    await documentListPage.bulkTagsButton().click();

    const tagInput = page.getByPlaceholder(/search tags to add/i).first();
    await fillSignalInput(tagInput, TEST_DATA.documents.tags.bulkTag1);

    await page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/documents/tags') &&
        resp.url().includes('Search=') &&
        resp.request().method() === 'GET',
    );

    await expect(page.getByText(TEST_DATA.documents.tags.bulkTag1).first()).toBeVisible();
    await page.getByText(TEST_DATA.documents.tags.bulkTag1).first().click();

    await page.getByRole('button', { name: /Apply/i }).click();

    const bulkUpdateResponse = page.waitForResponse(
      (resp) => resp.url().includes('/api/v1/documents/bulk-update') && resp.request().method() === 'POST',
    );
    await page.getByRole('button', { name: /Confirm/i }).click();
    await bulkUpdateResponse;
  });

  test('can bulk assign parties to selected documents', async ({ page, documentListPage }) => {
    await documentListPage.goto();
    await documentListPage.switchToTableView();

    await documentListPage.selectDocumentCheckbox('test-image');
    await documentListPage.selectDocumentCheckbox('test-document');

    await documentListPage.bulkPartiesButton().click();

    const partyInput = page.getByPlaceholder(/search parties to add/i).first();
    await fillSignalInput(partyInput, TEST_DATA.documents.parties.acmeCorp);

    await page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/documents/parties') &&
        resp.url().includes('Search=') &&
        resp.request().method() === 'GET',
    );

    await expect(page.getByText(TEST_DATA.documents.parties.acmeCorp).first()).toBeVisible();
    await page.getByText(TEST_DATA.documents.parties.acmeCorp).first().click();

    await page.getByRole('button', { name: /Apply/i }).click();

    const bulkUpdateResponse = page.waitForResponse(
      (resp) => resp.url().includes('/api/v1/documents/bulk-update') && resp.request().method() === 'POST',
    );
    await page.getByRole('button', { name: /Confirm/i }).click();
    await bulkUpdateResponse;
  });

  test('can bulk delete selected documents (soft delete)', async ({ page, documentListPage }) => {
    await documentListPage.goto();
    await documentListPage.switchToTableView();

    await documentListPage.selectDocumentCheckbox('test-image');
    await documentListPage.bulkDeleteButton().click();
    const deleteResponse = page.waitForResponse(
      (resp) => resp.url().includes('/api/v1/documents/bulk-delete') && resp.request().method() === 'POST',
    );
    await page.getByRole('button', { name: /^Delete$/ }).last().click();
    await deleteResponse;
  });

  test('can select all documents on page', async ({ page, documentListPage }) => {
    await documentListPage.goto();
    await documentListPage.switchToTableView();

    await page.getByLabel('Select all').first().click();
    await expect(documentListPage.selectionCountText()).toBeVisible();
    await documentListPage.exitSelectionMode();
  });
});
