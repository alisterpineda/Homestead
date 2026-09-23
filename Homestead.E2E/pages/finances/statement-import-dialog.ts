import { type Locator, type Page, expect } from '@playwright/test';
import { step } from '../../helpers/step';
import type { FilePayload } from '../../helpers/files';

/** The "Import statement" dialog on a (non-Investment) account's detail page. */
export class StatementImportDialog {
  readonly openButton: Locator;
  /** The dialog role sits on a zero-size CDK container; assert on {@link heading} instead. */
  readonly dialog: Locator;
  readonly heading: Locator;
  readonly importButton: Locator;
  /** The inline failure (or already-imported warning) the dialog keeps on screen. */
  readonly alert: Locator;

  constructor(readonly page: Page) {
    this.openButton = page.getByRole('button', { name: 'Import statement' });
    this.dialog = page.getByRole('dialog', { name: 'Import statement' });
    this.heading = this.dialog.getByRole('heading', { name: 'Import statement' });
    this.importButton = this.dialog.getByRole('button', { name: 'Import', exact: true });
    this.alert = this.dialog.getByRole('alert');
  }

  @step()
  async open(): Promise<void> {
    await this.openButton.click();
    await expect(this.heading).toBeVisible();
  }

  /**
   * Uploads `file` as a new document and starts the import. Returns once the document is on file;
   * the dialog then waits on its processing and the statement parse, ending in the review page or
   * an inline {@link alert}.
   */
  @step()
  async uploadAndImport(file: FilePayload): Promise<void> {
    const fileChooser = this.page.waitForEvent('filechooser');
    await this.dialog.getByText('Drop a file here, or click to browse').click();
    await (await fileChooser).setFiles(file);
    await expect(this.dialog.getByText(file.name, { exact: true })).toBeVisible();

    const uploaded = this.page.waitForResponse(
      (resp) => /\/api\/v1\/documents$/.test(resp.url()) && resp.request().method() === 'POST',
    );
    await this.importButton.click();
    expect((await uploaded).status(), 'statement upload').toBe(202);
  }
}
