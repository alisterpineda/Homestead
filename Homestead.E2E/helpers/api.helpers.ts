import { APIRequestContext, expect } from '@playwright/test';

// --- Processing ---

export async function waitForProcessingComplete(
  request: APIRequestContext,
  timeoutMs = 30_000,
): Promise<void> {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    const response = await request.get('/api/v1/processing/jobs?Page=1&PageSize=100');
    expect(response.ok()).toBeTruthy();
    const body = await response.json();
    const pending = body.items?.filter(
      (j: { status: string }) => j.status === 'Pending' || j.status === 'InProgress',
    );
    if (!pending || pending.length === 0) return;
    await new Promise((r) => setTimeout(r, 1_000));
  }
  throw new Error(`Processing jobs did not complete within ${timeoutMs}ms`);
}

// --- Documents ---

/**
 * Delete all documents (soft-delete then empty trash).
 * Used to ensure a clean state before serial test blocks that assume an empty list.
 */
export async function deleteAllDocuments(request: APIRequestContext): Promise<void> {
  const response = await request.get('/api/v1/documents?Page=1&PageSize=100');
  if (!response.ok()) return;
  const body = await response.json();
  const ids = (body.items ?? []).map((d: { id: string }) => d.id);
  if (ids.length === 0) return;

  await request.post('/api/v1/documents/bulk-delete', { data: { ids } });
  await request.post('/api/v1/documents/empty-trash');
}

// --- Tags ---

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

// --- Parties ---

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
