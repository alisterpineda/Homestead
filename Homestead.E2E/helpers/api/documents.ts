import { APIRequestContext, expect } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { waitForProcessingComplete } from './processing';

const FIXTURES_DIR = path.resolve(__dirname, '../../fixtures/files');

// IncludeChildTags is required by the GetDocuments endpoint binder (non-nullable bool).
const LIST_DOCUMENTS_URL = '/api/v1/documents?Page=1&PageSize=100&IncludeChildTags=false';

const FIXTURES: Record<'pdf' | 'image', { file: string; mimeType: string }> = {
  pdf: { file: 'test-document.pdf', mimeType: 'application/pdf' },
  image: { file: 'test-image.png', mimeType: 'image/png' },
};

/**
 * Delete all documents (soft-delete then empty trash).
 * Used to ensure a clean state before serial test blocks that assume an empty list.
 */
export async function deleteAllDocuments(request: APIRequestContext): Promise<void> {
  const response = await request.get(LIST_DOCUMENTS_URL);
  expect(response.ok()).toBeTruthy();
  const body = await response.json();
  const ids = (body.items ?? []).map((d: { id: string }) => d.id);
  if (ids.length === 0) return;

  await request.post('/api/v1/documents/bulk-delete', { data: { ids } });
  await request.post('/api/v1/documents/empty-trash');
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
