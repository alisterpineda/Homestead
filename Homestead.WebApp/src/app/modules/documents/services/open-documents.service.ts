import { Injectable, signal } from '@angular/core';
import { persistedSignal } from '@/shared/utils/persisted-signal';

export interface OpenDocument {
  id: string;
  name: string;
}

const MAX_OPEN = 20;

function isOpenDocument(d: unknown): d is OpenDocument {
  return typeof d === 'object' && d !== null &&
    typeof (d as OpenDocument).id === 'string' &&
    typeof (d as OpenDocument).name === 'string';
}

@Injectable({ providedIn: 'root' })
export class OpenDocumentsService {
  readonly openDocuments = persistedSignal<OpenDocument[]>({
    key: 'homestead-open-documents',
    defaultValue: [],
    validate: (v): v is OpenDocument[] => Array.isArray(v) && v.every(isOpenDocument),
  });

  // Transient — not persisted
  readonly navigationList = signal<string[]>([], {
    equal: (a, b) => a.length === b.length && a.every((v, i) => v === b[i]),
  });

  addDocument(id: string, name: string): void {
    this.openDocuments.update(docs => {
      const existing = docs.find(d => d.id === id);
      if (existing) {
        return existing.name === name ? docs : docs.map(d => d.id === id ? { id, name } : d);
      }
      return [...docs, { id, name }].slice(-MAX_OPEN);
    });
  }

  removeDocument(id: string): void {
    if (!this.openDocuments().some(d => d.id === id)) return;
    this.openDocuments.update(docs => docs.filter(d => d.id !== id));
  }

  clearAll(): void {
    this.openDocuments.set([]);
  }

  setNavigationList(ids: string[]): void {
    this.navigationList.set(ids);
  }

  getPrevDocumentId(currentId: string): string | null {
    const list = this.navigationList();
    const idx = list.indexOf(currentId);
    return idx > 0 ? list[idx - 1] : null;
  }

  getNextDocumentId(currentId: string): string | null {
    const list = this.navigationList();
    const idx = list.indexOf(currentId);
    return idx >= 0 && idx < list.length - 1 ? list[idx + 1] : null;
  }
}
