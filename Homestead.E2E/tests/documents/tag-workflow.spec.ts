import { test, expect } from '../../fixtures/documents.fixture';
import {
  createTagViaApi,
  getDocumentViaApi,
  patchDocumentViaApi,
  uploadDocumentViaApi,
} from '../../helpers/api/documents';
import { makePdf } from '../../helpers/files';

test.describe('Documents — tags', () => {
  test('creates a tag from the tag list', async ({ tagListPage, uid }) => {
    const path = `ui-created-${uid}`;

    await tagListPage.goto();
    await tagListPage.createTag(path, { description: 'Created via E2E test' });
    await tagListPage.search(path);
    await expect(tagListPage.tag(path)).toBeVisible();
  });

  test('searching narrows the tag list', async ({ request, tagListPage, uid }) => {
    const finance = `finance-${uid}`;
    const contracts = `contracts-${uid}`;
    await createTagViaApi(request, finance);
    await createTagViaApi(request, `${finance}/invoices`);
    await createTagViaApi(request, contracts);

    await tagListPage.goto();
    await tagListPage.search(finance);
    await expect(tagListPage.tag(finance)).toBeVisible();
    await expect(tagListPage.tag(contracts)).toHaveCount(0);
  });

  test('assigns a tag to a document from its detail page', async ({ page, request, documentDetailPage, uid }) => {
    const path = `finance-${uid}`;
    await createTagViaApi(request, path);
    const doc = await uploadDocumentViaApi(request, await makePdf(`invoice-${uid}`, 'Invoice'));

    await documentDetailPage.goto(doc.id);
    await documentDetailPage.addTag(path);
    await documentDetailPage.save();

    expect((await getDocumentViaApi(request, doc.id)).tags.map((t) => t.path)).toEqual([path]);
    await page.reload();
    await documentDetailPage.waitForLoaded();
    await expect(page.getByRole('tabpanel').getByText(path, { exact: true })).toBeVisible();
  });

  test('filters the document list by tag', async ({ page, request, documentListPage, uid }) => {
    const path = `finance-${uid}`;
    const [tagged, untagged] = await Promise.all([
      uploadDocumentViaApi(request, await makePdf(`tagged-${uid}`, 'Tagged')),
      uploadDocumentViaApi(request, await makePdf(`untagged-${uid}`, 'Untagged')),
    ]);
    await patchDocumentViaApi(request, tagged.id, [{ op: 'replace', path: '/tagPaths', value: [path] }]);

    await documentListPage.goto();
    await documentListPage.search(uid);
    await expect(documentListPage.document(untagged.name)).toBeVisible();

    await documentListPage.filterByTag(path);
    await expect(page).toHaveURL(/[?&]tagIds=/);
    // The trigger summarises active filters: "Filter: Tags" on desktop, a count badge on phones.
    await expect(documentListPage.filterButton).toContainText(/Filter: Tags|^1$/);

    await expect(documentListPage.document(tagged.name)).toBeVisible();
    await expect(documentListPage.document(untagged.name)).toHaveCount(0);
  });

  test('deletes a tag from the tag list', async ({ request, tagListPage, uid }) => {
    const path = `doomed-${uid}`;
    await createTagViaApi(request, path);

    await tagListPage.goto();
    await tagListPage.search(path);
    await tagListPage.select(path);
    await tagListPage.deleteSelected();

    await expect(tagListPage.tag(path)).toHaveCount(0);
    const remaining = await request.get(`/api/v1/documents/tags?Page=1&PageSize=10&Search=${path}`);
    expect((await remaining.json()).items).toEqual([]);
  });
});
