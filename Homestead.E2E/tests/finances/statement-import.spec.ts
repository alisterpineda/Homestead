import { test, expect } from '../../fixtures/finances.fixture';
import { DISPLAY_CURRENCY } from '../../e2e.config';
import { createAccountViaApi, findCurrencyId, listAccountFlowsViaApi } from '../../helpers/api/finances';
import { isoDateDaysAgo } from '../../helpers/date.helpers';
import { makePdf, type FilePayload } from '../../helpers/files';
import { formatBalance } from '../../pages/finances/account-detail.page';

/** A bank-export CSV. Its rows carry `uid`, so its bytes are unique to the test. */
function makeStatementCsv(baseName: string, uid: string): FilePayload {
  const csv = ['Date,Description,Amount', `2026-01-05,COFFEE ${uid},-4.50`, `2026-01-06,PAYROLL ${uid},2500.00`].join('\n');
  return { name: `${baseName}.csv`, mimeType: 'text/csv', buffer: Buffer.from(`${csv}\n`, 'utf8') };
}

// Statement parsing is AI-only (there is no deterministic CSV/OFX parser), and the E2E stack runs
// without AI configured. These tests therefore cover how the import dialog explains each
// rejection; the review → commit flow needs an AI stub in the stack to be testable end to end.
test.describe('Finances — statement import', () => {
  test('a CSV statement is rejected inline because it has no text to import', async ({
    request,
    accountDetailPage,
    uid,
  }) => {
    const cad = await findCurrencyId(request, DISPLAY_CURRENCY);
    const chequing = await createAccountViaApi(request, {
      name: `Chequing ${uid}`,
      kind: 'Cash',
      reportingCurrencyId: cad,
      initialBalance: { amount: 1000, asOfDate: isoDateDaysAgo(30) },
    });
    const statement = makeStatementCsv(`statement-${uid}`, uid);
    const dialog = accountDetailPage.statementImport;

    await accountDetailPage.gotoById(chequing.id);
    await dialog.open();
    await dialog.uploadAndImport(statement);

    await expect(dialog.alert).toContainText('This document has no extracted text, so there is nothing to import.');
    await expect(dialog.alert).toContainText('CSV and spreadsheet statements are not supported.');
    // The document was filed, so the dialog links to it...
    await expect(dialog.alert.getByRole('link', { name: new RegExp(`^Open statement-${uid}`) })).toBeVisible();
    // ...and won't re-upload the same bytes.
    await expect(dialog.importButton).toBeDisabled();
    await expect(dialog.heading).toBeVisible();

    expect(await listAccountFlowsViaApi(request, chequing.id)).toHaveLength(0);
  });

  test('a PDF statement fails to parse with AI unavailable, and nothing is imported', async ({
    request,
    accountDetailPage,
    uid,
  }) => {
    const cad = await findCurrencyId(request, DISPLAY_CURRENCY);
    const card = await createAccountViaApi(request, {
      name: `Visa ${uid}`,
      kind: 'CreditCard',
      reportingCurrencyId: cad,
      initialBalance: { amount: 250, asOfDate: isoDateDaysAgo(30) },
    });
    const statement = await makePdf(`statement-${uid}`, `Visa statement ${uid} - closing balance 312.40`);
    const dialog = accountDetailPage.statementImport;

    await accountDetailPage.gotoById(card.id);
    await dialog.open();
    await dialog.uploadAndImport(statement);

    await expect(dialog.alert).toContainText('AI is not configured; statement parsing is unavailable.', {
      timeout: 30_000,
    });
    // Opening the document wouldn't help, so there's no link this time.
    await expect(dialog.alert.getByRole('link')).toHaveCount(0);
    await expect(dialog.heading).toBeVisible();

    await dialog.page.keyboard.press('Escape');
    await expect(dialog.heading).toBeHidden();
    await expect(accountDetailPage.balanceText()).toHaveText(formatBalance(250));
    await expect(accountDetailPage.activityRows()).toHaveCount(1);
  });
});
