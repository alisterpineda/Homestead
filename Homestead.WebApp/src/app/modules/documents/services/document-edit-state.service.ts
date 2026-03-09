import { Injectable, signal, computed } from '@angular/core';

export interface DocumentEditState {
  editName: string;
  editDocumentDate: string | null;
  editValidFrom: string | null;
  editValidUntil: string | null;
  editParties: Array<{ id: string; name: string }>;
  editTags: string[];
  editVersionLabel: string;
  editExtractedText: string;
  versionId: string | null;
}

const STORAGE_KEY = 'homestead-document-edit-state';

@Injectable({ providedIn: 'root' })
export class DocumentEditStateService {
  /** Active changes tracked by the component (lightweight boolean per doc). */
  private activeChanges = signal<Record<string, boolean>>({});

  /** Stored state doc IDs (synced from sessionStorage). */
  private storedDocIds = signal<Set<string>>(this.loadStoredDocIds());

  saveState(docId: string, state: DocumentEditState): void {
    const all = this.loadAll();
    all[docId] = state;
    this.saveAll(all);
    this.storedDocIds.update((ids) => {
      if (ids.has(docId)) return ids;
      const next = new Set(ids);
      next.add(docId);
      return next;
    });
  }

  getState(docId: string): DocumentEditState | null {
    const all = this.loadAll();
    return all[docId] ?? null;
  }

  hasState(docId: string): boolean {
    return this.storedDocIds().has(docId);
  }

  clearState(docId: string): void {
    const all = this.loadAll();
    if (!(docId in all)) return;
    delete all[docId];
    this.saveAll(all);
    this.storedDocIds.update((ids) => {
      if (!ids.has(docId)) return ids;
      const next = new Set(ids);
      next.delete(docId);
      return next;
    });
  }

  /** Clear stored state and active tracking for a document (convenience for close/delete flows). */
  discardChanges(docId: string): void {
    this.clearState(docId);
    this.setActiveHasChanges(docId, false);
  }

  clearAll(): void {
    try {
      sessionStorage.removeItem(STORAGE_KEY);
    } catch {
      // ignore
    }
    this.storedDocIds.set(new Set());
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

  private loadAll(): Record<string, DocumentEditState> {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      if (!raw) return {};
      const parsed = JSON.parse(raw);
      if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return {};
      return parsed;
    } catch {
      return {};
    }
  }

  private saveAll(all: Record<string, DocumentEditState>): void {
    try {
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(all));
    } catch {
      // ignore
    }
  }

  private loadStoredDocIds(): Set<string> {
    const all = this.loadAll();
    return new Set(Object.keys(all));
  }
}
