import { test, expect } from '../../fixtures/app.fixture';

test.describe.configure({ mode: 'serial' });

test.describe('Trash management', () => {
  test('soft-delete a document via the UI', async ({ page, documentListPage }) => {
    await documentListPage.goto();
    await documentListPage.switchToTableView();

    await page.locator('table tbody tr td:first-child [role="checkbox"]').first().click();
    await documentListPage.bulkDeleteButton().click();
    await page.getByRole('button', { name: /^Delete$/ }).last().click();
    await page.waitForLoadState('networkidle');
  });

  test('trash page shows soft-deleted documents', async ({ trashListPage }) => {
    await trashListPage.goto();
    await expect(trashListPage.trashEmpty).not.toBeVisible();
    await expect(trashListPage.page.locator('table tbody tr').first()).toBeVisible();
  });

  test('can restore a document from trash', async ({ page, trashListPage }) => {
    await trashListPage.goto();

    const docName = await trashListPage.getFirstDocumentName();
    expect(docName.length).toBeGreaterThan(0);

    await trashListPage.selectDocument(docName);
    await trashListPage.restore();

    await page.goto('/documents');
    await page.waitForLoadState('networkidle');
    await expect(page.getByText(docName).first()).toBeVisible({ timeout: 10_000 });
  });

  test('can permanently delete a document', async ({ page, documentListPage, trashListPage }) => {
    await documentListPage.goto();
    await documentListPage.switchToTableView();

    const docName = await page.locator('table tbody tr td:nth-child(2)').first().textContent();
    const trimmedName = docName?.trim() ?? '';

    await page.locator('table tbody tr td:first-child [role="checkbox"]').first().click();
    await documentListPage.bulkDeleteButton().click();
    await page.getByRole('button', { name: /^Delete$/ }).last().click();
    await page.waitForLoadState('networkidle');

    await trashListPage.goto();
    await trashListPage.waitForDocumentVisible(trimmedName);
    await trashListPage.selectDocument(trimmedName);
    await trashListPage.deletePermanently();

    await expect(trashListPage.page.getByText(trimmedName).first()).not.toBeVisible();
  });

  test('can empty entire trash', async ({ page, documentListPage, trashListPage }) => {
    await documentListPage.goto();
    await documentListPage.switchToTableView();
    await page.waitForLoadState('networkidle');

    const rowCount = await page.locator('table tbody tr').count();
    if (rowCount > 0) {
      await page.getByLabel('Select all').first().click();
      await documentListPage.bulkDeleteButton().click();
      await page.getByRole('button', { name: /^Delete$/ }).last().click();
      await page.waitForLoadState('networkidle');
    }

    await trashListPage.goto();

    if (await trashListPage.trashEmpty.isVisible()) {
      test.skip(true, 'No documents available to test empty trash');
      return;
    }

    await expect(trashListPage.page.locator('table tbody tr').first()).toBeVisible();
    await trashListPage.emptyTrash();
    await trashListPage.waitForEmpty();
  });
});
