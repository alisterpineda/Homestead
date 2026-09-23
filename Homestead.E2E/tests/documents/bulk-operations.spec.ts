import { test, expect } from '../../fixtures/documents.fixture';
import { LOCKS } from '../../e2e.config';
import {
  createPartyViaApi,
  createTagViaApi,
  getDocumentViaApi,
  uploadDocumentViaApi,
} from '../../helpers/api/documents';
import { makePdf, makePng } from '../../helpers/files';

test.describe('Documents — bulk operations', () => {
  test('selecting documents shows the selection count, and select-all takes the whole page', async ({
    request,
    documentListPage,
    uid,
  }) => {
    const [pdf, image] = await Promise.all([
      uploadDocumentViaApi(request, await makePdf(`bulk-${uid}`, 'Bulk')),
      uploadDocumentViaApi(request, makePng(`bulk-photo-${uid}`)),
    ]);

    await documentListPage.goto();
    await documentListPage.switchToTableView();
    await documentListPage.search(uid);
    await documentListPage.select(pdf.name);
    await expect(documentListPage.selectionCount).toHaveText('1 document selected');
    await documentListPage.select(image.name);
    await expect(documentListPage.selectionCount).toHaveText('2 documents selected');
    await documentListPage.exitSelectionMode();

    await documentListPage.selectAllCheckbox.click();
    await expect(documentListPage.selectionCount).toHaveText('2 documents selected');
  });

  test('bulk-assigns a tag to the selected documents', async ({ request, documentListPage, uid }) => {
    const path = `bulk-tag-${uid}`;
    await createTagViaApi(request, path);
    const [pdf, image] = await Promise.all([
      uploadDocumentViaApi(request, await makePdf(`bulk-${uid}`, 'Bulk')),
      uploadDocumentViaApi(request, makePng(`bulk-photo-${uid}`)),
    ]);

    await documentListPage.goto();
    await documentListPage.switchToTableView();
    await documentListPage.search(uid);
    await documentListPage.select(pdf.name);
    await documentListPage.select(image.name);
    await documentListPage.bulkAdd('Tags', path);

    for (const doc of [pdf, image]) {
      expect((await getDocumentViaApi(request, doc.id)).tags.map((t) => t.path), doc.name).toEqual([path]);
    }
  });

  test('bulk-assigns a party to the selected documents', async ({ request, documentListPage, uid }) => {
    const party = `Acme ${uid}`;
    await createPartyViaApi(request, party);
    const [pdf, image] = await Promise.all([
      uploadDocumentViaApi(request, await makePdf(`bulk-${uid}`, 'Bulk')),
      uploadDocumentViaApi(request, makePng(`bulk-photo-${uid}`)),
    ]);

    await documentListPage.goto();
    await documentListPage.switchToTableView();
    await documentListPage.search(uid);
    await documentListPage.select(pdf.name);
    await documentListPage.select(image.name);
    await documentListPage.bulkAdd('Parties', party);

    for (const doc of [pdf, image]) {
      expect((await getDocumentViaApi(request, doc.id)).parties.map((p) => p.name), doc.name).toEqual([party]);
    }
  });

  test.describe('moving to trash', { lock: LOCKS.documentsTrash }, () => {
    test('bulk-deletes the selected documents into the trash', async ({ request, documentListPage, trashListPage, uid }) => {
      const [kept, trashed] = await Promise.all([
        uploadDocumentViaApi(request, await makePdf(`kept-${uid}`, 'Kept')),
        uploadDocumentViaApi(request, await makePdf(`trashed-${uid}`, 'Trashed')),
      ]);

      await documentListPage.goto();
    await documentListPage.switchToTableView();
    await documentListPage.search(uid);
      await documentListPage.select(trashed.name);
      await documentListPage.trashSelected();

      await expect(documentListPage.document(trashed.name)).toHaveCount(0);
      await expect(documentListPage.document(kept.name)).toBeVisible();

      await trashListPage.goto();
      await expect(trashListPage.document(trashed.name)).toBeVisible();
    });
  });
});
