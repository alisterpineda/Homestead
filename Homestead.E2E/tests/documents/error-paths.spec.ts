import { test, expect } from '../../fixtures/documents.fixture';
import { getDocumentViaApi, searchDocumentsViaApi, uploadDocumentViaApi } from '../../helpers/api/documents';
import { waitForJob } from '../../helpers/api/processing';
import { makePdf } from '../../helpers/files';
import { stubFailure } from '../../helpers/network';

test.describe('Documents — error paths', () => {
  test('uploading content that is already stored points at the existing document instead of adding a copy', async ({
    page,
    request,
    documentListPage,
    uid,
  }) => {
    const original = await makePdf(`original-${uid}`, 'Home insurance policy');
    const existing = await uploadDocumentViaApi(request, original);
    // Same bytes, different file name.
    const copy = { ...original, name: `copy-${uid}.pdf` };

    await documentListPage.goto();
    const jobId = await documentListPage.upload(copy);

    await expect(
      page.getByRole('status').filter({ hasText: `"${copy.name}" already exists as "${existing.name}"` }),
    ).toBeVisible();
    const existingLink = page.locator('[hlmPopoverContent]').getByRole('link', { name: existing.name, exact: true });
    await expect(existingLink).toHaveAttribute('href', `/documents/${existing.id}`);

    const job = await waitForJob(request, jobId);
    expect(job.status).toBe('Failed');
    expect(await searchDocumentsViaApi(request, uid)).toEqual([existing]);
  });

  test('a failed save says so and keeps the unsaved edit for a retry', async ({
    page,
    request,
    documentDetailPage,
    uid,
  }) => {
    const doc = await uploadDocumentViaApi(request, await makePdf(`contract-${uid}`, 'Service contract'));
    const renamed = `renamed-${uid}`;
    const patchUrl = new RegExp(`/api/v1/documents/${doc.id}$`);

    await documentDetailPage.goto(doc.id);
    await stubFailure(page, patchUrl, { method: 'PATCH' });
    await documentDetailPage.nameInput.fill(renamed);
    const failedSave = page.waitForResponse((resp) => patchUrl.test(resp.url()) && resp.request().method() === 'PATCH');
    await documentDetailPage.saveButton.click();
    expect((await failedSave).status()).toBe(500);

    await expect(page.getByRole('status').filter({ hasText: 'An unexpected server error occurred' })).toBeVisible();
    await expect(page.getByRole('status').filter({ hasText: 'Document updated.' })).toHaveCount(0);
    await expect(documentDetailPage.nameInput).toHaveValue(renamed);
    await expect(documentDetailPage.saveButton).toBeEnabled();
    expect((await getDocumentViaApi(request, doc.id)).name).toBe(doc.name);

    // Once the server is back, the same edit saves.
    await page.unroute(patchUrl);
    await documentDetailPage.save();
    expect((await getDocumentViaApi(request, doc.id)).name).toBe(renamed);
  });
});
