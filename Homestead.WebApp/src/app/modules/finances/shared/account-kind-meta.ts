import type { AccountKind } from '@/api/generated/models/account-kind';

// The generated AccountKind union literally includes the string 'null'
// (an ng-openapi-gen artifact from the C# AccountKind? nullability). We never
// surface that value in the UI — this constant is the source of truth for
// "real" user-creatable kinds.
export type UserAccountKind = Exclude<AccountKind, 'null'>;

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

// Display order of groups on the account list (Cash first, liabilities last).
export const KIND_GROUPS: ReadonlyArray<{
  kind: UserAccountKind;
  sectionLabel: string;
}> = [
  { kind: 'Cash', sectionLabel: 'Cash' },
  { kind: 'Investment', sectionLabel: 'Investments' },
  { kind: 'OtherAsset', sectionLabel: 'Other Assets' },
  { kind: 'CreditCard', sectionLabel: 'Credit Cards' },
  { kind: 'Loan', sectionLabel: 'Loans' },
  { kind: 'OtherLiability', sectionLabel: 'Other Liabilities' },
];

export function isUserAccountKind(kind: AccountKind | null | undefined): kind is UserAccountKind {
  return kind !== null && kind !== undefined && kind !== 'null' && USER_ACCOUNT_KINDS.includes(kind as UserAccountKind);
}
