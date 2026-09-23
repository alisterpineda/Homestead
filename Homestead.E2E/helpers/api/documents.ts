import { type APIRequestContext, expect } from '@playwright/test';
import type { FilePayload } from '../files';
import { waitForEntityJobs, waitForJob } from './processing';

export interface DocumentHandle {
  readonly id: string;
  readonly name: string;
}

/** The name the app gives an uploaded file: its file name without the extension. */
export function documentNameOf(file: FilePayload): string {
  return file.name.replace(/\.[^.]+$/, '');
}

/**
 * Uploads through the API and waits until the document exists and its text extraction is done, so
 * callers can assert against search results straight away.
 */
export async function uploadDocumentViaApi(request: APIRequestContext, file: FilePayload): Promise<DocumentHandle> {
  const response = await request.post('/api/v1/documents', { multipart: { file } });
  expect(response.status(), `upload ${file.name}`).toBe(202);
  const { jobId } = await response.json();

  const job = await waitForJob(request, jobId);
  expect(job.status, `upload job for ${file.name}: ${job.error ?? ''}`).toBe('Completed');

  const name = documentNameOf(file);
  const document = await findDocumentByName(request, name);
  await waitForEntityJobs(request, document.id);
  return document;
}

/** Live (not trashed) documents the list's search finds for `term`. */
export async function searchDocumentsViaApi(request: APIRequestContext, term: string): Promise<DocumentHandle[]> {
  const response = await request.get(
    `/api/v1/documents?Page=1&PageSize=20&IncludeChildTags=false&Search=${encodeURIComponent(term)}`,
  );
  await expect(response).toBeOK();
  return ((await response.json()).items as DocumentHandle[]).map(({ id, name }) => ({ id, name }));
}

export async function findDocumentByName(request: APIRequestContext, name: string): Promise<DocumentHandle> {
  const items = await searchDocumentsViaApi(request, name);
  const match = items.find((d) => d.name === name);
  expect(match, `document "${name}" exists`).toBeDefined();
  return { id: match!.id, name: match!.name };
}

export interface DocumentDetail {
  id: string;
  name: string;
  notes: string;
  versionCount: number;
  versions: Array<{ id: string; versionNumber: number; label: string | null }>;
  tags: Array<{ id: string; path: string }>;
  parties: Array<{ id: string; name: string }>;
}

export async function getDocumentViaApi(request: APIRequestContext, id: string): Promise<DocumentDetail> {
  const response = await request.get(`/api/v1/documents/${id}`);
  await expect(response, `get document ${id}`).toBeOK();
  return response.json();
}

export async function patchDocumentViaApi(
  request: APIRequestContext,
  id: string,
  operations: Array<{ op: 'replace' | 'add' | 'remove'; path: string; value?: unknown }>,
): Promise<void> {
  const response = await request.patch(`/api/v1/documents/${id}`, {
    headers: { 'Content-Type': 'application/json-patch+json' },
    data: operations,
  });
  await expect(response, `patch document ${id}`).toBeOK();
}

export async function trashDocumentsViaApi(request: APIRequestContext, ids: string[]): Promise<void> {
  const response = await request.post('/api/v1/documents/bulk-delete', { data: { ids } });
  await expect(response, 'bulk-delete documents').toBeOK();
}

export async function createTagViaApi(
  request: APIRequestContext,
  tagPath: string,
  options?: { color?: string; description?: string },
): Promise<{ id: string }> {
  const response = await request.post('/api/v1/documents/tags', {
    data: { path: tagPath, color: options?.color ?? null, description: options?.description ?? null },
  });
  expect(response.status(), `create tag ${tagPath}`).toBe(201);
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
  expect(response.status(), `create party ${name}`).toBe(201);
  return response.json();
}

/**
 * Uploads `file` as a new version of an existing document and waits for its processing job (text
 * extraction included). Resolves to the new version as the document now lists it.
 */
export async function addVersionViaApi(
  request: APIRequestContext,
  documentId: string,
  file: FilePayload,
  label?: string,
): Promise<{ id: string; versionNumber: number; label: string | null }> {
  const response = await request.post(`/api/v1/documents/${documentId}/versions`, {
    multipart: label ? { file, label } : { file },
  });
  expect(response.status(), `upload version ${file.name}`).toBe(202);
  const { jobId } = await response.json();

  const job = await waitForJob(request, jobId);
  expect(job.status, `version upload job for ${file.name}: ${job.error ?? ''}`).toBe('Completed');
  await waitForEntityJobs(request, documentId);

  const { versions } = await getDocumentViaApi(request, documentId);
  return versions.reduce((latest, v) => (v.versionNumber > latest.versionNumber ? v : latest));
}

export interface ActivityItem {
  type: 'audit_log' | 'comment';
  summary: string | null;
  body: string | null;
}

/** A document's activity stream (audit entries and comments), newest first. */
export async function getActivityViaApi(request: APIRequestContext, documentId: string): Promise<ActivityItem[]> {
  const response = await request.get(`/api/v1/documents/${documentId}/activity?Page=1&PageSize=100`);
  await expect(response, `get activity for ${documentId}`).toBeOK();
  return (await response.json()).items;
}

export interface DocumentSettings {
  autoClassifyOnUpload: boolean | null;
}

export async function getDocumentSettingsViaApi(request: APIRequestContext): Promise<DocumentSettings> {
  const response = await request.get('/api/v1/documents/settings');
  await expect(response, 'get document settings').toBeOK();
  return response.json();
}

export async function patchDocumentSettingsViaApi(
  request: APIRequestContext,
  settings: Partial<DocumentSettings>,
): Promise<void> {
  const response = await request.patch('/api/v1/documents/settings', {
    headers: { 'Content-Type': 'application/json-patch+json' },
    data: Object.entries(settings).map(([key, value]) => ({ op: 'replace', path: `/${key}`, value })),
  });
  await expect(response, 'patch document settings').toBeOK();
}

/** Parties whose name matches `search`. */
export async function findPartiesViaApi(
  request: APIRequestContext,
  search: string,
): Promise<Array<{ id: string; name: string; description: string | null }>> {
  const response = await request.get(
    `/api/v1/documents/parties?Page=1&PageSize=20&Search=${encodeURIComponent(search)}`,
  );
  await expect(response, `find parties "${search}"`).toBeOK();
  return (await response.json()).items;
}
