import { test, expect } from '../../fixtures/documents.fixture';
import { AUTH_STATE, KEYCLOAK_USER } from '../../e2e.config';
import { addGroupMemberViaApi, createGroupViaApi, findUserIdByEmail } from '../../helpers/api/admin';
import { patchDocumentViaApi, searchDocumentsViaApi, uploadDocumentViaApi } from '../../helpers/api/documents';
import { makePdf } from '../../helpers/files';

// Everything here runs as the contributor; `adminRequest` arranges data as the admin.
test.use({ storageState: AUTH_STATE.contributor });

test.describe('Contributor role', () => {
  test('is kept out of Admin, in the app and in the API', async ({ page, request }) => {
    await page.goto('/admin/users');
    await expect(page).toHaveURL(/\/documents/);

    expect((await request.get('/api/v1/admin/users?Page=1&PageSize=1')).status()).toBe(403);
    expect((await request.get('/api/v1/admin/groups?Page=1&PageSize=1')).status()).toBe(403);
  });

  test("sees household documents but not a group's documents until added to that group", async ({
    adminRequest,
    documentListPage,
    uid,
  }) => {
    const [household, scoped] = await Promise.all([
      uploadDocumentViaApi(adminRequest, await makePdf(`household-${uid}`, 'Household')),
      uploadDocumentViaApi(adminRequest, await makePdf(`group-only-${uid}`, 'Group only')),
    ]);
    const group = await createGroupViaApi(adminRequest, `Private ${uid}`);
    await patchDocumentViaApi(adminRequest, scoped.id, [{ op: 'replace', path: '/groupId', value: group.id }]);

    await documentListPage.goto();
    await documentListPage.search(uid);
    await expect(documentListPage.document(household.name)).toBeVisible();
    await expect(documentListPage.document(scoped.name)).toHaveCount(0);

    const contributorId = await findUserIdByEmail(adminRequest, KEYCLOAK_USER.email);
    await addGroupMemberViaApi(adminRequest, group.id, contributorId, 'Viewer');

    await documentListPage.goto();
    await documentListPage.search(uid);
    await expect(documentListPage.document(household.name)).toBeVisible();
    await expect(documentListPage.document(scoped.name)).toBeVisible();
  });

  test("can't open a group-scoped document by URL either", async ({ page, request, adminRequest, uid }) => {
    const scoped = await uploadDocumentViaApi(adminRequest, await makePdf(`hidden-${uid}`, 'Hidden'));
    const group = await createGroupViaApi(adminRequest, `Private ${uid}`);
    await patchDocumentViaApi(adminRequest, scoped.id, [{ op: 'replace', path: '/groupId', value: group.id }]);

    expect((await request.get(`/api/v1/documents/${scoped.id}`)).status()).toBe(403);
    // Wait for the page's own load to be refused, so the absence check runs against the settled page
    // rather than passing instantly while it is still loading.
    const load = page.waitForResponse(
      (r) => r.request().method() === 'GET' && new URL(r.url()).pathname === `/api/v1/documents/${scoped.id}`,
    );
    await page.goto(`/documents/${scoped.id}`);
    expect((await load).status()).toBe(403);
    await expect(page.getByRole('textbox', { name: 'Name', exact: true })).toHaveCount(0);
  });

  test('can upload a document, and the admin sees it', async ({ adminRequest, documentListPage, uid }) => {
    const file = await makePdf(`contributed-${uid}`, 'Contributed');

    await documentListPage.goto();
    const jobId = await documentListPage.upload(file);
    await expect
      .poll(async () => (await searchDocumentsViaApi(adminRequest, `contributed-${uid}`)).map((d) => d.name), {
        message: `upload job ${jobId} produces the document`,
        timeout: 60_000,
      })
      .toContain(`contributed-${uid}`);
  });
});
