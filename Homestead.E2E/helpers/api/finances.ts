import { APIRequestContext, expect } from '@playwright/test';

const ACCOUNTS_URL = '/api/v1/finances/accounts?Page=1&PageSize=200&IncludeArchived=true';
const FINANCE_SETTINGS_URL = '/api/v1/finances/settings';

export type AccountKind = 'Cash' | 'Investment' | 'OtherAsset' | 'CreditCard' | 'Loan' | 'OtherLiability';

export interface AccountHandle {
  readonly id: string;
  readonly name: string;
  readonly kind: AccountKind;
  readonly accountType: 'Asset' | 'Liability' | 'Equity' | 'Income' | 'Expense';
  readonly reportingCurrencyId: string;
  readonly isSystem: boolean;
}

export interface CreateAccountInput {
  name: string;
  kind: AccountKind;
  reportingCurrencyId: string;
  institution?: string;
  initialBalance?: { amount: number; asOfDate: string };
}

export async function findCurrencyId(request: APIRequestContext, code: string): Promise<string> {
  const response = await request.get(`/api/v1/finances/commodities?Kind=Currency&Search=${encodeURIComponent(code)}&Page=1&PageSize=50`);
  await expect(response, `lookup currency ${code}`).toBeOK();
  const body = await response.json();
  const items = (body.items ?? []) as Array<{ id: string; symbol: string; kind: string }>;
  const match = items.find((c) => c.symbol === code && c.kind === 'Currency');
  if (!match) {
    throw new Error(`Currency commodity not found for ${code}; got ${JSON.stringify(items.map(i => i.symbol))}`);
  }
  return match.id;
}

export async function setDisplayCurrencyViaApi(
  request: APIRequestContext,
  currencyId: string,
): Promise<void> {
  const response = await request.patch(FINANCE_SETTINGS_URL, {
    headers: { 'Content-Type': 'application/json-patch+json' },
    data: [{ op: 'replace', path: '/displayCurrencyId', value: currencyId }],
  });
  await expect(response, 'patch finance settings').toBeOK();
}

export async function createAccountViaApi(
  request: APIRequestContext,
  input: CreateAccountInput,
): Promise<AccountHandle> {
  const response = await request.post('/api/v1/finances/accounts', {
    data: {
      name: input.name,
      kind: input.kind,
      reportingCurrencyId: input.reportingCurrencyId,
      institution: input.institution ?? null,
      initialBalance: input.initialBalance ?? null,
    },
  });
  expect(response.status(), `create account ${input.name}`).toBe(201);
  const body = await response.json();
  return {
    id: body.id,
    name: body.name,
    kind: body.kind,
    accountType: body.accountType,
    reportingCurrencyId: body.reportingCurrencyId,
    isSystem: body.isSystem,
  };
}

interface AccountListItem {
  id: string;
  name: string;
  kind: AccountKind;
  accountType: AccountHandle['accountType'];
  reportingCurrencyId: string;
  isSystem: boolean;
}

export async function listAccountsViaApi(request: APIRequestContext): Promise<AccountHandle[]> {
  const response = await request.get(ACCOUNTS_URL);
  await expect(response, 'list accounts').toBeOK();
  const body = await response.json();
  return ((body.items ?? []) as AccountListItem[]).map((a) => ({
    id: a.id,
    name: a.name,
    kind: a.kind,
    accountType: a.accountType,
    reportingCurrencyId: a.reportingCurrencyId,
    isSystem: a.isSystem,
  }));
}

// ---------------------------------------------------------------------------------------------
// Categories, payees and Expense/Income flows
// ---------------------------------------------------------------------------------------------

export type FlowDirection = 'Expense' | 'Income';

export interface CategoryHandle {
  readonly id: string;
  readonly name: string;
  readonly type: FlowDirection;
  readonly parentId: string | null;
}

export async function createCategoryViaApi(
  request: APIRequestContext,
  input: { name: string; type: FlowDirection; parentId?: string },
): Promise<CategoryHandle> {
  const response = await request.post('/api/v1/finances/categories', {
    data: { name: input.name, type: input.type, parentId: input.parentId ?? null },
  });
  expect(response.status(), `create category ${input.name}`).toBe(201);
  const body = await response.json();
  return { id: body.id, name: body.name, type: body.type, parentId: body.parentId };
}

export async function listCategoriesViaApi(request: APIRequestContext): Promise<CategoryHandle[]> {
  const response = await request.get('/api/v1/finances/categories');
  await expect(response, 'list categories').toBeOK();
  const items = (await response.json()) as CategoryHandle[];
  return items.map((c) => ({ id: c.id, name: c.name, type: c.type, parentId: c.parentId }));
}

export interface PayeeHandle {
  readonly id: string;
  readonly name: string;
  readonly defaultCategoryId: string | null;
  readonly isArchived: boolean;
  readonly transactionCount: number;
}

export async function createPayeeViaApi(
  request: APIRequestContext,
  input: { name: string; defaultCategoryId?: string },
): Promise<PayeeHandle> {
  const response = await request.post('/api/v1/finances/payees', {
    data: { name: input.name, defaultCategoryId: input.defaultCategoryId ?? null },
  });
  expect(response.status(), `create payee ${input.name}`).toBe(201);
  return toPayeeHandle(await response.json());
}

/** Payees whose name contains `search`, archived ones included. */
export async function searchPayeesViaApi(request: APIRequestContext, search: string): Promise<PayeeHandle[]> {
  const response = await request.get(
    `/api/v1/finances/payees?Search=${encodeURIComponent(search)}&IncludeArchived=true&Page=1&PageSize=50`,
  );
  await expect(response, `search payees ${search}`).toBeOK();
  const body = await response.json();
  return (body.items as PayeeHandle[]).map(toPayeeHandle);
}

function toPayeeHandle(p: PayeeHandle): PayeeHandle {
  return {
    id: p.id,
    name: p.name,
    defaultCategoryId: p.defaultCategoryId ?? null,
    isArchived: p.isArchived,
    transactionCount: p.transactionCount,
  };
}

export interface CreateFlowInput {
  direction: FlowDirection;
  accountId: string;
  amount: number;
  date: string;
  description?: string;
  categoryId?: string;
  payeeId?: string;
}

export interface FlowHandle {
  readonly id: string;
  readonly shape: string;
  readonly memo: string;
  readonly amount: number;
  readonly categoryId: string | null;
  readonly payeeId: string | null;
}

export async function createFlowViaApi(request: APIRequestContext, input: CreateFlowInput): Promise<FlowHandle> {
  const response = await request.post('/api/v1/finances/transactions/flows', {
    data: {
      direction: input.direction,
      accountId: input.accountId,
      amount: input.amount,
      date: input.date,
      description: input.description ?? null,
      categoryId: input.categoryId ?? null,
      payeeId: input.payeeId ?? null,
    },
  });
  expect(response.status(), `create ${input.direction.toLowerCase()} flow`).toBe(201);
  return toFlowHandle(await response.json());
}

interface TransactionBody {
  id: string;
  shape: string;
  memo: string;
  details?: { kind: string; amount?: number; categoryId?: string | null; payeeId?: string | null } | null;
}

function toFlowHandle(tx: TransactionBody): FlowHandle {
  return {
    id: tx.id,
    shape: tx.shape,
    memo: tx.memo,
    amount: tx.details?.amount ?? NaN,
    categoryId: tx.details?.categoryId ?? null,
    payeeId: tx.details?.payeeId ?? null,
  };
}

/** Every Expense/Income flow recorded against an account (other shapes are left out). */
export async function listAccountFlowsViaApi(request: APIRequestContext, accountId: string): Promise<FlowHandle[]> {
  const response = await request.get(`/api/v1/finances/accounts/${accountId}/transactions?Page=1&PageSize=200`);
  await expect(response, 'list account transactions').toBeOK();
  const body = await response.json();
  return (body.items as TransactionBody[]).filter((tx) => tx.details?.kind === 'flow').map(toFlowHandle);
}

export async function getFlowViaApi(request: APIRequestContext, transactionId: string): Promise<FlowHandle> {
  const response = await request.get(`/api/v1/finances/transactions/${transactionId}`);
  await expect(response, `get transaction ${transactionId}`).toBeOK();
  return toFlowHandle(await response.json());
}

// ---------------------------------------------------------------------------------------------
// Settings, reports, commodities
// ---------------------------------------------------------------------------------------------

export async function getDisplayCurrencyIdViaApi(request: APIRequestContext): Promise<string | null> {
  const response = await request.get(FINANCE_SETTINGS_URL);
  await expect(response, 'get finance settings').toBeOK();
  return (await response.json()).displayCurrencyId ?? null;
}

/** The watch list ("pair allow list") of an external price source such as `BankOfCanada`. */
export async function getPriceSourceWatchListViaApi(request: APIRequestContext, source: string): Promise<string[]> {
  const response = await request.get(`/api/v1/finances/settings/price-sources/${source}`);
  await expect(response, `get price source ${source}`).toBeOK();
  return (await response.json()).pairAllowList ?? [];
}

/**
 * Net worth at the end of a custom `[from, to]` window — the last point of the history the Reports
 * page charts. A window that ends before any recorded activity has no points, which is worth 0.
 */
export async function netWorthAtEndOfViaApi(request: APIRequestContext, from: string, to: string): Promise<number> {
  const response = await request.get(
    `/api/v1/finances/reports/net-worth-history?Range=Custom&FromDate=${from}&ToDate=${to}`,
  );
  await expect(response, `net worth ${from}..${to}`).toBeOK();
  const points = (await response.json()).points as Array<{ value: number | null }>;
  const last = points.at(-1);
  if (last && last.value === null) throw new Error(`Net worth ${from}..${to} is unpriced`);
  return last?.value ?? 0;
}

export async function createSecurityViaApi(
  request: APIRequestContext,
  input: { symbol: string; exchange: string; name: string },
): Promise<{ id: string; symbol: string; exchange: string; name: string }> {
  const response = await request.post('/api/v1/finances/commodities', {
    data: { symbol: input.symbol, exchange: input.exchange, name: input.name, displayPrecision: 4 },
  });
  expect(response.status(), `create security ${input.symbol}`).toBe(201);
  const body = await response.json();
  return { id: body.id, symbol: body.symbol, exchange: body.exchange, name: body.name };
}
