import { test, expect } from '../../fixtures/app.fixture';
import { TEST_DATA } from '../../e2e.config';
import { fillSignalInput } from '../../helpers/playwright.helpers';

test.describe.configure({ mode: 'serial' });

test.describe('Bulk operations', () => {
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

    await fillSignalInput(page.getByPlaceholder(/search tags to add/i).first(), TEST_DATA.tags.bulkTag1);
    await page.waitForTimeout(1_000);
    await page.getByText(TEST_DATA.tags.bulkTag1).first().click();

    await page.getByRole('button', { name: /Apply/i }).click();
    await page.waitForLoadState('networkidle');
  });

  test('can bulk assign parties to selected documents', async ({ page, documentListPage }) => {
    await documentListPage.goto();
    await documentListPage.switchToTableView();

    await documentListPage.selectDocumentCheckbox('test-image');
    await documentListPage.selectDocumentCheckbox('test-document');

    await documentListPage.bulkPartiesButton().click();

    await fillSignalInput(page.getByPlaceholder(/search parties to add/i).first(), TEST_DATA.parties.acmeCorp);
    await page.waitForTimeout(1_000);
    await page.getByText(TEST_DATA.parties.acmeCorp).first().click();

    await page.getByRole('button', { name: /Apply/i }).click();
    await page.waitForLoadState('networkidle');
  });

  test('can bulk delete selected documents (soft delete)', async ({ page, documentListPage }) => {
    await documentListPage.goto();
    await documentListPage.switchToTableView();

    await documentListPage.selectDocumentCheckbox('test-image');
    await documentListPage.bulkDeleteButton().click();
    await page.getByRole('button', { name: /^Delete$/ }).last().click();
    await page.waitForLoadState('networkidle');
  });

  test('can select all documents on page', async ({ page, documentListPage }) => {
    await documentListPage.goto();
    await documentListPage.switchToTableView();

    await page.getByLabel('Select all').first().click();
    await expect(documentListPage.selectionCountText()).toBeVisible();
    await documentListPage.exitSelectionMode();
  });
});
