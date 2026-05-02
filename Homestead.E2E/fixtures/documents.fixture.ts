import { test as base } from '@playwright/test';
import { DocumentListPage } from '../pages/documents/document-list.page';
import { DocumentDetailPage } from '../pages/documents/document-detail.page';
import { TagListPage } from '../pages/documents/tag-list.page';
import { TrashListPage } from '../pages/documents/trash-list.page';

type DocumentsFixtures = {
  documentListPage: DocumentListPage;
  documentDetailPage: DocumentDetailPage;
  tagListPage: TagListPage;
  trashListPage: TrashListPage;
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
});

export { expect } from '@playwright/test';
