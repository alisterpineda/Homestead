import { test, expect } from '../../fixtures/documents.fixture';
import { LOCKS } from '../../e2e.config';
import {
  type DocumentSettings,
  getDocumentSettingsViaApi,
  patchDocumentSettingsViaApi,
} from '../../helpers/api/documents';

test.describe('Documents — settings', () => {
  test('reports the built-in Tesseract engine as the configured default', async ({ documentSettingsPage }) => {
    await documentSettingsPage.goto();

    // Engines are deployment config (Ocr:*); with none configured the built-in tesseract is the default.
    await expect(documentSettingsPage.textExtractionCard.getByRole('heading', { name: 'Text Extraction' })).toBeVisible();
    const tesseract = documentSettingsPage.engine('tesseract');
    await expect(tesseract).toContainText('Default');
    await expect(tesseract).toContainText('Configured');
    await expect(tesseract).not.toContainText('Not configured');
  });

  // Household-wide: flip the toggle under the lock and put the original value back afterwards.
  test.describe('auto-classify on upload', { lock: LOCKS.documentsSettings }, () => {
    let original: DocumentSettings;

    test.beforeEach(async ({ request }) => {
      original = await getDocumentSettingsViaApi(request);
    });

    test.afterEach(async ({ request }) => {
      await patchDocumentSettingsViaApi(request, { autoClassifyOnUpload: original.autoClassifyOnUpload });
    });

    test('the auto-classify toggle saves and is still set after a reload', async ({ request, documentSettingsPage }) => {
      const flipped = !original.autoClassifyOnUpload;

      await documentSettingsPage.goto();
      await expect(documentSettingsPage.autoClassifyCheckbox).toBeChecked({ checked: !flipped });
      await expect(documentSettingsPage.saveButton).toBeDisabled();

      await documentSettingsPage.setAutoClassify(flipped);
      expect((await getDocumentSettingsViaApi(request)).autoClassifyOnUpload).toBe(flipped);

      await documentSettingsPage.goto();
      await expect(documentSettingsPage.autoClassifyCheckbox).toBeChecked({ checked: flipped });
    });
  });
});
