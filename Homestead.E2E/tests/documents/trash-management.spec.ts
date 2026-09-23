import { test, expect } from '../../fixtures/documents.fixture';
import { LOCKS } from '../../e2e.config';
import { trashDocumentsViaApi, uploadDocumentViaApi } from '../../helpers/api/documents';
import { makePdf } from '../../helpers/files';

// The trash is household-wide — "Empty Trash" purges every test's trashed documents — so every
// test that relies on the trash's contents holds the same lock.
test.describe('Documents — trash', { lock: LOCKS.documentsTrash }, () => {
  test('restores a document from the trash', async ({ request, documentListPage, trashListPage, uid }) => {
    const doc = await uploadDocumentViaApi(request, await makePdf(`restore-${uid}`, 'Restore me'));
    await trashDocumentsViaApi(request, [doc.id]);

    await trashListPage.goto();
    await trashListPage.select(doc.name);
    await trashListPage.restoreSelected();
    await expect(trashListPage.document(doc.name)).toHaveCount(0);

    await documentListPage.goto();
    await documentListPage.search(doc.name);
    await expect(documentListPage.document(doc.name)).toBeVisible();
  });

  test('permanently deletes a document from the trash', async ({ request, trashListPage, uid }) => {
    const doc = await uploadDocumentViaApi(request, await makePdf(`purge-${uid}`, 'Purge me'));
    await trashDocumentsViaApi(request, [doc.id]);

    await trashListPage.goto();
    await trashListPage.select(doc.name);
    await trashListPage.deleteSelectedPermanently();

    await expect(trashListPage.document(doc.name)).toHaveCount(0);
    expect((await request.get(`/api/v1/documents/${doc.id}`)).status()).toBe(404);
  });

  test('empties the whole trash', async ({ request, trashListPage, uid }) => {
    const docs = await Promise.all([
      uploadDocumentViaApi(request, await makePdf(`empty-a-${uid}`, 'A')),
      uploadDocumentViaApi(request, await makePdf(`empty-b-${uid}`, 'B')),
    ]);
    await trashDocumentsViaApi(
      request,
      docs.map((d) => d.id),
    );

    await trashListPage.goto();
    await expect(trashListPage.document(docs[0].name)).toBeVisible();
    await trashListPage.emptyTrash();
    await expect(trashListPage.trashEmpty).toBeVisible();
  });
});
