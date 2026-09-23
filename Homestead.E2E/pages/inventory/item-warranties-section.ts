import { type Locator, type Page, expect } from '@playwright/test';
import { expectOk } from '../../helpers/playwright.helpers';
import { step } from '../../helpers/step';

/** The Warranties section on a durable's detail page: its composer and the recorded rows. */
export class ItemWarrantiesSection {
  readonly root: Locator;
  /** The composer's expiry chip — "Expiry: Expires" until a term is chosen. */
  readonly expiryChip: Locator;
  readonly addButton: Locator;

  constructor(readonly page: Page) {
    this.root = page.locator('section').filter({
      has: page.getByRole('heading', { name: 'Warranties', exact: true }),
    });
    this.expiryChip = this.root.getByRole('button', { name: /^Expiry: / }).first();
    this.addButton = this.root.getByRole('button', { name: 'Add warranty', exact: true });
  }

  rows(): Locator {
    return this.root.getByRole('listitem');
  }

  /**
   * Chooses one of the chip's year presets ("1 year · Oct 3, 2026"), counted from the item's
   * acquired-on date; resolves to the menu label so the caller can check the date it resolved to.
   */
  @step()
  async chooseYearPreset(years: number): Promise<string> {
    await this.expiryChip.click();
    const noun = years === 1 ? 'year' : 'years';
    const preset = this.page.getByRole('menuitem', { name: new RegExp(`^${years} ${noun} · `) });
    const label = (await preset.textContent())?.trim() ?? '';
    await preset.click();
    await expect(this.page.getByRole('menu')).toBeHidden();
    return label;
  }

  @step()
  async submit(): Promise<void> {
    const responsePromise = this.page.waitForResponse(
      (resp) => /\/api\/v1\/inventory\/items\/[0-9a-f-]+\/warranties$/.test(resp.url()) && resp.request().method() === 'POST',
    );
    await this.addButton.click();
    await expectOk(await responsePromise);
  }
}
