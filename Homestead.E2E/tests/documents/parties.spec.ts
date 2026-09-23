import { test, expect } from '../../fixtures/documents.fixture';
import {
  createPartyViaApi,
  findPartiesViaApi,
  getDocumentViaApi,
  patchDocumentViaApi,
  uploadDocumentViaApi,
} from '../../helpers/api/documents';
import { makePdf } from '../../helpers/files';

test.describe('Documents — parties', () => {
  test('creates a party from the parties page', async ({ page, request, partyListPage, uid }) => {
    const name = `Acme Insurance ${uid}`;
    const description = `Home and auto policy ${uid}`;

    await partyListPage.goto();
    await partyListPage.createParty(name, { description });

    await partyListPage.search(uid);
    await expect(partyListPage.party(name)).toBeVisible();
    await expect(page.getByRole('main').getByText(description, { exact: true })).toBeVisible();
    expect(await findPartiesViaApi(request, uid)).toEqual([expect.objectContaining({ name, description })]);
  });

  test('renaming a party renames it on the documents it is linked to', async ({
    request,
    partyListPage,
    documentDetailPage,
    uid,
  }) => {
    const oldName = `Landlord ${uid}`;
    const newName = `Maple Property Management ${uid}`;
    const party = await createPartyViaApi(request, oldName);
    const lease = await uploadDocumentViaApi(request, await makePdf(`lease-${uid}`, 'Residential lease'));
    await patchDocumentViaApi(request, lease.id, [{ op: 'replace', path: '/partyIds', value: [party.id] }]);

    await partyListPage.goto();
    await partyListPage.search(uid);
    await partyListPage.renameParty(oldName, newName);

    await expect(partyListPage.party(newName)).toBeVisible();
    await expect(partyListPage.party(oldName)).toHaveCount(0);

    // The link is by id, so the document now shows the new name without being touched.
    await documentDetailPage.goto(lease.id);
    await expect(documentDetailPage.activePanel.getByText(newName, { exact: true })).toBeVisible();
    await expect(documentDetailPage.activePanel.getByText(oldName, { exact: true })).toHaveCount(0);
  });

  test('deleting a party unlinks it from its documents and leaves the documents alone', async ({
    request,
    partyListPage,
    uid,
  }) => {
    const name = `Old Insurer ${uid}`;
    const party = await createPartyViaApi(request, name);
    const policy = await uploadDocumentViaApi(request, await makePdf(`policy-${uid}`, 'Expired policy'));
    await patchDocumentViaApi(request, policy.id, [{ op: 'replace', path: '/partyIds', value: [party.id] }]);

    await partyListPage.goto();
    await partyListPage.search(uid);
    await partyListPage.select(name);
    await partyListPage.deleteSelected();

    await expect(partyListPage.party(name)).toHaveCount(0);
    expect(await findPartiesViaApi(request, uid)).toEqual([]);
    const after = await getDocumentViaApi(request, policy.id);
    expect(after.name).toBe(policy.name);
    expect(after.parties).toEqual([]);
  });

  test('links a party to a document from its detail page', async ({ page, request, documentDetailPage, uid }) => {
    const name = `Dr Smith ${uid}`;
    await createPartyViaApi(request, name);
    const referral = await uploadDocumentViaApi(request, await makePdf(`referral-${uid}`, 'Specialist referral'));

    await documentDetailPage.goto(referral.id);
    await documentDetailPage.addParty(name);
    await documentDetailPage.save();

    expect((await getDocumentViaApi(request, referral.id)).parties.map((p) => p.name)).toEqual([name]);
    await page.reload();
    await documentDetailPage.waitForLoaded();
    await expect(documentDetailPage.activePanel.getByText(name, { exact: true })).toBeVisible();
  });
});
