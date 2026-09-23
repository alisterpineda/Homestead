import { APIRequestContext, expect } from '@playwright/test';

const BASE = '/api/v1/inventory';

export type ItemStatus = 'Active' | 'Lent' | 'Archived';

export interface AreaHandle {
  readonly id: string;
  readonly name: string;
  readonly parentAreaId: string | null;
}

export type WarrantyStatus = 'None' | 'Active' | 'Expiring' | 'Expired';

export interface DurableHandle {
  readonly id: string;
  readonly name: string;
  readonly status: ItemStatus;
  readonly statusNote: string | null;
  readonly areaId: string | null;
  readonly parentItemId: string | null;
  readonly isContainer: boolean;
  readonly brandId: string | null;
  readonly tagPaths: string[];
  readonly warrantyStatus: WarrantyStatus;
}

export interface BrandHandle {
  readonly id: string;
  readonly name: string;
  readonly itemCount: number;
}

export interface TagHandle {
  readonly id: string;
  readonly name: string;
  readonly path: string;
  readonly parentId: string | null;
}

export interface ActivityRow {
  readonly type: string;
  readonly id: string;
  readonly body: string | null;
  /** A note's "when it happened" instant (ISO); null on audit rows. */
  readonly occurredAt: string | null;
  readonly isEdited: boolean;
}

export interface ConsumableHandle {
  readonly id: string;
  readonly name: string;
  readonly status: ItemStatus;
  readonly totalOnHand: number;
}

interface StockEntry {
  readonly quantity: number;
  readonly areaId: string | null;
  readonly containerItemId: string | null;
}

export interface CreateDurableInput {
  name: string;
  areaId?: string;
  parentItemId?: string;
  isContainer?: boolean;
  tags?: string[];
  brandId?: string;
  /** `yyyy-MM-dd` — the base the warranty composer's year presets count from. */
  acquiredOn?: string;
}

export interface CreateConsumableInput {
  name: string;
  unit?: string;
  restockThreshold?: number;
  tags?: string[];
  initialQuantity?: number;
  initialAreaId?: string;
  initialContainerItemId?: string;
}

export async function createAreaViaApi(
  request: APIRequestContext,
  name: string,
  parentAreaId: string | null = null,
): Promise<AreaHandle> {
  const response = await request.post(`${BASE}/areas`, { data: { name, parentAreaId } });
  expect(response.status(), `create area ${name}`).toBe(201);
  const body = await response.json();
  return { id: body.id, name: body.name, parentAreaId: body.parentAreaId ?? null };
}

export async function createDurableViaApi(
  request: APIRequestContext,
  input: CreateDurableInput,
): Promise<DurableHandle> {
  const response = await request.post(`${BASE}/durables`, {
    data: {
      name: input.name,
      areaId: input.areaId ?? null,
      parentItemId: input.parentItemId ?? null,
      isContainer: input.isContainer ?? false,
      tags: input.tags ?? [],
      brandId: input.brandId ?? null,
      acquiredOn: input.acquiredOn ?? null,
    },
  });
  expect(response.status(), `create durable ${input.name}`).toBe(201);
  return toDurableHandle(await response.json());
}

export async function createConsumableViaApi(
  request: APIRequestContext,
  input: CreateConsumableInput,
): Promise<ConsumableHandle> {
  const response = await request.post(`${BASE}/consumables`, {
    data: {
      name: input.name,
      unit: input.unit ?? null,
      restockThreshold: input.restockThreshold ?? null,
      tags: input.tags ?? [],
      initialQuantity: input.initialQuantity ?? null,
      initialAreaId: input.initialAreaId ?? null,
      initialContainerItemId: input.initialContainerItemId ?? null,
    },
  });
  expect(response.status(), `create consumable ${input.name}`).toBe(201);
  return toConsumableHandle(await response.json());
}

export async function getConsumableViaApi(
  request: APIRequestContext,
  id: string,
): Promise<{ consumable: ConsumableHandle; locations: StockEntry[] }> {
  const response = await request.get(`${BASE}/consumables/${id}`);
  await expect(response, `get consumable ${id}`).toBeOK();
  const body = await response.json();
  return { consumable: toConsumableHandle(body.consumable), locations: body.locations ?? [] };
}

export async function getDurableViaApi(request: APIRequestContext, id: string): Promise<DurableHandle> {
  const response = await request.get(`${BASE}/durables/${id}`);
  await expect(response, `get durable ${id}`).toBeOK();
  return toDurableHandle(await response.json());
}

/** Applies RFC 6902 `replace` ops to a durable, e.g. `{ status: 'Lent', statusNote: 'Dave' }`. */
export async function patchDurableViaApi(
  request: APIRequestContext,
  id: string,
  changes: Record<string, unknown>,
): Promise<DurableHandle> {
  const response = await request.patch(`${BASE}/durables/${id}`, {
    headers: { 'Content-Type': 'application/json-patch+json' },
    data: Object.entries(changes).map(([key, value]) => ({ op: 'replace', path: `/${key}`, value })),
  });
  await expect(response, `patch durable ${id}`).toBeOK();
  return toDurableHandle(await response.json());
}

export async function createWarrantyViaApi(
  request: APIRequestContext,
  itemId: string,
  input: { provider?: string; expiresOn?: string; notes?: string } = {},
): Promise<void> {
  const response = await request.post(`${BASE}/items/${itemId}/warranties`, {
    data: { provider: input.provider ?? null, expiresOn: input.expiresOn ?? null, notes: input.notes ?? null },
  });
  expect(response.status(), `create warranty on ${itemId}`).toBe(201);
}

export async function getItemActivityViaApi(request: APIRequestContext, itemId: string): Promise<ActivityRow[]> {
  const response = await request.get(`${BASE}/items/${itemId}/activity?PageSize=100`);
  await expect(response, `get activity of ${itemId}`).toBeOK();
  const body = await response.json();
  return (body.items as ActivityRow[]).map((r) => ({
    type: r.type,
    id: r.id,
    body: r.body ?? null,
    occurredAt: r.occurredAt ?? null,
    isEdited: r.isEdited === true,
  }));
}

export async function createBrandViaApi(request: APIRequestContext, name: string): Promise<BrandHandle> {
  const response = await request.post(`${BASE}/brands`, { data: { name } });
  await expect(response, `create brand ${name}`).toBeOK();
  return toBrandHandle(await response.json());
}

/** Brands whose name contains `search` (case-insensitive). */
export async function findBrandsViaApi(request: APIRequestContext, search: string): Promise<BrandHandle[]> {
  const response = await request.get(`${BASE}/brands`, { params: { Search: search, PageSize: 100 } });
  await expect(response, `search brands for ${search}`).toBeOK();
  const body = await response.json();
  return (body.items as BrandHandle[]).map(toBrandHandle);
}

/** Creates a tag by its full path; missing ancestors are upserted alongside it. */
export async function createTagViaApi(request: APIRequestContext, path: string): Promise<TagHandle> {
  const response = await request.post(`${BASE}/tags`, { data: { path } });
  await expect(response, `create tag ${path}`).toBeOK();
  return toTagHandle(await response.json());
}

/** Tags whose name or path contains `search` (case-insensitive). */
export async function findTagsViaApi(request: APIRequestContext, search: string): Promise<TagHandle[]> {
  const response = await request.get(`${BASE}/tags`, { params: { Search: search, PageSize: 100 } });
  await expect(response, `search tags for ${search}`).toBeOK();
  const body = await response.json();
  return (body.items as TagHandle[]).map(toTagHandle);
}

function toDurableHandle(d: {
  id: string;
  name: string;
  status: ItemStatus;
  statusNote?: string | null;
  areaId?: string | null;
  parentItemId?: string | null;
  isContainer?: boolean | null;
  brandId?: string | null;
  tags?: { path: string }[] | null;
  warrantyStatus?: WarrantyStatus;
}): DurableHandle {
  return {
    id: d.id,
    name: d.name,
    status: d.status,
    statusNote: d.statusNote ?? null,
    areaId: d.areaId ?? null,
    parentItemId: d.parentItemId ?? null,
    isContainer: d.isContainer === true,
    brandId: d.brandId ?? null,
    tagPaths: (d.tags ?? []).map((t) => t.path),
    warrantyStatus: d.warrantyStatus ?? 'None',
  };
}

function toBrandHandle(b: BrandHandle): BrandHandle {
  return { id: b.id, name: b.name, itemCount: b.itemCount };
}

function toTagHandle(t: TagHandle): TagHandle {
  return { id: t.id, name: t.name, path: t.path, parentId: t.parentId ?? null };
}

function toConsumableHandle(c: {
  id: string;
  name: string;
  status: ItemStatus;
  totalOnHand: number;
}): ConsumableHandle {
  return { id: c.id, name: c.name, status: c.status, totalOnHand: c.totalOnHand };
}
