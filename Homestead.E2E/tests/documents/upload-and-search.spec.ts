import { test, expect } from '../../fixtures/documents.fixture';
import {
  documentNameOf,
  findDocumentByName,
  getDocumentViaApi,
  patchDocumentViaApi,
  uploadDocumentViaApi,
} from '../../helpers/api/documents';
import { waitForJob } from '../../helpers/api/processing';
import { copyRepoPdf, makePdf, makePng } from '../../helpers/files';
import { stubEmptyList } from '../../helpers/network';

test.describe('Documents — upload and search', () => {
  test('shows the empty state when there are no documents', async ({ page, documentListPage }) => {
    await stubEmptyList(page, /\/api\/v1\/documents\?/);
    await documentListPage.goto();
    await expect(documentListPage.noDocsEmpty).toBeVisible();
    await expect(documentListPage.uploadEmptyStateButton).toBeVisible();
  });

  const uploads = [
    { what: 'a PDF', makeFile: (uid: string) => copyRepoPdf('irs-form-w9.pdf', `w9-${uid}`) },
    { what: 'an image', makeFile: async (uid: string) => makePng(`photo-${uid}`) },
  ];
  for (const { what, makeFile } of uploads) {
    test(`uploads ${what} through the Upload popover`, async ({ request, documentListPage, uid }) => {
      const file = await makeFile(uid);
      const name = documentNameOf(file);

      await documentListPage.goto();
      const jobId = await documentListPage.upload(file);
      expect((await waitForJob(request, jobId)).status).toBe('Completed');

      await documentListPage.search(name);
      await expect(documentListPage.document(name)).toBeVisible();
    });
  }

  test('finds a document by its extracted text, and only that document', async ({ request, documentListPage, uid }) => {
    // The term is only in the PDF's text layer — neither document's name contains it.
    const term = `lighthouse${uid}`;
    const [pdf, image] = await Promise.all([
      uploadDocumentViaApi(request, await makePdf(`scan-${uid}`, `Keeper's log: ${term} lamp serviced`)),
      uploadDocumentViaApi(request, makePng(`photo-${uid}`)),
    ]);

    await documentListPage.goto();
    await documentListPage.search(term);
    await expect(documentListPage.document(pdf.name)).toBeVisible();
    await expect(documentListPage.document(image.name)).toHaveCount(0);

    await documentListPage.search(uid);
    await expect(documentListPage.document(pdf.name)).toBeVisible();
    await expect(documentListPage.document(image.name)).toBeVisible();
  });

  test('finds a document by the name of a tag attached to it', async ({ request, documentListPage, uid }) => {
    const [tagged, untagged] = await Promise.all([
      uploadDocumentViaApi(request, await makePdf(`tagged-${uid}`, 'Tagged')),
      uploadDocumentViaApi(request, await makePdf(`untagged-${uid}`, 'Untagged')),
    ]);
    await patchDocumentViaApi(request, tagged.id, [{ op: 'replace', path: '/tagPaths', value: [`zebratax${uid}`] }]);

    await documentListPage.goto();
    await documentListPage.search(`zebratax${uid}`);
    await expect(documentListPage.document(tagged.name)).toBeVisible();
    await expect(documentListPage.document(untagged.name)).toHaveCount(0);
  });

  test('the search-result snippet keeps its highlighted match inside the row in both views', async ({
    page,
    request,
    documentListPage,
    uid,
  }) => {
    const term = `lighthouse${uid}`;
    const doc = await uploadDocumentViaApi(
      request,
      await makePdf(`snippet-${uid}`, `A long preamble before the match so the snippet has to scroll to ${term} here`),
    );

    await documentListPage.goto();
    await documentListPage.search(term);
    await expect(documentListPage.document(doc.name)).toBeVisible();

    const expectMarkInsideHost = async (view: string) => {
      const snippet = page.locator('app-match-snippet').first();
      await expect(snippet, view).toBeVisible();
      const box = await snippet.evaluate((el) => {
        const mark = el.querySelector('mark');
        if (!mark) return null;
        const host = el.getBoundingClientRect();
        const m = mark.getBoundingClientRect();
        return { left: m.left - host.left, right: m.right - host.left, width: m.width, hostWidth: host.width };
      });
      expect(box, `${view}: snippet has a highlighted match`).not.toBeNull();
      expect(box!.width, view).toBeGreaterThan(0);
      expect(box!.left, `${view}: mark clipped on the left`).toBeGreaterThanOrEqual(-0.5);
      expect(box!.right, `${view}: mark clipped on the right`).toBeLessThanOrEqual(box!.hostWidth + 0.5);
    };

    await expectMarkInsideHost('grid view');
    await documentListPage.switchToTableView();
    await expectMarkInsideHost('table view');
  });

  test('opens a document from the list', async ({ request, documentListPage, documentDetailPage, uid }) => {
    const doc = await uploadDocumentViaApi(request, await makePdf(`open-${uid}`, 'Open me'));

    await documentListPage.goto();
    await documentListPage.search(doc.name);
    await documentListPage.document(doc.name).click();

    await documentDetailPage.waitForLoaded();
    await expect(documentDetailPage.nameInput).toHaveValue(doc.name);
    await expect(documentDetailPage.downloadButton).toBeVisible();
  });

  test('renames a document from its detail page', async ({ page, request, documentDetailPage, uid }) => {
    const doc = await uploadDocumentViaApi(request, await makePdf(`rename-${uid}`, 'Rename me'));
    const renamed = `renamed-${uid}`;

    await documentDetailPage.goto(doc.id);
    await documentDetailPage.nameInput.fill(renamed);
    await documentDetailPage.save();

    await page.reload();
    await documentDetailPage.waitForLoaded();
    await expect(documentDetailPage.nameInput).toHaveValue(renamed);
    expect((await getDocumentViaApi(request, doc.id)).name).toBe(renamed);
    expect((await findDocumentByName(request, renamed)).id).toBe(doc.id);
  });
});
