import { test, expect } from '../../fixtures/app.fixture';

test.describe.configure({ mode: 'serial' });

test.describe('Trash management', () => {
  test('soft-delete a document via the UI', async ({ page, documentListPage }) => {
    await documentListPage.goto();
    await documentListPage.switchToTableView();

    await page.getByLabel(/^Select /).first().click();
    await documentListPage.bulkDeleteButton().click();
    const deleteResponse = page.waitForResponse(
      (resp) => resp.url().includes('/api/v1/documents/bulk-delete') && resp.request().method() === 'POST',
    );
    await page.getByRole('button', { name: /^Delete$/ }).last().click();
    await deleteResponse;
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

    const docsResponse = page.waitForResponse(
      (resp) => resp.url().includes('/api/v1/documents') && resp.request().method() === 'GET' && resp.status() === 200,
    );
    await page.goto('/documents');
    await docsResponse;
    await expect(page.getByText(docName).first()).toBeVisible();
  });

  test('can permanently delete a document', async ({ page, documentListPage, trashListPage }) => {
    await documentListPage.goto();
    await documentListPage.switchToTableView();

    const firstNameCell = page.locator('table tbody tr').first().locator('td').nth(1);
    await expect(firstNameCell).toBeVisible();
    const docName = await firstNameCell.textContent();
    const trimmedName = docName?.trim() ?? '';

    await page.getByLabel(/^Select /).first().click();
    await documentListPage.bulkDeleteButton().click();
    const deleteResponse = page.waitForResponse(
      (resp) => resp.url().includes('/api/v1/documents/bulk-delete') && resp.request().method() === 'POST',
    );
    await page.getByRole('button', { name: /^Delete$/ }).last().click();
    await deleteResponse;

    await trashListPage.goto();
    await trashListPage.waitForDocumentVisible(trimmedName);
    await trashListPage.selectDocument(trimmedName);
    await trashListPage.deletePermanently();

    await expect(trashListPage.page.getByText(trimmedName).first()).not.toBeVisible();
  });

  test('can empty entire trash', async ({ page, documentListPage, trashListPage }) => {
    await documentListPage.goto();
    await documentListPage.switchToTableView();

    // Check if there are documents to soft-delete first
    const hasDocuments = await page.locator('table tbody tr').first().isVisible().catch(() => false);
    if (hasDocuments) {
      await page.getByLabel('Select all').first().click();
      await documentListPage.bulkDeleteButton().click();
      const deleteResponse = page.waitForResponse(
        (resp) => resp.url().includes('/api/v1/documents/bulk-delete') && resp.request().method() === 'POST',
      );
      await page.getByRole('button', { name: /^Delete$/ }).last().click();
      await deleteResponse;
    }

    await trashListPage.goto();

    // Wait for either the empty state or table rows to render before checking
    await Promise.race([
      trashListPage.trashEmpty.waitFor({ state: 'visible', timeout: 5_000 }),
      trashListPage.page.locator('table tbody tr').first().waitFor({ state: 'visible', timeout: 5_000 }),
    ]);

    if (await trashListPage.trashEmpty.isVisible()) {
      test.skip(true, 'No documents available to test empty trash');
      return;
    }

    await expect(trashListPage.page.locator('table tbody tr').first()).toBeVisible();
    await trashListPage.emptyTrash();
    await trashListPage.waitForEmpty();
  });
});
