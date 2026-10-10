import { Injectable } from '@angular/core';
import type { ColumnVisibilityState } from '@tanstack/angular-table';
import { persistedSignal } from '../utils/persisted-signal';
import type { NetWorthRange } from '@/api/generated/models/net-worth-range';

/** Top-level module base paths eligible for last-module cold-start restore.
 *  Keep in sync with the AppShell children in app.routes.ts and modules/registry.ts basePaths. */
const KNOWN_MODULE_PATHS = ['/documents', '/finances', '/inventory', '/admin'] as const;

const NET_WORTH_RANGE_KINDS: readonly NetWorthRange[] = [
  'ThisMonth',
  'ThreeMonths',
  'SixMonths',
  'TwelveMonths',
  'YearToDate',
  'LastYear',
  'All',
  'Custom',
];

export interface NetWorthRangePref {
  readonly kind: NetWorthRange;
  // ISO yyyy-MM-dd; only when kind === 'Custom'
  readonly from?: string;
  readonly to?: string;
}

const ISO_DATE_RE = /^\d{4}-\d{2}-\d{2}$/;

function isIsoDate(v: unknown): v is string {
  return typeof v === 'string' && ISO_DATE_RE.test(v) && !Number.isNaN(Date.parse(v));
}

function isNetWorthRangePref(v: unknown): v is NetWorthRangePref {
  if (typeof v !== 'object' || v === null) return false;
  const obj = v as Record<string, unknown>;
  if (typeof obj['kind'] !== 'string') return false;
  if (!(NET_WORTH_RANGE_KINDS as readonly string[]).includes(obj['kind'])) return false;
  if (obj['kind'] === 'Custom') {
    return isIsoDate(obj['from']) && isIsoDate(obj['to']);
  }
  return true;
}

function isVisibilityState(v: unknown): v is ColumnVisibilityState {
  return typeof v === 'object' && v !== null && !Array.isArray(v) &&
    Object.values(v as Record<string, unknown>).every(val => typeof val === 'boolean');
}

@Injectable({ providedIn: 'root' })
export class UserPreferencesService {
  readonly pageSize = persistedSignal<number>({
    key: 'homestead-page-size',
    defaultValue: 50,
    validate: (v): v is number => typeof v === 'number' && [10, 20, 50, 100].includes(v),
  });

  /** Base path of the module the user was last in; restored on cold start at '/'. */
  readonly lastModulePath = persistedSignal<string>({
    key: 'homestead-last-module',
    defaultValue: '/documents',
    validate: (v): v is string =>
      typeof v === 'string' && (KNOWN_MODULE_PATHS as readonly string[]).includes(v),
  });

  readonly documentViewMode = persistedSignal<'table' | 'grid'>({
    key: 'homestead-document-view-mode',
    defaultValue: 'grid',
    validate: (v): v is 'table' | 'grid' => v === 'table' || v === 'grid',
  });

  readonly accountViewMode = persistedSignal<'table' | 'card' | null>({
    key: 'homestead-account-view-mode',
    defaultValue: null,
    validate: (v): v is 'table' | 'card' | null =>
      v === null || v === 'table' || v === 'card',
  });

  readonly inventoryItemViewMode = persistedSignal<'table' | 'card' | null>({
    key: 'homestead-inventory-item-view-mode',
    defaultValue: null,
    validate: (v): v is 'table' | 'card' | null =>
      v === null || v === 'table' || v === 'card',
  });

  readonly documentColumnVisibility = persistedSignal<ColumnVisibilityState>({
    key: 'homestead-document-columns',
    defaultValue: {},
    validate: isVisibilityState,
  });

  readonly partyColumnVisibility = persistedSignal<ColumnVisibilityState>({
    key: 'homestead-party-columns',
    defaultValue: {},
    validate: isVisibilityState,
  });

  readonly tagColumnVisibility = persistedSignal<ColumnVisibilityState>({
    key: 'homestead-tag-columns',
    defaultValue: {},
    validate: isVisibilityState,
  });

  readonly inventoryTagColumnVisibility = persistedSignal<ColumnVisibilityState>({
    key: 'homestead-inventory-tag-column-visibility',
    defaultValue: {},
    validate: isVisibilityState,
  });

  readonly netWorthRange = persistedSignal<NetWorthRangePref>({
    key: 'homestead-finances-net-worth-range',
    defaultValue: { kind: 'All' },
    validate: isNetWorthRangePref,
  });

  readonly netWorthShowBreakdown = persistedSignal<boolean>({
    key: 'homestead-finances-net-worth-show-breakdown',
    defaultValue: false,
    validate: (v): v is boolean => typeof v === 'boolean',
  });

  readonly financesPrivacyMasked = persistedSignal<boolean>({
    key: 'homestead-finances-privacy-masked',
    defaultValue: true,
    validate: (v): v is boolean => typeof v === 'boolean',
  });
}
