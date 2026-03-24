import { Injectable } from '@angular/core';
import type { VisibilityState } from '@tanstack/angular-table';
import { persistedSignal } from '../utils/persisted-signal';

const OLD_VIEW_MODE_KEY = 'document-view-mode';

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
