import { test, expect } from '../../fixtures/app.fixture';
import { TEST_DATA } from '../../e2e.config';
import { fillSignalInput } from '../../helpers/playwright.helpers';

test.describe.configure({ mode: 'serial' });

test.describe('Tag workflow', () => {
  test('tag list shows seeded tags', async ({ tagListPage }) => {
    await tagListPage.goto();
    await tagListPage.waitForTagVisible(TEST_DATA.tags.finance);
    await tagListPage.waitForTagVisible(TEST_DATA.tags.contracts);
  });

  test('can create a new tag via the UI', async ({ tagListPage }) => {
    await tagListPage.goto();
    await tagListPage.createTag('e2e-ui-created', { description: 'Created via E2E test' });
    await tagListPage.waitForTagVisible('e2e-ui-created');
  });

  test('can search tags', async ({ tagListPage }) => {
    await tagListPage.goto();

    await tagListPage.searchTags('e2e-finance');
    // Should show finance-related tags
    await tagListPage.waitForTagVisible(TEST_DATA.tags.finance);
    // Unrelated tags should not be visible
    await expect(tagListPage.page.getByText(TEST_DATA.tags.contracts).first()).not.toBeVisible();

    await tagListPage.clearSearch();
  });

  test('can assign a tag to a document from the detail page', async ({ page }) => {
    // Navigate to a document
    const docsResponse = page.waitForResponse(
      (resp) => resp.url().includes('/api/v1/documents') && resp.request().method() === 'GET' && resp.status() === 200,
    );
    await page.goto('/documents');
    await docsResponse;

    // Click the first document by name
    await page.getByText('test-document').first().click();
    await page.waitForURL(/\/documents\/[0-9a-f-]+/);

    // Wait for the detail page to load
    await expect(page.getByLabel('Name')).toBeVisible({ timeout: 15_000 });

    // Use the tag input to add a tag
    const tagInput = page.locator('app-tag-input input').first();
    await tagInput.click();
    await fillSignalInput(tagInput, TEST_DATA.tags.finance);

    // Wait for autocomplete API response, then click the matching suggestion
    await page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/documents/tags') &&
        resp.url().includes('Search=') &&
        resp.request().method() === 'GET',
    );
    const suggestion = page.getByRole('button', { name: TEST_DATA.tags.finance }).first();
    await expect(suggestion).toBeVisible();
    await suggestion.click();

    // Wait for Angular to update the Save button state
    await expect(page.getByRole('button', { name: /^Save$/ })).toBeEnabled({ timeout: 10_000 });

    // Save the document and wait for the API response
    const saveResponse = page.waitForResponse(
      (resp) => resp.url().includes('/api/v1/documents/') && resp.request().method() === 'PATCH',
    );
    await page.getByRole('button', { name: /^Save$/ }).click();
    await saveResponse;
  });

  test('can filter documents by tag', async ({ page, documentListPage }) => {
    await documentListPage.goto();

    // Open Tags filter popover and wait for it to render
    await page.getByRole('button', { name: 'Tags' }).first().click();
    const filterSearch = page.getByPlaceholder('Search tags...');
    await expect(filterSearch).toBeVisible();

    // Search for the tag and wait for the API response
    const tagOptionsResponse = page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/documents/tags') &&
        resp.url().includes('Search=') &&
        resp.request().method() === 'GET',
    );
    await filterSearch.fill(TEST_DATA.tags.finance);
    await tagOptionsResponse;

    // Click the filter option and wait for filtered document list
    const filteredDocsResponse = page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/documents') &&
        resp.url().includes('TagIds=') &&
        resp.request().method() === 'GET' &&
        resp.status() === 200,
    );
    await page.getByRole('button', { name: TEST_DATA.tags.finance, exact: true }).click();
    await filteredDocsResponse;

    // The tagged document should be visible
    await expect(page.getByText('test-document').first()).toBeVisible();
  });

  test('can bulk delete the UI-created tag', async ({ tagListPage }) => {
    await tagListPage.goto();

    // Find and select the tag we created via UI
    await tagListPage.selectTagCheckbox('e2e-ui-created');

    // Delete
    await tagListPage.deleteSelected();

    // Verify it's gone
    await expect(tagListPage.page.getByText('e2e-ui-created').first()).not.toBeVisible();
  });
});
