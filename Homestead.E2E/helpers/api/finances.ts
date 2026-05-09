import { APIRequestContext, expect } from '@playwright/test';

const ACCOUNTS_URL = '/api/v1/finances/accounts?Page=1&PageSize=200&IncludeArchived=true';
const TRANSACTIONS_URL = '/api/v1/finances/transactions?Page=1&PageSize=500';
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
  expect(response.ok(), `lookup currency ${code}`).toBeTruthy();
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
  expect(response.ok(), 'patch finance settings').toBeTruthy();
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
  expect(response.ok(), 'list accounts').toBeTruthy();
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

/**
 * Reset all user finance data. Order matters: regular transactions first
 * (transfers, balance entries), then opening balances per account (those
 * require the dedicated endpoint), then accounts (which only succeed when
 * no splits remain).
 */
export async function deleteAllFinancesData(request: APIRequestContext): Promise<void> {
  const txResponse = await request.get(TRANSACTIONS_URL);
  expect(txResponse.ok(), 'list transactions').toBeTruthy();
  const txBody = await txResponse.json();
  const txItems = (txBody.items ?? []) as Array<{ id: string; shape: string }>;
  await Promise.all(
    txItems
      .filter((tx) => tx.shape !== 'OpeningBalance')
      .map(async (tx) => {
        const del = await request.delete(`/api/v1/finances/transactions/${tx.id}`);
        if (!del.ok() && del.status() !== 404) {
          throw new Error(`failed to delete transaction ${tx.id}: ${del.status()} ${await del.text()}`);
        }
      }),
  );

  const accountResponse = await request.get(ACCOUNTS_URL);
  expect(accountResponse.ok(), 'list accounts').toBeTruthy();
  const accountBody = await accountResponse.json();
  const accounts = (accountBody.items ?? []) as Array<{ id: string; isSystem: boolean }>;
  const userAccounts = accounts.filter((a) => !a.isSystem);

  await Promise.all(
    userAccounts.map(async (acct) => {
      const obResp = await request.get(`/api/v1/finances/accounts/${acct.id}/opening-balances`);
      if (!obResp.ok()) return;
      const obList = (await obResp.json()) as Array<{ commodityId: string }>;
      await Promise.all(
        obList.map(async (ob) => {
          const del = await request.delete(
            `/api/v1/finances/accounts/${acct.id}/opening-balances/${ob.commodityId}`,
          );
          if (!del.ok() && del.status() !== 404) {
            throw new Error(
              `failed to delete opening balance for account ${acct.id} / ${ob.commodityId}: ${del.status()} ${await del.text()}`,
            );
          }
        }),
      );
    }),
  );

  await Promise.all(
    userAccounts.map(async (acct) => {
      const del = await request.delete(`/api/v1/finances/accounts/${acct.id}`);
      if (!del.ok() && del.status() !== 404) {
        throw new Error(`failed to delete account ${acct.id}: ${del.status()} ${await del.text()}`);
      }
    }),
  );
}
