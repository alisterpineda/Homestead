import * as fs from 'node:fs';
import { test, expect } from '../../fixtures/documents.fixture';
import { LOCKS } from '../../e2e.config';
import { createPartyViaApi, patchDocumentViaApi, uploadDocumentViaApi } from '../../helpers/api/documents';
import { makePdf, makePng } from '../../helpers/files';
import { stubFailure } from '../../helpers/network';

/** The entry names in a ZIP archive, read from its central directory. */
function zipEntryNames(zip: Buffer): string[] {
  const endOfCentralDirectory = zip.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06]));
  expect(endOfCentralDirectory, 'ZIP end-of-central-directory record').toBeGreaterThanOrEqual(0);
  const count = zip.readUInt16LE(endOfCentralDirectory + 10);
  const names: string[] = [];
  let at = zip.readUInt32LE(endOfCentralDirectory + 16);
  for (let i = 0; i < count; i++) {
    const nameLength = zip.readUInt16LE(at + 28);
    const extraLength = zip.readUInt16LE(at + 30);
    const commentLength = zip.readUInt16LE(at + 32);
    names.push(zip.toString('utf8', at + 46, at + 46 + nameLength));
    at += 46 + nameLength + extraLength + commentLength;
  }
  return names;
}

test.describe('Documents — download', () => {
  test("downloads the document's file from its detail page", async ({ request, documentDetailPage, uid }) => {
    const file = await makePdf(`receipt-${uid}`, 'Hardware store receipt');
    const doc = await uploadDocumentViaApi(request, file);

    await documentDetailPage.goto(doc.id);
    const download = await documentDetailPage.download();

    expect(download.suggestedFilename()).toBe(`${doc.name}.pdf`);
    expect(fs.readFileSync(await download.path()).equals(file.buffer), 'downloaded bytes match the upload').toBe(
      true,
    );
  });

  test("downloads the file even when the page couldn't load its preview", async ({
    page,
    request,
    documentDetailPage,
    uid,
  }) => {
    const file = await makePdf(`invoice-${uid}`, 'Plumber invoice');
    const doc = await uploadDocumentViaApi(request, file);
    // Fails the preview's fetch (`…/file`) but not the download itself (`…/file?download=true`).
    const previewPath = `/api/v1/documents/${doc.id}/file`;
    await stubFailure(page, new RegExp(`${previewPath}$`));
    const preview = page.waitForResponse((resp) => new URL(resp.url()).pathname === previewPath);

    await documentDetailPage.goto(doc.id);
    expect((await preview).status(), 'the preview fetch fails').toBe(500);
    const download = await documentDetailPage.download();

    expect(download.suggestedFilename()).toBe(`${doc.name}.pdf`);
    expect(fs.readFileSync(await download.path()).equals(file.buffer), 'downloaded bytes match the upload').toBe(
      true,
    );
  });

  // Each user may have only one ZIP download in flight, and every spec signs in as the same admin.
  test.describe('as a ZIP', { lock: LOCKS.documentsDownload }, () => {
    test('downloads the selected documents as a ZIP filed by tag, with party names in the file names', async ({
      request,
      documentListPage,
      uid,
    }) => {
      const tag = `finance-${uid}`;
      const party = `Northwind Bank ${uid}`;
      const { id: partyId } = await createPartyViaApi(request, party);
      const [statement, photo] = await Promise.all([
        uploadDocumentViaApi(request, await makePdf(`statement-${uid}`, 'Monthly statement')),
        uploadDocumentViaApi(request, makePng(`photo-${uid}`)),
      ]);
      await patchDocumentViaApi(request, statement.id, [
        { op: 'replace', path: '/tagPaths', value: [tag] },
        { op: 'replace', path: '/partyIds', value: [partyId] },
      ]);

      await documentListPage.goto();
      await documentListPage.switchToTableView();
      await documentListPage.search(uid);
      await documentListPage.select(statement.name);
      await documentListPage.select(photo.name);
      const download = await documentListPage.downloadSelectedAsZip();

      expect(download.suggestedFilename()).toMatch(/^Homestead-Download-\d{4}-\d{2}-\d{2}\.zip$/);
      const entries = zipEntryNames(fs.readFileSync(await download.path()));
      expect([...entries].sort()).toEqual(
        [`${tag}/${statement.name} - ${party}.pdf`, `_Untagged/${photo.name}.png`].sort(),
      );
    });
  });
});
