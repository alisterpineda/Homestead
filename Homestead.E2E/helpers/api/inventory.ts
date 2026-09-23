import { APIRequestContext, expect } from '@playwright/test';

const BASE = '/api/v1/inventory';
const AREAS_URL = `${BASE}/areas?Page=1&PageSize=200`;
const DURABLES_URL = `${BASE}/durables?Page=1&PageSize=1000&IncludeInactive=true`;
const CONSUMABLES_URL = `${BASE}/consumables?Page=1&PageSize=1000&IncludeInactive=true`;
const TAGS_URL = `${BASE}/tags?Page=1&PageSize=1000`;
const BRANDS_URL = `${BASE}/brands?Page=1&PageSize=1000`;

const JSON_PATCH = { 'Content-Type': 'application/json-patch+json' };

export type ItemStatus = 'Active' | 'Lent' | 'Archived';

export interface AreaHandle {
  readonly id: string;
  readonly name: string;
  readonly parentAreaId: string | null;
}

export interface DurableHandle {
  readonly id: string;
  readonly name: string;
  readonly status: ItemStatus;
  readonly areaId: string | null;
  readonly parentItemId: string | null;
  readonly isContainer: boolean;
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

export async function listAreasViaApi(request: APIRequestContext): Promise<AreaHandle[]> {
  const response = await request.get(AREAS_URL);
  expect(response.ok(), 'list areas').toBeTruthy();
  const body = await response.json();
  return ((body.items ?? []) as Array<{ id: string; name: string; parentAreaId?: string | null }>).map(
    (a) => ({ id: a.id, name: a.name, parentAreaId: a.parentAreaId ?? null }),
  );
}

export async function listDurablesViaApi(request: APIRequestContext): Promise<DurableHandle[]> {
  const response = await request.get(DURABLES_URL);
  expect(response.ok(), 'list durables').toBeTruthy();
  const body = await response.json();
  return (body.items ?? []).map(toDurableHandle);
}

export async function listConsumablesViaApi(request: APIRequestContext): Promise<ConsumableHandle[]> {
  const response = await request.get(CONSUMABLES_URL);
  expect(response.ok(), 'list consumables').toBeTruthy();
  const body = await response.json();
  return (body.items ?? []).map(toConsumableHandle);
}

export async function getConsumableViaApi(
  request: APIRequestContext,
  id: string,
): Promise<{ consumable: ConsumableHandle; locations: StockEntry[] }> {
  const response = await request.get(`${BASE}/consumables/${id}`);
  expect(response.ok(), `get consumable ${id}`).toBeTruthy();
  const body = await response.json();
  return { consumable: toConsumableHandle(body.consumable), locations: body.locations ?? [] };
}

/**
 * Reset all inventory data as far as the API allows. Items that carry history can't be deleted —
 * a consumable with ledger movements, a durable that has held contents or was tracked from stock —
 * so those are archived instead: terminal items are hidden from every default view and the
 * location pickers, and they no longer block deleting their area.
 *
 * Order matters: stock is used up first (stock sitting in an area or container blocks both the
 * area delete and archiving the container), then consumables, then durables leaf-first (a
 * container with live contents can't be archived), then areas leaf-first, then tags and brands.
 */
export async function resetInventoryViaApi(request: APIRequestContext): Promise<void> {
  for (const consumable of await listConsumablesViaApi(request)) {
    const { locations } = await getConsumableViaApi(request, consumable.id);
    for (const entry of locations) {
      if (entry.quantity <= 0) continue;
      const consume = await request.post(`${BASE}/consumables/${consumable.id}/consume`, {
        data: {
          areaId: entry.areaId,
          containerItemId: entry.containerItemId,
          quantity: entry.quantity,
        },
      });
      expect(consume.ok(), `use up stock of ${consumable.name}: ${await consume.text()}`).toBeTruthy();
    }
    await deleteOrArchive(request, `${BASE}/consumables/${consumable.id}`, consumable.name, consumable.status);
  }

  // Leaf-first: each pass retires every live durable with no live children left, which frees its
  // parent for the next pass.
  let durables = await listDurablesViaApi(request);
  for (;;) {
    const live = durables.filter((d) => d.status !== 'Archived');
    const leaves = live.filter((d) => !live.some((child) => child.parentItemId === d.id));
    if (leaves.length === 0) break;
    for (const durable of leaves) {
      await deleteOrArchive(request, `${BASE}/durables/${durable.id}`, durable.name, durable.status);
    }
    durables = await listDurablesViaApi(request);
  }
  // Archived durables left over from an earlier run: delete the ones that allow it.
  for (const durable of durables) {
    await request.delete(`${BASE}/durables/${durable.id}`);
  }

  let areas = await listAreasViaApi(request);
  while (areas.length > 0) {
    const leaves = areas.filter((a) => !areas.some((child) => child.parentAreaId === a.id));
    for (const area of leaves) {
      const del = await request.delete(`${BASE}/areas/${area.id}`);
      if (!del.ok() && del.status() !== 404) {
        throw new Error(`failed to delete area ${area.name}: ${del.status()} ${await del.text()}`);
      }
    }
    areas = await listAreasViaApi(request);
  }

  const tagsResponse = await request.get(TAGS_URL);
  expect(tagsResponse.ok(), 'list inventory tags').toBeTruthy();
  const tagIds = ((await tagsResponse.json()).items ?? []).map((t: { id: string }) => t.id);
  if (tagIds.length > 0) {
    const del = await request.post(`${BASE}/tags/bulk-delete`, { data: { ids: tagIds } });
    expect(del.ok(), 'bulk-delete inventory tags').toBeTruthy();
  }

  const brandsResponse = await request.get(BRANDS_URL);
  expect(brandsResponse.ok(), 'list brands').toBeTruthy();
  for (const brand of ((await brandsResponse.json()).items ?? []) as Array<{ id: string; name: string }>) {
    const del = await request.delete(`${BASE}/brands/${brand.id}`);
    // 400 = still referenced by an item the reset could only archive (its history blocks a hard
    // delete); archived items keep their brand, so leave that brand behind rather than fail the reset.
    if (!del.ok() && del.status() !== 404 && del.status() !== 400) {
      throw new Error(`failed to delete brand ${brand.name}: ${del.status()} ${await del.text()}`);
    }
  }
}

/** Hard-delete an item; when its history forbids that (400), archive it instead. */
async function deleteOrArchive(
  request: APIRequestContext,
  itemUrl: string,
  name: string,
  status: ItemStatus,
): Promise<void> {
  const del = await request.delete(itemUrl);
  if (del.ok() || del.status() === 404) return;
  if (del.status() !== 400) {
    throw new Error(`failed to delete ${name}: ${del.status()} ${await del.text()}`);
  }
  if (status === 'Archived') return;
  const archive = await request.patch(itemUrl, {
    headers: JSON_PATCH,
    data: [{ op: 'replace', path: '/status', value: 'Archived' }],
  });
  expect(archive.ok(), `archive ${name}: ${await archive.text()}`).toBeTruthy();
}

function toDurableHandle(d: {
  id: string;
  name: string;
  status: ItemStatus;
  areaId?: string | null;
  parentItemId?: string | null;
  isContainer?: boolean | null;
}): DurableHandle {
  return {
    id: d.id,
    name: d.name,
    status: d.status,
    areaId: d.areaId ?? null,
    parentItemId: d.parentItemId ?? null,
    isContainer: d.isContainer === true,
  };
}

function toConsumableHandle(c: {
  id: string;
  name: string;
  status: ItemStatus;
  totalOnHand: number;
}): ConsumableHandle {
  return { id: c.id, name: c.name, status: c.status, totalOnHand: c.totalOnHand };
}
