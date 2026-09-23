import { test as base } from './base.fixture';
import { DocumentListPage } from '../pages/documents/document-list.page';
import { DocumentDetailPage } from '../pages/documents/document-detail.page';
import { TagListPage } from '../pages/documents/tag-list.page';
import { TrashListPage } from '../pages/documents/trash-list.page';
import { PartyListPage } from '../pages/documents/party-list.page';
import { DocumentSettingsPage } from '../pages/documents/document-settings.page';

type DocumentsFixtures = {
  documentListPage: DocumentListPage;
  documentDetailPage: DocumentDetailPage;
  tagListPage: TagListPage;
  trashListPage: TrashListPage;
  partyListPage: PartyListPage;
  documentSettingsPage: DocumentSettingsPage;
};

export const test = base.extend<DocumentsFixtures>({
  documentListPage: async ({ page }, use) => {
    await use(new DocumentListPage(page));
  },
  documentDetailPage: async ({ page }, use) => {
    await use(new DocumentDetailPage(page));
  },
  tagListPage: async ({ page }, use) => {
    await use(new TagListPage(page));
  },
  trashListPage: async ({ page }, use) => {
    await use(new TrashListPage(page));
  },
  partyListPage: async ({ page }, use) => {
    await use(new PartyListPage(page));
  },
  documentSettingsPage: async ({ page }, use) => {
    await use(new DocumentSettingsPage(page));
  },
});

export { expect } from './base.fixture';
