import { APIRequestContext, expect } from '@playwright/test';

export async function waitForProcessingComplete(
  request: APIRequestContext,
  timeoutMs = 30_000,
): Promise<void> {
  const start = Date.now();
  let pollInterval = 250;
  const maxInterval = 2_000;

  while (Date.now() - start < timeoutMs) {
    const response = await request.get('/api/v1/processing/jobs?Page=1&PageSize=100');
    expect(response.ok()).toBeTruthy();
    const body = await response.json();
    const pending = body.items?.filter(
      (j: { status: string }) => j.status === 'Pending' || j.status === 'InProgress',
    );
    if (!pending || pending.length === 0) return;
    await new Promise((r) => setTimeout(r, pollInterval));
    pollInterval = Math.min(pollInterval * 1.5, maxInterval);
  }
  throw new Error(`Processing jobs did not complete within ${timeoutMs}ms`);
}
