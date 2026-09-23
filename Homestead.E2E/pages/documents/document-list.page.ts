import { type Download, type Locator, type Page, type Response, expect } from '@playwright/test';
import { expectOk, isMobileViewport } from '../../helpers/playwright.helpers';
import { step } from '../../helpers/step';

const isDocumentsList = (resp: Response) =>
  /\/api\/v1\/documents\?/.test(resp.url()) && resp.request().method() === 'GET' && resp.status() === 200;

export class DocumentListPage {
  readonly searchInput: Locator;
  readonly uploadButton: Locator;
  readonly uploadEmptyStateButton: Locator;
  readonly noDocsEmpty: Locator;
  readonly noMatchingEmpty: Locator;
  readonly filterButton: Locator;
  readonly selectAllCheckbox: Locator;
  readonly selectionCount: Locator;

  constructor(readonly page: Page) {
    this.searchInput = page.getByRole('textbox', { name: 'Search documents' });
    this.uploadButton = page.getByRole('button', { name: 'Upload', exact: true });
    this.uploadEmptyStateButton = page.getByRole('button', { name: 'Upload Document' });
    this.noDocsEmpty = page.getByText('No documents yet');
    this.noMatchingEmpty = page.getByText('No matching documents');
    this.filterButton = page.getByRole('button', { name: 'Filter documents' });
    this.selectAllCheckbox = page.getByRole('checkbox', { name: 'Select all' });
    this.selectionCount = page.getByText(/\d+ documents? selected/);
  }

  async goto(): Promise<void> {
    const responsePromise = this.page.waitForResponse(isDocumentsList);
    await this.page.goto('/documents');
    await responsePromise;
  }

  /** Searches and waits for the server's answer to exactly this term. */
  @step()
  async search(term: string): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) => isDocumentsList(resp) && new URL(resp.url()).searchParams.get('Search') === term,
    );
    await this.searchInput.fill(term);
    await expect(this.page).toHaveURL(/[?&]search=/);
    await responsePromise;
  }

  @step()
  async clearSearch(): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) => isDocumentsList(resp) && !new URL(resp.url()).searchParams.has('Search'),
    );
    await this.searchInput.fill('');
    await expect(this.page).not.toHaveURL(/[?&]search=/);
    await responsePromise;
  }

  /** A document's name as listed, in either the grid or the table view. */
  document(name: string): Locator {
    return this.page.getByRole('main').getByText(name, { exact: true });
  }

  /** Uploads through the toolbar's Upload popover; resolves to the upload job's id. */
  @step()
  async upload(file: { name: string; mimeType: string; buffer: Buffer }): Promise<string> {
    await this.uploadButton.click();
    const chooseFiles = this.page.getByRole('button', { name: 'Choose files' });
    await expect(chooseFiles).toBeVisible();

    const uploadResponse = this.page.waitForResponse(
      (resp) => /\/api\/v1\/documents$/.test(resp.url()) && resp.request().method() === 'POST',
    );
    const fileChooser = this.page.waitForEvent('filechooser');
    await chooseFiles.click();
    await (await fileChooser).setFiles(file);
    const response = await uploadResponse;
    expect(response.status(), `upload ${file.name}`).toBe(202);
    return (await response.json()).jobId as string;
  }

  /**
   * Picks a tag in the Filters panel (a popover on desktop, a drawer on phones), waits for the
   * filtered list, then closes the panel.
   */
  @step()
  async filterByTag(path: string): Promise<void> {
    await this.filterButton.click();
    const panelTitle = this.page.getByText('Filters', { exact: true });
    const search = this.page.getByPlaceholder('Search tags...');
    // On phones the drawer slides in; click() waits for it to settle so the options anchor correctly.
    await search.click();

    const options = this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/documents/tags?') &&
        new URL(resp.url()).searchParams.get('Search') === path &&
        resp.status() === 200,
    );
    await search.fill(path);
    await options;

    const filtered = this.page.waitForResponse(
      (resp) => isDocumentsList(resp) && new URL(resp.url()).searchParams.has('TagIds'),
    );
    await this.page.getByRole('option', { name: path, exact: true }).click();
    await filtered;

    // Escape with focus still in the tag combobox clears the pick on phones, so move focus to the
    // panel first. The first Escape can land on the combobox list while it is still closing, so
    // retry until the panel itself is gone.
    await panelTitle.click();
    await expect(async () => {
      await this.page.keyboard.press('Escape');
      await expect(panelTitle).toBeHidden({ timeout: 1_000 });
    }).toPass();
  }

  /** Switches to the table view: a toggle on desktop, an overflow-menu choice on phones. */
  @step()
  async switchToTableView(): Promise<void> {
    if (isMobileViewport(this.page)) {
      await this.page.getByRole('button', { name: 'More options' }).click();
      await this.page.getByRole('menuitemradio', { name: 'Table' }).click();
    } else {
      await this.page.getByRole('button', { name: 'Table view' }).click();
    }
    await expect(this.page.getByRole('table')).toBeVisible();
  }

  @step()
  async select(name: string): Promise<void> {
    const checkbox = this.page.getByRole('checkbox', { name: `Select ${name}`, exact: true });
    await checkbox.click();
    await expect(checkbox).toBeChecked();
  }

  @step()
  async exitSelectionMode(): Promise<void> {
    await this.page.getByRole('button', { name: 'Exit selection mode' }).click();
    await expect(this.selectionCount).toBeHidden();
  }

  /** Moves the selected documents to the trash through the toolbar's confirm dialog. */
  @step()
  async trashSelected(): Promise<void> {
    await this.page.getByRole('button', { name: 'Delete', exact: true }).click();
    const confirm = this.page.getByRole('alertdialog', { name: /^Delete \d+ documents?\?$/ });
    await expect(confirm.getByRole('heading')).toBeVisible();
    const deleteResponse = this.page.waitForResponse(
      (resp) => resp.url().endsWith('/api/v1/documents/bulk-delete') && resp.request().method() === 'POST',
    );
    await confirm.getByRole('button', { name: 'Delete', exact: true }).click();
    await expectOk(await deleteResponse);
  }

  /**
   * Adds a tag or party to every selected document through the toolbar's bulk-edit popover:
   * search, pick the suggestion, Apply, then Confirm.
   */
  @step()
  async bulkAdd(kind: 'Tags' | 'Parties', value: string): Promise<void> {
    await this.page.getByRole('button', { name: kind, exact: true }).click();
    const search = this.page.getByRole('textbox', { name: new RegExp(`^Search ${kind.toLowerCase()} to add`, 'i') });

    const suggestions = this.page.waitForResponse(
      (resp) =>
        resp.url().includes(`/api/v1/documents/${kind.toLowerCase()}?`) &&
        new URL(resp.url()).searchParams.get('Search') === value &&
        resp.status() === 200,
    );
    await search.pressSequentially(value);
    await suggestions;
    await this.page.getByRole('button', { name: value, exact: true }).click();

    await this.page.getByRole('button', { name: 'Apply' }).click();
    const bulkUpdate = this.page.waitForResponse(
      (resp) => resp.url().endsWith('/api/v1/documents/bulk-update') && resp.request().method() === 'POST',
    );
    await this.page.getByRole('button', { name: 'Confirm' }).click();
    await expectOk(await bulkUpdate);
  }

  /**
   * Downloads the selected documents as one ZIP: the toolbar's Download starts a background job,
   * the progress dialog waits for it, then offers the archive. Resolves to the browser download.
   */
  @step()
  async downloadSelectedAsZip(): Promise<Download> {
    const startResponse = this.page.waitForResponse(
      (resp) => resp.url().endsWith('/api/v1/documents/download') && resp.request().method() === 'POST',
    );
    await this.page.getByRole('button', { name: 'Download', exact: true }).click();
    await expectOk(await startResponse);

    const dialog = this.page.getByRole('dialog', { name: 'Download Documents' });
    await expect(dialog.getByRole('heading', { name: 'Download Documents' })).toBeVisible();
    const archiveLink = dialog.getByRole('link', { name: 'Download documents' });
    await expect(archiveLink).toBeVisible({ timeout: 30_000 });

    const download = this.page.waitForEvent('download');
    await archiveLink.click();
    return download;
  }
}
