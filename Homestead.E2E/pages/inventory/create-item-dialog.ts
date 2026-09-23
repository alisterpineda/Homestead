import { type Locator, type Page, expect } from '@playwright/test';
import { step } from '../../helpers/step';
import { pickLocation } from './inventory-pickers';

export interface CreateItemInput {
  kind: 'Durable' | 'Consumable';
  name: string;
  /** Area or container name to pick; omit to leave the dialog's current location (Unplaced or pre-filled). */
  location?: string;
  startingStock?: string;
  unit?: string;
  restockThreshold?: string;
}

/**
 * The unified "New item" dialog (`app-create-entry-dialog`). Several pages mount their own trigger,
 * so callers open it with whichever trigger button fits and then drive it through this object.
 */
export class CreateItemDialog {
  readonly root: Locator;
  readonly heading: Locator;

  constructor(readonly page: Page) {
    this.root = page.getByRole('dialog', { name: 'New item' });
    this.heading = this.root.getByRole('heading', { name: 'New item' });
  }

  async open(trigger: Locator): Promise<void> {
    await trigger.click();
    await expect(this.heading).toBeVisible();
  }

  locationTrigger(): Locator {
    return this.root.getByRole('button', { name: 'Location', exact: true });
  }

  @step()
  async fill(input: CreateItemInput): Promise<void> {
    await this.root.getByRole('button', { name: input.kind, exact: true }).click();
    await this.root.getByLabel('Name', { exact: true }).fill(input.name);

    if (input.location !== undefined) {
      await pickLocation(this.page, this.root, 'Location', input.location);
      await expect(this.locationTrigger()).toContainText(input.location);
    }

    if (input.startingStock !== undefined) {
      await this.root.getByLabel('Starting stock').fill(input.startingStock);
    }
    if (input.unit !== undefined) {
      await this.root.getByLabel('Unit', { exact: true }).fill(input.unit);
    }

    if (input.restockThreshold !== undefined) {
      await this.root.getByRole('button', { name: 'More details' }).click();
      await this.root.getByLabel('Restock threshold').fill(input.restockThreshold);
    }
  }

  /** Submits with "Create" and waits for the item POST; resolves to the new item's id. */
  @step()
  async submit(kind: CreateItemInput['kind']): Promise<string> {
    const endpoint = kind === 'Durable' ? '/api/v1/inventory/durables' : '/api/v1/inventory/consumables';
    const responsePromise = this.page.waitForResponse(
      (resp) =>
        resp.url().endsWith(endpoint) && resp.request().method() === 'POST' && resp.status() === 201,
    );
    await this.root.getByRole('button', { name: 'Create', exact: true }).click();
    const response = await responsePromise;
    await expect(this.heading).toBeHidden();
    const body = await response.json();
    return body.id as string;
  }

  @step()
  async create(input: CreateItemInput): Promise<string> {
    await this.fill(input);
    return this.submit(input.kind);
  }
}
