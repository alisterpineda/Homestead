import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { DISPLAY_CURRENCY } from '../e2e.config';
import type { KnownViolation } from './a11y';
import { createTagViaApi, uploadDocumentViaApi } from './api/documents';
import { createAccountViaApi, findCurrencyId } from './api/finances';
import { createAreaViaApi, createConsumableViaApi, createDurableViaApi } from './api/inventory';
import { makePdf } from './files';
import { isoDateDaysAgo } from './date.helpers';

/** A representative page per module, seeded so it renders real data rather than an empty state. */
export interface KeyPage {
  name: string;
  /** Seeds whatever keeps the page out of its empty state; resolves to the path to open. */
  open: (request: APIRequestContext, uid: string) => Promise<string>;
  /** Visible once the page has rendered its data. */
  ready: (page: Page, uid: string) => Locator;
  /** Accessibility violations this page is known to have (see helpers/a11y.ts). */
  known?: KnownViolation[];
}

const NESTED_CARDS: KnownViolation = {
  rule: 'nested-interactive',
  reason: 'List cards are focusable, clickable containers that also hold a checkbox / menu button.',
};

const seedDocument = (request: APIRequestContext, uid: string) =>
  makePdf(`a11y-${uid}`, 'Accessibility scan').then((file) => uploadDocumentViaApi(request, file));

const seedAccount = async (request: APIRequestContext, uid: string) =>
  createAccountViaApi(request, {
    name: `A11y ${uid}`,
    kind: 'Cash',
    reportingCurrencyId: await findCurrencyId(request, DISPLAY_CURRENCY),
    initialBalance: { amount: 100, asOfDate: isoDateDaysAgo(10) },
  });

const seedInventory = async (request: APIRequestContext, uid: string) => {
  const area = await createAreaViaApi(request, `A11y area ${uid}`);
  await createConsumableViaApi(request, { name: `A11y soap ${uid}`, initialQuantity: 2, initialAreaId: area.id });
  return createDurableViaApi(request, { name: `A11y drill ${uid}`, areaId: area.id });
};

export const KEY_PAGES: KeyPage[] = [
  {
    name: 'document list',
    open: async (request, uid) => (await seedDocument(request, uid), '/documents'),
    ready: (page, uid) => page.getByRole('main').getByText(`a11y-${uid}`, { exact: true }),
    known: [NESTED_CARDS],
  },
  {
    name: 'document detail',
    open: async (request, uid) => `/documents/${(await seedDocument(request, uid)).id}`,
    ready: (page) => page.getByRole('button', { name: 'Download' }),
    known: [
      { rule: 'aria-required-attr', reason: 'Spartan resizable handle (generated helm) lacks aria-valuenow.', on: 'desktop' },
    ],
  },
  {
    name: 'document tags',
    open: async (request, uid) => (await createTagViaApi(request, `a11y-${uid}`), '/documents/tags'),
    ready: (page, uid) => page.getByRole('main').getByText(`a11y-${uid}`, { exact: true }),
  },
  {
    // The trash is household-wide and emptied by locked specs, so nothing is seeded here: wait for
    // the list to have loaded, whichever state it's in, rather than the shell's route title.
    name: 'document trash',
    open: async () => '/documents/trash',
    ready: (page) =>
      page
        .getByRole('main')
        .getByText('Trash is empty')
        .or(page.getByRole('main').getByRole('checkbox', { name: /^Select / }))
        // The empty state stays in the DOM (hidden) while the trash has items.
        .filter({ visible: true })
        .first(),
  },
  { name: 'accounts', open: async (request, uid) => (await seedAccount(request, uid), '/finances/accounts'), ready: (page, uid) => page.getByText(`A11y ${uid}`).first() },
  {
    name: 'account detail',
    open: async (request, uid) => `/finances/accounts/${(await seedAccount(request, uid)).id}`,
    ready: (page) => page.getByTestId('account-balance'),
  },
  {
    name: 'inventory browse',
    open: async (request, uid) => (await seedInventory(request, uid), '/inventory/browse'),
    ready: (page, uid) => page.locator('app-place-card').getByRole('link', { name: new RegExp(`^A11y area ${uid}\\b`) }),
  },
  {
    name: 'inventory items',
    open: async (request, uid) => (await seedInventory(request, uid), '/inventory/items'),
    ready: (page) => page.getByRole('button', { name: 'New item' }).first(),
    known: [{ ...NESTED_CARDS, on: 'phones' }],
  },
  {
    name: 'inventory item detail',
    open: async (request, uid) => `/inventory/items/${(await seedInventory(request, uid)).id}`,
    ready: (page, uid) => page.getByRole('heading', { name: `A11y drill ${uid}` }),
  },
];

