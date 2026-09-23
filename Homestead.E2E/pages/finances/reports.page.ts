import type { Locator, Page } from '@playwright/test';
import { step } from '../../helpers/step';

/** `/finances/reports` — the household net-worth headline and history chart. */
export class ReportsPage {
  readonly previousRangeButton: Locator;
  /** The range popover's trigger — its text is the active window, e.g. "Jan 2024 – Dec 2024". */
  readonly rangeTrigger: Locator;

  constructor(readonly page: Page) {
    this.previousRangeButton = page.getByRole('button', { name: 'Previous range' });
    this.rangeTrigger = page.locator('app-net-worth-range-selector').getByRole('button', { name: /\d{4}|All Dates/ });
  }

  async goto(): Promise<void> {
    const loaded = this.waitForHistory();
    await this.page.goto('/finances/reports');
    await loaded;
  }

  /** The headline card: "Net worth", the most recent value in the window, and its caption. */
  headline(): Locator {
    return this.page.locator('app-reports-page section').filter({ hasText: 'Net worth' });
  }

  @step()
  async selectPreset(label: string): Promise<void> {
    await this.rangeTrigger.click();
    const loaded = this.waitForHistory();
    await this.page.getByRole('button', { name: label, exact: true }).click();
    await loaded;
  }

  /** Steps the window back by its own length (e.g. last year → the year before). */
  @step()
  async stepBack(): Promise<void> {
    const loaded = this.waitForHistory();
    await this.previousRangeButton.click();
    await loaded;
  }

  private waitForHistory() {
    return this.page.waitForResponse(
      (resp) =>
        resp.url().includes('/api/v1/finances/reports/net-worth-history') &&
        resp.request().method() === 'GET' &&
        resp.status() === 200,
    );
  }
}
