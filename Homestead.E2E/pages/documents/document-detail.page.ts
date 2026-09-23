import { type Download, type Locator, type Page, expect } from '@playwright/test';
import type { FilePayload } from '../../helpers/files';
import { expectOk, isMobileViewport } from '../../helpers/playwright.helpers';
import { step } from '../../helpers/step';

export class DocumentDetailPage {
  readonly nameInput: Locator;
  readonly saveButton: Locator;
  readonly downloadButton: Locator;
  readonly moreActionsButton: Locator;
  /** The breadcrumb's version dropdown — only rendered once a document has two or more versions. */
  readonly versionSwitcher: Locator;
  readonly versionLabelInput: Locator;
  readonly extractedTextInput: Locator;
  readonly commentInput: Locator;
  /** The visible tab's content (Info, Content, Metadata or Activity). */
  readonly activePanel: Locator;

  constructor(readonly page: Page) {
    this.nameInput = page.getByRole('textbox', { name: 'Name', exact: true });
    this.saveButton = page.getByRole('button', { name: /^(Save|Saving)/ });
    this.downloadButton = page.getByRole('button', { name: 'Download' });
    this.moreActionsButton = page.getByRole('button', { name: 'More actions' });
    this.versionSwitcher = page
      .getByRole('navigation', { name: 'breadcrumb' })
      .getByRole('button', { name: /^(Latest|v\d+)\b/ });
    this.versionLabelInput = page.getByRole('textbox', { name: 'Label', exact: true });
    this.extractedTextInput = page.getByRole('textbox', { name: 'Extracted Text' });
    this.commentInput = page.getByRole('textbox', { name: 'Add a comment' });
    this.activePanel = page.getByRole('tabpanel');
  }

  async goto(documentId: string): Promise<void> {
    await this.page.goto(`/documents/${documentId}`);
    await this.waitForLoaded();
  }

  /** Deep-links to one version of a document (`/documents/:id/versions/:versionId`). */
  async gotoVersion(documentId: string, versionId: string): Promise<void> {
    await this.page.goto(`/documents/${documentId}/versions/${versionId}`);
    await this.waitForLoaded();
  }

  async waitForLoaded(): Promise<void> {
    await expect(this.page).toHaveURL(/\/documents\/[0-9a-f-]+/);
    // Phones get a tabbed layout that opens on Preview; the editable fields live on Info.
    if (isMobileViewport(this.page)) {
      await this.page.getByRole('tab', { name: 'Info' }).click();
    }
    await expect(this.nameInput).toBeVisible({ timeout: 15_000 });
  }

  /** Saves pending edits and waits for the PATCH to succeed. */
  @step()
  async save(): Promise<void> {
    const saveResponse = this.page.waitForResponse(
      (resp) => /\/api\/v1\/documents\/[0-9a-f-]+$/.test(resp.url()) && resp.request().method() === 'PATCH',
    );
    await expect(this.saveButton).toBeEnabled();
    await this.saveButton.click();
    await expectOk(await saveResponse);
    await expect(this.saveButton).toBeDisabled();
  }

  /** Adds an existing tag through the Tags field's autocomplete (not yet saved). */
  @step()
  async addTag(path: string): Promise<void> {
    const tagInput = this.page.getByRole('textbox', { name: 'Add tags...' });
    const suggestions = this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/documents/tags?') &&
        new URL(resp.url()).searchParams.get('Search') === path &&
        resp.status() === 200,
    );
    await tagInput.pressSequentially(path);
    await suggestions;
    await this.page.getByRole('button', { name: path, exact: true }).click();
  }

  /** Switches the side panel (desktop) or the page (phones) to one of the document's tabs. */
  @step()
  async openTab(name: 'Info' | 'Content' | 'Metadata' | 'Activity'): Promise<void> {
    const tab = this.page.getByRole('tab', { name, exact: true });
    await tab.click();
    await expect(tab).toHaveAttribute('aria-selected', 'true');
  }

  /** Adds an existing party through the Parties field's autocomplete (not yet saved). */
  @step()
  async addParty(name: string): Promise<void> {
    const partyInput = this.page.getByRole('textbox', { name: 'Add parties...' });
    const suggestions = this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/documents/parties?') &&
        new URL(resp.url()).searchParams.get('Search') === name &&
        resp.status() === 200,
    );
    await partyInput.pressSequentially(name);
    await suggestions;
    await this.page.getByRole('button', { name, exact: true }).click();
  }

  /**
   * Uploads `file` as a new version through ⋮ More actions → Add Version; resolves to the
   * processing job's id once the upload is accepted and the dialog has closed.
   */
  @step()
  async addVersion(file: FilePayload, label?: string): Promise<string> {
    await this.moreActionsButton.click();
    await this.page.getByRole('menuitem', { name: 'Add Version' }).click();
    const dialog = this.page.getByRole('dialog', { name: 'Add Version' });
    const heading = dialog.getByRole('heading', { name: 'Add Version' });
    await expect(heading).toBeVisible();

    if (label) {
      await dialog.getByLabel('Label (optional)').fill(label);
    }
    const fileChooser = this.page.waitForEvent('filechooser');
    await dialog.getByText('Drop a file here, or click to browse').click();
    await (await fileChooser).setFiles(file);
    await expect(dialog.getByText(file.name, { exact: true })).toBeVisible();

    const uploadResponse = this.page.waitForResponse(
      (resp) => /\/api\/v1\/documents\/[0-9a-f-]+\/versions$/.test(resp.url()) && resp.request().method() === 'POST',
    );
    await dialog.getByRole('button', { name: 'Upload' }).click();
    const response = await uploadResponse;
    expect(response.status(), `upload version ${file.name}`).toBe(202);
    await expect(heading).toBeHidden();
    return (await response.json()).jobId as string;
  }

  /** Picks an entry ("Latest", "Latest - <label>", "v1", "v1 - <label>", …) in the version dropdown. */
  @step()
  async selectVersion(entry: string): Promise<void> {
    await this.versionSwitcher.click();
    await this.page.getByRole('menuitem', { name: entry, exact: true }).click();
    await expect(this.versionSwitcher).toHaveText(entry);
  }

  /** Posts a comment from the Activity tab and waits until the composer has cleared. */
  @step()
  async postComment(body: string): Promise<void> {
    await this.commentInput.fill(body);
    const commentResponse = this.page.waitForResponse(
      (resp) => /\/api\/v1\/documents\/[0-9a-f-]+\/comments$/.test(resp.url()) && resp.request().method() === 'POST',
    );
    await this.page.getByRole('button', { name: 'Send comment' }).click();
    await expectOk(await commentResponse);
    await expect(this.commentInput).toHaveValue('');
  }

  /**
   * Clicks Download and resolves to the browser download. The button hands over the file the page
   * has already fetched for its preview, and does nothing until that fetch lands — so retry the
   * click until a download starts.
   *
   * Headless Chromium doesn't render PDFs inline: when the preview iframe loads its blob it fires a
   * download of its own, named after the blob's UUID. Those are ignored.
   */
  @step()
  async download(): Promise<Download> {
    let download: Download | undefined;
    await expect(async () => {
      const started = this.page.waitForEvent('download', {
        predicate: (d) => !/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}(\.\w+)?$/.test(d.suggestedFilename()),
        timeout: 2_000,
      });
      await this.downloadButton.click();
      download = await started;
    }).toPass();
    return download!;
  }
}
