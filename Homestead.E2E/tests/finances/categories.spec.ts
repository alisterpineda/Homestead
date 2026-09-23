import { test, expect } from '../../fixtures/finances.fixture';
import { DISPLAY_CURRENCY } from '../../e2e.config';
import {
  createAccountViaApi,
  createCategoryViaApi,
  createFlowViaApi,
  findCurrencyId,
  getFlowViaApi,
  listCategoriesViaApi,
} from '../../helpers/api/finances';
import { isoDateDaysAgo } from '../../helpers/date.helpers';

test.describe('Finances — categories', () => {
  test('creates a category and renames it', async ({ request, categoryListPage, uid }) => {
    const name = `Dining ${uid}`;
    const renamed = `Restaurants ${uid}`;

    await categoryListPage.goto();
    await categoryListPage.create({ type: 'Expense', name });
    await expect(categoryListPage.node(name)).toContainText('Expense');

    await categoryListPage.rename(name, renamed);
    await expect(categoryListPage.node(renamed)).toBeVisible();
    await expect(categoryListPage.node(name)).toHaveCount(0);

    const saved = (await listCategoriesViaApi(request)).filter((c) => c.name.endsWith(uid));
    expect(saved).toEqual([expect.objectContaining({ name: renamed, type: 'Expense', parentId: null })]);
  });

  test('a duplicate name is rejected inline and the dialog stays open', async ({ request, categoryListPage, uid }) => {
    const existing = await createCategoryViaApi(request, { name: `Utilities ${uid}`, type: 'Expense' });

    await categoryListPage.goto();
    await categoryListPage.openCreateDialog();
    // The name is required before anything can be submitted.
    await expect(categoryListPage.createDialog.getByRole('button', { name: 'Create category' })).toBeDisabled();
    await categoryListPage.submitCreate({ type: 'Expense', name: existing.name });

    await expect(categoryListPage.createDialog.getByText(`category named '${existing.name}' already exists`)).toBeVisible();
    await expect(categoryListPage.createHeading).toBeVisible();
    expect((await listCategoriesViaApi(request)).filter((c) => c.name === existing.name)).toHaveLength(1);
  });

  test('deleting a category in use moves its transactions to Uncategorised', async ({
    request,
    categoryListPage,
    uid,
  }) => {
    const cad = await findCurrencyId(request, DISPLAY_CURRENCY);
    const chequing = await createAccountViaApi(request, {
      name: `Chequing ${uid}`,
      kind: 'Cash',
      reportingCurrencyId: cad,
      initialBalance: { amount: 1000, asOfDate: isoDateDaysAgo(30) },
    });
    const hobbies = await createCategoryViaApi(request, { name: `Hobbies ${uid}`, type: 'Expense' });
    const flow = await createFlowViaApi(request, {
      direction: 'Expense',
      accountId: chequing.id,
      amount: 30,
      date: isoDateDaysAgo(3),
      description: `Paint ${uid}`,
      categoryId: hobbies.id,
    });

    await categoryListPage.goto();
    await categoryListPage.delete(hobbies.name);

    await expect(categoryListPage.node(hobbies.name)).toHaveCount(0);
    // The flow survives, now Uncategorised.
    expect(await getFlowViaApi(request, flow.id)).toMatchObject({ amount: 30, categoryId: null });
    expect((await listCategoriesViaApi(request)).map((c) => c.id)).not.toContain(hobbies.id);
  });
});
