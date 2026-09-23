import { type Locator, type Page, expect } from '@playwright/test';
import { expectOk } from '../../helpers/playwright.helpers';
import { step } from '../../helpers/step';

const NOTES_API = /\/api\/v1\/inventory\/items\/[0-9a-f-]+\/activity\/notes(\/[0-9a-f-]+)?$/;

/** The Activity section at the foot of an item's detail page: the audit + notes feed and its composer. */
export class ItemActivityFeed {
  readonly root: Locator;
  /** The header composer's date chip — "Date: Today" until a date is picked, then "Date: Mar 12, 2025". */
  readonly dateChip: Locator;

  constructor(readonly page: Page) {
    this.root = page.locator('section').filter({
      has: page.getByRole('heading', { name: 'Activity', exact: true }),
    });
    this.dateChip = this.headerComposer().getByRole('button', { name: /^Date: / });
  }

  private headerComposer(): Locator {
    return this.root.locator('app-item-note-composer').first();
  }

  /** The row composer that replaces a note while it is being edited (it has Save / Cancel). */
  private editComposer(): Locator {
    return this.root
      .locator('app-item-note-composer')
      .filter({ has: this.page.getByRole('button', { name: 'Save', exact: true }) });
  }

  /** A note row, found by its body text. */
  note(body: string): Locator {
    return this.root
      .locator('div')
      .filter({ has: this.page.getByText(body, { exact: true }) })
      .filter({ has: this.page.getByRole('button', { name: 'Note actions' }) })
      .last();
  }

  /**
   * Picks the header composer's date. The chip opens the browser's native date picker, which a
   * test can't drive, so the date goes straight into the (visually hidden) native input behind it.
   */
  @step()
  async pickDate(isoDate: string): Promise<void> {
    await this.headerComposer().locator('input[type="date"]').fill(isoDate);
  }

  /** Types a note and presses Enter (the composer's submit). */
  @step()
  async addNote(body: string): Promise<void> {
    const input = this.headerComposer().getByRole('textbox', { name: 'Note' });
    await input.fill(body);
    const responsePromise = this.page.waitForResponse(
      (resp) => NOTES_API.test(resp.url()) && resp.request().method() === 'POST',
    );
    await input.press('Enter');
    await expectOk(await responsePromise);
    await expect(this.note(body)).toBeVisible();
  }

  @step()
  async editNote(body: string, newBody: string): Promise<void> {
    await this.note(body).getByRole('button', { name: 'Note actions' }).click();
    await this.page.getByRole('menuitem', { name: 'Edit' }).click();
    const editor = this.editComposer();
    const input = editor.getByRole('textbox', { name: 'Note' });
    await expect(input).toHaveValue(body);
    await input.fill(newBody);

    const responsePromise = this.page.waitForResponse(
      (resp) => NOTES_API.test(resp.url()) && resp.request().method() === 'PUT',
    );
    await editor.getByRole('button', { name: 'Save', exact: true }).click();
    await expectOk(await responsePromise);
    await expect(editor).toHaveCount(0);
  }

  @step()
  async deleteNote(body: string): Promise<void> {
    await this.note(body).getByRole('button', { name: 'Note actions' }).click();
    await this.page.getByRole('menuitem', { name: 'Delete' }).click();
    const confirm = this.page.getByRole('alertdialog');
    const heading = confirm.getByRole('heading', { name: 'Delete this note?' });
    await expect(heading).toBeVisible();

    const responsePromise = this.page.waitForResponse(
      (resp) => NOTES_API.test(resp.url()) && resp.request().method() === 'DELETE',
    );
    await confirm.getByRole('button', { name: 'Delete', exact: true }).click();
    await expectOk(await responsePromise);
    await expect(heading).toBeHidden();
  }
}
