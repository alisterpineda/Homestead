import { APIRequestContext, expect } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { waitForProcessingComplete } from './processing';

const FIXTURES_DIR = path.resolve(__dirname, '../../fixtures/files');

// IncludeChildTags is required by the GetDocuments endpoint binder (non-nullable bool).
const LIST_DOCUMENTS_URL = '/api/v1/documents?Page=1&PageSize=100&IncludeChildTags=false';

/**
 * Drain a paginated list endpoint by repeatedly fetching the first page and bulk-deleting
 * its ids. Each iteration deletes up to the requested PageSize, so this works for arbitrary
 * backlogs without leaking residue.
 */
async function bulkDeleteAll(
  request: APIRequestContext,
  listUrl: string,
  bulkDeleteUrl: string,
): Promise<void> {
  while (true) {
    const response = await request.get(listUrl);
    expect(response.ok()).toBeTruthy();
    const body = await response.json();
    const ids = (body.items ?? []).map((item: { id: string }) => item.id);
    if (ids.length === 0) return;
    const deleteResponse = await request.post(bulkDeleteUrl, { data: { ids } });
    expect(deleteResponse.ok()).toBeTruthy();
  }
}

const FIXTURES: Record<'pdf' | 'image', { file: string; mimeType: string }> = {
  pdf: { file: 'test-document.pdf', mimeType: 'application/pdf' },
  image: { file: 'test-image.png', mimeType: 'image/png' },
};

export async function deleteAllDocuments(request: APIRequestContext): Promise<void> {
  await bulkDeleteAll(request, LIST_DOCUMENTS_URL, '/api/v1/documents/bulk-delete');
  const emptyTrash = await request.post('/api/v1/documents/empty-trash');
  expect(emptyTrash.ok()).toBeTruthy();
}

export async function deleteAllTags(request: APIRequestContext): Promise<void> {
  await bulkDeleteAll(
    request,
    '/api/v1/documents/tags?Page=1&PageSize=100',
    '/api/v1/documents/tags/bulk-delete',
  );
}

export async function deleteAllParties(request: APIRequestContext): Promise<void> {
  await bulkDeleteAll(
    request,
    '/api/v1/documents/parties?Page=1&PageSize=100',
    '/api/v1/documents/parties/bulk-delete',
  );
}

/**
 * Documents go first so the junction rows that link them to tags/parties are gone
 * before the bulk-delete checks; tags and parties are independent of each other.
 */
export async function deleteAllDocumentMetadata(request: APIRequestContext): Promise<void> {
  await deleteAllDocuments(request);
  await Promise.all([deleteAllTags(request), deleteAllParties(request)]);
}

export async function uploadDocumentViaApi(
  request: APIRequestContext,
  filePath: string,
  mimeType: string,
): Promise<{ jobId: string }> {
  const buffer = fs.readFileSync(filePath);
  const response = await request.post('/api/v1/documents', {
    multipart: {
      file: {
        name: path.basename(filePath),
        mimeType,
        buffer,
      },
    },
  });
  expect(response.status()).toBe(202);
  const body = await response.json();
  return { jobId: body.jobId };
}

/**
 * Upload the standard fixture set, then wait for processing so OCR/text extraction
 * has run before callers assert against extracted-text search results.
 */
export async function seedFixtureDocuments(
  request: APIRequestContext,
  opts: { include?: 'pdf' | 'image' | 'both' } = {},
): Promise<void> {
  const include = opts.include ?? 'both';
  const kinds: ('pdf' | 'image')[] = include === 'both' ? ['pdf', 'image'] : [include];

  await Promise.all(
    kinds.map((kind) =>
      uploadDocumentViaApi(request, path.join(FIXTURES_DIR, FIXTURES[kind].file), FIXTURES[kind].mimeType),
    ),
  );

  await waitForProcessingComplete(request, 60_000);
}

export async function createTagViaApi(
  request: APIRequestContext,
  tagPath: string,
  options?: { color?: string; description?: string },
): Promise<{ id: string }> {
  const response = await request.post('/api/v1/documents/tags', {
    data: {
      path: tagPath,
      color: options?.color ?? null,
      description: options?.description ?? null,
    },
  });
  expect(response.status()).toBe(201);
  return response.json();
}

export async function createPartyViaApi(
  request: APIRequestContext,
  name: string,
  description?: string,
): Promise<{ id: string }> {
  const response = await request.post('/api/v1/documents/parties', {
    data: { name, description: description ?? null },
  });
  expect(response.status()).toBe(201);
  return response.json();
}
