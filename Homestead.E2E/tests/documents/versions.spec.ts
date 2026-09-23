import { test, expect } from '../../fixtures/documents.fixture';
import { addVersionViaApi, getDocumentViaApi, uploadDocumentViaApi } from '../../helpers/api/documents';
import { waitForJob } from '../../helpers/api/processing';
import { makePdf } from '../../helpers/files';

test.describe('Documents — versions', () => {
  test('adds a labelled version, and the version switcher shows each version with its own content', async ({
    page,
    request,
    documentDetailPage,
    uid,
  }) => {
    const draftTerm = `draftclause${uid}`;
    const signedTerm = `signedclause${uid}`;
    const doc = await uploadDocumentViaApi(request, await makePdf(`lease-${uid}`, `Lease draft ${draftTerm}`));
    const [v1] = (await getDocumentViaApi(request, doc.id)).versions;

    await documentDetailPage.goto(doc.id);
    await expect(documentDetailPage.versionSwitcher).toHaveCount(0);
    const jobId = await documentDetailPage.addVersion(
      await makePdf(`lease-signed-${uid}`, `Lease signed ${signedTerm}`),
      'Signed copy',
    );
    expect((await waitForJob(request, jobId)).status).toBe('Completed');

    // The new upload becomes the latest version; document-scoped metadata carries over unchanged.
    await expect(documentDetailPage.versionSwitcher).toHaveText('Latest - Signed copy');
    await expect(documentDetailPage.nameInput).toHaveValue(doc.name);
    await expect(documentDetailPage.versionLabelInput).toHaveValue('Signed copy');
    await documentDetailPage.openTab('Content');
    await expect(documentDetailPage.extractedTextInput).toHaveValue(new RegExp(signedTerm));

    await documentDetailPage.selectVersion('v1');
    await expect(page).toHaveURL(new RegExp(`/documents/${doc.id}/versions/${v1.id}$`));
    await expect(documentDetailPage.extractedTextInput).toHaveValue(new RegExp(draftTerm));

    const { versions } = await getDocumentViaApi(request, doc.id);
    expect(versions.map((v) => [v.versionNumber, v.label])).toEqual(expect.arrayContaining([[1, null], [2, 'Signed copy']]));
    expect(versions).toHaveLength(2);
  });

  test('deep-linking to an older version opens that version', async ({ request, documentDetailPage, uid }) => {
    const doc = await uploadDocumentViaApi(request, await makePdf(`passport-${uid}`, 'Passport scan'));
    const [v1] = (await getDocumentViaApi(request, doc.id)).versions;
    await addVersionViaApi(request, doc.id, await makePdf(`passport-rescan-${uid}`, 'Passport rescan'), 'Rescan');

    await documentDetailPage.gotoVersion(doc.id, v1.id);
    await expect(documentDetailPage.versionSwitcher).toHaveText('v1');
    await expect(documentDetailPage.versionLabelInput).toHaveValue('');

    await documentDetailPage.selectVersion('Latest - Rescan');
    await expect(documentDetailPage.page).toHaveURL(new RegExp(`/documents/${doc.id}$`));
    await expect(documentDetailPage.versionLabelInput).toHaveValue('Rescan');
  });

  test("a new version's text makes the document findable, without creating a second document", async ({
    request,
    documentListPage,
    uid,
  }) => {
    const term = `addendum${uid}`;
    const doc = await uploadDocumentViaApi(request, await makePdf(`tenancy-${uid}`, 'Original tenancy terms'));
    await addVersionViaApi(request, doc.id, await makePdf(`tenancy-addendum-${uid}`, `Pet ${term} clause`), 'Addendum');

    await documentListPage.goto();
    await documentListPage.search(term);
    await expect(documentListPage.document(doc.name)).toBeVisible();
    await expect(documentListPage.document(`tenancy-addendum-${uid}`)).toHaveCount(0);
  });
});
