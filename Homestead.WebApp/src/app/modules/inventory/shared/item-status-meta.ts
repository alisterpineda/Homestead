import type { ItemStatus } from '@/api/generated/models/item-status';

export interface ItemStatusMeta {
  readonly label: string;
  /** Full Tailwind class string for the status pill (self-contained — not an hlmBadge variant). */
  readonly badgeClass: string;
  readonly terminal: boolean;
}

/** Shared structural base for self-contained status pills — tone tokens are appended per badge. */
export const BADGE_BASE =
  'inline-flex h-5 w-fit shrink-0 items-center rounded-full px-2 py-0.5 text-xs font-medium whitespace-nowrap';

/**
 * Stock-signal badge classes — self-contained pills for a fungible item's stock level, built on the
 * shared {@link BADGE_BASE}. Amber (warning) means low; destructive (empty) means out. Defining the two
 * tones together, distinct from each other, is what stops "low" and "out" reading as the same alert.
 * The low-stock pill carries an icon, so it adds `gap-1`; the "out" pill is text-only.
 */
export const LOW_STOCK_BADGE_CLASS = `${BADGE_BASE} gap-1 bg-amber-100 text-amber-700 dark:bg-amber-950 dark:text-amber-300`;
export const OUT_OF_STOCK_BADGE_CLASS = `${BADGE_BASE} bg-destructive/10 text-destructive dark:bg-destructive/20`;

export const ITEM_STATUS_META: Record<ItemStatus, ItemStatusMeta> = {
  Active: {
    label: 'Active',
    badgeClass: `${BADGE_BASE} bg-emerald-100 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300`,
    terminal: false,
  },
  Lent: {
    label: 'Lent',
    badgeClass: `${BADGE_BASE} bg-amber-100 text-amber-700 dark:bg-amber-950 dark:text-amber-300`,
    terminal: false,
  },
  InRepair: {
    label: 'In repair',
    badgeClass: `${BADGE_BASE} bg-sky-100 text-sky-700 dark:bg-sky-950 dark:text-sky-300`,
    terminal: false,
  },
  Sold: {
    label: 'Sold',
    badgeClass: `${BADGE_BASE} bg-muted text-muted-foreground`,
    terminal: true,
  },
  Discarded: {
    label: 'Discarded',
    badgeClass: `${BADGE_BASE} bg-destructive/10 text-destructive dark:bg-destructive/20`,
    terminal: true,
  },
  Archived: {
    label: 'Archived',
    badgeClass: `${BADGE_BASE} bg-muted text-muted-foreground`,
    terminal: true,
  },
};

/** All statuses, in lifecycle order — for status select dropdowns. */
export const ALL_ITEM_STATUSES: readonly ItemStatus[] = [
  'Active', 'Lent', 'InRepair', 'Sold', 'Discarded', 'Archived',
];

export function itemStatusLabel(status: ItemStatus): string {
  return ITEM_STATUS_META[status]?.label ?? status;
}

export function isTerminalStatus(status: ItemStatus): boolean {
  return ITEM_STATUS_META[status]?.terminal ?? false;
}
