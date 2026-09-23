import { type Locator, type Page, expect } from '@playwright/test';
import { expectOk } from '../../helpers/playwright.helpers';
import { step } from '../../helpers/step';

/** `/documents/settings` — household-wide Documents settings (admin only). */
export class DocumentSettingsPage {
  readonly autoClassifyCheckbox: Locator;
  readonly saveButton: Locator;
  readonly textExtractionCard: Locator;

  constructor(readonly page: Page) {
    this.autoClassifyCheckbox = page.getByRole('checkbox', { name: 'Auto-classify on upload' });
    this.saveButton = page.getByRole('button', { name: /^(Save|Saving)/ });
    this.textExtractionCard = page.getByTestId('text-extraction-card');
  }

  async goto(): Promise<void> {
    const settings = this.page.waitForResponse(
      (resp) => resp.url().endsWith('/api/v1/documents/settings') && resp.request().method() === 'GET',
    );
    await this.page.goto('/documents/settings');
    await expectOk(await settings);
    await expect(this.autoClassifyCheckbox).toBeVisible();
  }

  /** One engine's row in the read-only Text Extraction card, by its config key. */
  engine(key: string): Locator {
    return this.page.getByTestId('engine-row').filter({ has: this.page.getByText(key, { exact: true }) });
  }

  /** Flips the auto-classify toggle to `checked` and saves it. */
  @step()
  async setAutoClassify(checked: boolean): Promise<void> {
    // setChecked() reads the state back synchronously after its click, before Angular has
    // re-rendered aria-checked; click and wait for the new state instead.
    await expect(this.autoClassifyCheckbox).toBeChecked({ checked: !checked });
    await this.autoClassifyCheckbox.click();
    await expect(this.autoClassifyCheckbox).toBeChecked({ checked });
    const saveResponse = this.page.waitForResponse(
      (resp) => resp.url().endsWith('/api/v1/documents/settings') && resp.request().method() === 'PATCH',
    );
    await this.saveButton.click();
    await expectOk(await saveResponse);
    await expect(this.saveButton).toBeDisabled();
  }
}
