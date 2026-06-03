import type { AccountKind } from '@/api/generated/models/account-kind';

export type UserAccountKind = AccountKind;

export const USER_ACCOUNT_KINDS: readonly UserAccountKind[] = [
  'Cash',
  'Investment',
  'OtherAsset',
  'CreditCard',
  'Loan',
  'OtherLiability',
];

export const KIND_LABELS: Record<UserAccountKind, string> = {
  Cash: 'Cash',
  Investment: 'Investment',
  OtherAsset: 'Other Asset',
  CreditCard: 'Credit Card',
  Loan: 'Loan',
  OtherLiability: 'Other Liability',
};

export const KIND_DESCRIPTIONS: Record<UserAccountKind, string> = {
  Cash: 'Chequing, savings, physical cash, prepaid cards',
  Investment: 'RRSP, TFSA, brokerage, employer plan',
  OtherAsset: 'House, vehicle, collectibles',
  CreditCard: 'Displayed as positive amount owed',
  Loan: 'Mortgage, student loan, auto loan',
  OtherLiability: 'Money owed to family, taxes due, lease',
};

// lucide icon name per kind — caller must provideIcons() in the host component.
export const KIND_ICONS: Record<UserAccountKind, string> = {
  Cash: 'lucideWallet',
  Investment: 'lucideChartLine',
  OtherAsset: 'lucidePackage',
  CreditCard: 'lucideCreditCard',
  Loan: 'lucideLandmark',
  OtherLiability: 'lucideCircleMinus',
};

export function isUserAccountKind(kind: AccountKind | null | undefined): kind is UserAccountKind {
  return kind != null && USER_ACCOUNT_KINDS.includes(kind);
}

export function accountKindLabel(kind: AccountKind | null | undefined, fallback = ''): string {
  return isUserAccountKind(kind) ? KIND_LABELS[kind] : fallback;
}
