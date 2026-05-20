import type { AccountResponse } from '@/api/generated/models/account-response';
import type { AccountType } from '@/api/generated/models/account-type';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import { formatMaskedCurrency } from './format-currency';

// Beancount-style stored sign convention:
// - Asset/Expense: positive on increase
// - Liability/Equity/Income: negative on increase
// The UI flips Liability so the user sees positive "amount owed". Equity/Income/Expense
// only appear via system accounts (filtered out of user lists), but the helper handles
// them defensively: equity stays unflipped (positive equity = positive net wealth, which
// happens to align with user expectations).
export function displaySign(accountType: AccountType): 1 | -1 {
  return accountType === 'Liability' ? -1 : 1;
}

export function toUserAmount(amount: number | null, accountType: AccountType): number | null {
  if (amount === null) return null;
  return amount * displaySign(accountType);
}

// Format an account's "as of now" balance in the household display currency.
// Returns null if the value cannot be priced — the caller should render `—`.
export function formatAccountBalance(
  account: Pick<AccountResponse, 'accountType' | 'displayValue'>,
  displayCurrency: CommodityResponse | null,
  masked = false,
): string | null {
  if (!displayCurrency) return null;
  const raw = account.displayValue ?? null;
  if (raw === null) return null;
  return formatMaskedCurrency(toUserAmount(raw, account.accountType) ?? 0, displayCurrency, masked);
}

// Format a single-position account's native balance. Returns null for multi-position
// (Investment) accounts or when the value is unresolved.
export function formatNativeBalance(
  account: Pick<AccountResponse, 'accountType' | 'nativeBalance'>,
  reportingCurrency: CommodityResponse | null,
  masked = false,
): string | null {
  if (!reportingCurrency) return null;
  const raw = account.nativeBalance ?? null;
  if (raw === null) return null;
  return formatMaskedCurrency(toUserAmount(raw, account.accountType) ?? 0, reportingCurrency, masked);
}

// Net worth = sum of stored displayValue across all accounts (Beancount sign already
// makes this work without per-type arithmetic). Caller is responsible for filtering
// out system accounts.
export function netWorth(
  accounts: ReadonlyArray<Pick<AccountResponse, 'displayValue'>>,
): number {
  let total = 0;
  for (const a of accounts) {
    if (a.displayValue != null) total += a.displayValue;
  }
  return total;
}
