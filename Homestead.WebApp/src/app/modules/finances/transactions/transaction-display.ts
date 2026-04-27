import type { TransactionDetails } from '@/api/generated/models/transaction-details';
import type { TransactionResponse } from '@/api/generated/models/transaction-response';
import type { TransactionShape } from '@/api/generated/models/transaction-shape';

// Polymorphic transaction details discriminate via the `kind` field. This guard
// safely narrows null/unknown to a particular variant without sprinkling casts.
export function isDetailsOfKind<T extends { kind?: string }>(
  details: TransactionDetails | null | undefined,
  kind: NonNullable<T['kind']>,
): details is T {
  return !!details && (details as { kind?: string }).kind === kind;
}

export function shapeLabel(shape: TransactionShape): string {
  switch (shape) {
    case 'Transfer': return 'Transfer';
    case 'BalanceEntry': return 'Balance entry';
    case 'OpeningBalance': return 'Opening balance';
    case 'StockSplit': return 'Stock split';
    case 'Other': return 'Transaction';
  }
}

export function shapeIconName(shape: TransactionShape): string {
  switch (shape) {
    case 'Transfer': return 'lucideArrowRightLeft';
    case 'BalanceEntry': return 'lucideScale';
    case 'OpeningBalance': return 'lucideFlag';
    case 'StockSplit': return 'lucideSplit';
    case 'Other': return 'lucideCircleDashed';
  }
}

// Sum of split quantities affecting a particular account, in that account's commodity.
// Stored signs follow the Beancount convention; callers apply displaySign() for UI flips.
export function signedAmountForAccount(
  tx: Pick<TransactionResponse, 'splits'>,
  accountId: string,
): number {
  let sum = 0;
  for (const s of tx.splits) {
    if (s.accountId === accountId) sum += s.quantity;
  }
  return sum;
}

export interface TransferCounterparty {
  accountId: string;
  signedAmount: number;
}

// For a transfer, finds the *other* leg from the perspective of `currentAccountId`.
// Returns null if this transaction does not touch `currentAccountId` or has no other leg.
export function transferCounterparty(
  tx: Pick<TransactionResponse, 'splits'>,
  currentAccountId: string,
): TransferCounterparty | null {
  const other = tx.splits.find(s => s.accountId !== currentAccountId);
  if (!other) return null;
  return { accountId: other.accountId, signedAmount: other.quantity };
}
