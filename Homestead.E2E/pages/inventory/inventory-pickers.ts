import { type Locator, type Page, expect } from '@playwright/test';
import { escapeRegExp } from '../../helpers/playwright.helpers';

/**
 * Opens a Spartan popover picker (location / parent area) from its trigger, narrows it with the
 * picker's own search box and clicks the option whose label starts with `optionName`.
 *
 * The popover is portaled to the overlay container, so options are resolved inside the popover that
 * owns `searchLabel` — never against the page, where the same name can also appear on a place card
 * or table row behind the dialog.
 */
async function pickFromPopover(
  page: Page,
  trigger: Locator,
  searchLabel: string,
  optionName: string,
): Promise<void> {
  await trigger.click();
  const search = page.getByRole('textbox', { name: searchLabel });
  await expect(search).toBeVisible();
  await search.fill(optionName);
  const popover = page.locator('[hlmPopoverContent]').filter({ has: search });
  await popover
    .getByRole('button', { name: new RegExp(`^${escapeRegExp(optionName)}`) })
    .first()
    .click();
  await expect(search).toBeHidden();
}

/** Location picker (`app-location-picker`): areas, containers and assemblies. */
export async function pickLocation(page: Page, scope: Locator, ariaLabel: string, name: string): Promise<void> {
  await pickFromPopover(
    page,
    scope.getByRole('button', { name: ariaLabel, exact: true }),
    'Search areas, containers, devices',
    name,
  );
}

/** Parent-area picker (`app-area-parent-picker`) on the area create/edit dialogs. */
export async function pickParentArea(page: Page, scope: Locator, name: string): Promise<void> {
  await pickFromPopover(
    page,
    scope.getByRole('button', { name: 'Parent area', exact: true }),
    'Search areas',
    name,
  );
}

/**
 * The open brand picker's popover (`app-brand-picker`). Its search box shares the "Search brands"
 * name with the Brands page's own search, so it is found through the popover that holds it.
 */
export function brandPickerPopover(page: Page): Locator {
  return page.locator('[hlmPopoverContent]').filter({
    has: page.getByRole('textbox', { name: 'Search brands' }),
  });
}
