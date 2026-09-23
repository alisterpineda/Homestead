import { test, expect } from '../../fixtures/inventory.fixture';
import { createDurableViaApi, getItemActivityViaApi } from '../../helpers/api/inventory';

test.describe('Inventory — activity notes', () => {
  test('adds a back-dated note, edits it, and deletes it', async ({ request, itemDetailPage, uid }) => {
    const car = await createDurableViaApi(request, { name: `Car ${uid}` });
    const body = `Oil change ${uid}`;
    const edited = `Oil change and filter ${uid}`;

    await itemDetailPage.gotoById(car.id);
    const activity = itemDetailPage.activity;
    await expect(activity.dateChip).toHaveAccessibleName('Date: Today');
    await activity.pickDate('2025-03-12');
    await expect(activity.dateChip).toHaveAccessibleName('Date: Mar 12, 2025');

    await activity.addNote(body);
    // A back-filled note reads as the date it happened, not "just now".
    await expect(activity.note(body)).toContainText('Mar 12, 2025');
    await expect(activity.note(body)).not.toContainText('edited');
    // The picked date stays on the chip for the next entry.
    await expect(activity.dateChip).toHaveAccessibleName('Date: Mar 12, 2025');
    const [created] = (await getItemActivityViaApi(request, car.id)).filter((r) => r.type === 'note');
    expect(created.occurredAt).toMatch(/^2025-03-12T12:00:00/);

    await activity.editNote(body, edited);
    await expect(activity.note(edited)).toContainText('edited');
    await expect(activity.note(edited)).toContainText('Mar 12, 2025');
    await expect(activity.note(body)).toHaveCount(0);

    await activity.deleteNote(edited);
    await expect(activity.note(edited)).toHaveCount(0);
    expect((await getItemActivityViaApi(request, car.id)).filter((r) => r.type === 'note')).toEqual([]);
  });
});
