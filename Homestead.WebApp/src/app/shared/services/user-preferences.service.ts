import { Injectable } from '@angular/core';
import type { VisibilityState } from '@tanstack/angular-table';
import { persistedSignal } from '../utils/persisted-signal';
import type { NetWorthRange } from '@/api/generated/models/net-worth-range';

const OLD_VIEW_MODE_KEY = 'document-view-mode';

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

function isVisibilityState(v: unknown): v is VisibilityState {
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

  readonly documentViewMode = persistedSignal<'table' | 'grid'>({
    key: 'homestead-document-view-mode',
    defaultValue: 'grid',
    validate: (v): v is 'table' | 'grid' => v === 'table' || v === 'grid',
  });

  readonly documentColumnVisibility = persistedSignal<VisibilityState>({
    key: 'homestead-document-columns',
    defaultValue: {},
    validate: isVisibilityState,
  });

  readonly partyColumnVisibility = persistedSignal<VisibilityState>({
    key: 'homestead-party-columns',
    defaultValue: {},
    validate: isVisibilityState,
  });

  readonly tagColumnVisibility = persistedSignal<VisibilityState>({
    key: 'homestead-tag-columns',
    defaultValue: {},
    validate: isVisibilityState,
  });

  readonly netWorthRange = persistedSignal<NetWorthRangePref>({
    key: 'homestead-finances-net-worth-range',
    defaultValue: { kind: 'All' },
    validate: isNetWorthRangePref,
  });

  constructor() {
    this.migrateOldViewModeKey();
  }

  private migrateOldViewModeKey(): void {
    try {
      const old = localStorage.getItem(OLD_VIEW_MODE_KEY);
      if (old === 'table' || old === 'grid') {
        this.documentViewMode.set(old);
        localStorage.removeItem(OLD_VIEW_MODE_KEY);
      }
    } catch {
      // ignore
    }
  }
}
