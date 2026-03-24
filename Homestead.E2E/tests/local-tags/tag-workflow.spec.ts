import { test, expect } from '../../fixtures/app.fixture';
import { TEST_DATA } from '../../e2e.config';

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
    await page.goto('/documents');
    await page.waitForLoadState('networkidle');

    // Click the first document in the list
    await page.locator('table tbody tr, app-document-card-grid [hlmCard]').first().click();
    await page.waitForURL(/\/documents\/[0-9a-f-]+/);

    // Wait for the detail page to load
    await expect(page.getByLabel('Name')).toBeVisible({ timeout: 15_000 });

    // Use the tag input to add a tag
    const tagInput = page.getByPlaceholder('Add tags...').or(
      page.locator('app-tag-input input'),
    ).first();
    await tagInput.click();
    await tagInput.fill(TEST_DATA.tags.finance);
    await page.waitForTimeout(500);
    // Select the tag from autocomplete
    await page.getByText(TEST_DATA.tags.finance).first().click();

    // Save the document
    await page.getByRole('button', { name: /^Save$/ }).click();
    await expect(page.getByRole('button', { name: 'Save' })).toBeEnabled({ timeout: 10_000 });
  });

  test('can filter documents by tag', async ({ page, documentListPage }) => {
    await documentListPage.goto();

    // Open Tags filter popover
    await page.getByRole('button', { name: 'Tags' }).first().click();
    await page.waitForTimeout(300);

    // Search for the tag
    const filterSearch = page.getByPlaceholder('Search tags...');
    await filterSearch.fill(TEST_DATA.tags.finance);
    await page.waitForTimeout(500);

    // Click the checkbox next to the tag name (exact match to avoid selecting sub-tags)
    await page.getByText(TEST_DATA.tags.finance, { exact: true }).click();

    await page.waitForLoadState('networkidle');

    // The tagged document should be visible
    // At least one document should be in the list
    await expect(page.locator('table tbody tr, app-document-card-grid [hlmCard]').first()).toBeVisible();
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
