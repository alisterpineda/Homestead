import { test, expect } from '../../fixtures/documents.fixture';
import { getActivityViaApi, patchDocumentViaApi, uploadDocumentViaApi } from '../../helpers/api/documents';
import { makePdf } from '../../helpers/files';

test.describe('Documents — activity', () => {
  test('a posted comment joins the activity stream alongside the recorded changes', async ({
    request,
    documentDetailPage,
    uid,
  }) => {
    const doc = await uploadDocumentViaApi(request, await makePdf(`warranty-${uid}`, 'Dishwasher warranty'));
    await patchDocumentViaApi(request, doc.id, [{ op: 'replace', path: '/notes', value: 'Keep until 2030' }]);
    const comment = `Paper copy is in the blue binder ${uid}`;

    await documentDetailPage.goto(doc.id);
    await documentDetailPage.openTab('Activity');
    const activity = documentDetailPage.activePanel;
    await expect(activity.getByText(/created this document/)).toBeVisible();
    await expect(activity.getByText(/updated notes/)).toBeVisible();

    await documentDetailPage.postComment(comment);
    await expect(activity.getByText(comment, { exact: true })).toBeVisible();
    await expect(activity.getByText(/updated notes/)).toBeVisible();

    // One chronological feed, newest first: the comment now heads it.
    const [newest] = await getActivityViaApi(request, doc.id);
    expect(newest).toMatchObject({ type: 'comment', body: comment });
  });
});
