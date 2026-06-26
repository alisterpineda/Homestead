import { Injectable, signal, computed } from '@angular/core';
import { persistedSignal } from '@/shared/utils/persisted-signal';

export interface DocumentEditState {
  editName: string;
  editDocumentDate: string | null;
  editExpiryDate: string | null;
  editNotes: string;
  editParties: Array<{ id: string; name: string }>;
  editTags: string[];
  editVersionLabel: string;
  editExtractedText: string;
  versionId: string | null;
}

const STORAGE_KEY = 'homestead-document-edit-state';

function isStateRecord(v: unknown): v is Record<string, DocumentEditState> {
  return typeof v === 'object' && v !== null && !Array.isArray(v);
}

@Injectable({ providedIn: 'root' })
export class DocumentEditStateService {
  private activeChanges = signal<Record<string, boolean>>({});

  private allStates = persistedSignal<Record<string, DocumentEditState>>({
    key: STORAGE_KEY,
    defaultValue: {},
    storage: 'sessionStorage',
    validate: isStateRecord,
  });

  private storedDocIds = computed<Set<string>>(() => new Set(Object.keys(this.allStates())));

  saveState(docId: string, state: DocumentEditState): void {
    this.allStates.update((all) => ({ ...all, [docId]: state }));
  }

  getState(docId: string): DocumentEditState | null {
    return this.allStates()[docId] ?? null;
  }

  hasState(docId: string): boolean {
    return this.storedDocIds().has(docId);
  }

  clearState(docId: string): void {
    if (!(docId in this.allStates())) return;
    this.allStates.update((current) => {
      const next = { ...current };
      delete next[docId];
      return next;
    });
  }

  discardChanges(docId: string): void {
    this.clearState(docId);
    this.setActiveHasChanges(docId, false);
  }

  clearAll(): void {
    this.allStates.set({});
    this.activeChanges.set({});
  }

  setActiveHasChanges(docId: string, hasChanges: boolean): void {
    this.activeChanges.update((prev) => {
      if (prev[docId] === hasChanges) return prev;
      return { ...prev, [docId]: hasChanges };
    });
  }

  unsavedDocIds = computed(() => {
    const ids = new Set<string>();
    const active = this.activeChanges();
    for (const [id, changed] of Object.entries(active)) {
      if (changed) ids.add(id);
    }
    for (const id of this.storedDocIds()) {
      ids.add(id);
    }
    return ids;
  });
}
