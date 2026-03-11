import { Injectable, signal } from '@angular/core';

export interface OpenDocument {
  id: string;
  name: string;
}

const STORAGE_KEY = 'homestead-open-documents';
const MAX_OPEN = 20;

@Injectable({ providedIn: 'root' })
export class OpenDocumentsService {
  readonly openDocuments = signal<OpenDocument[]>(this.loadFromStorage());

  // Transient — not persisted
  readonly navigationList = signal<string[]>([], {
    equal: (a, b) => a.length === b.length && a.every((v, i) => v === b[i]),
  });

  addDocument(id: string, name: string): void {
    const before = this.openDocuments();
    this.openDocuments.update(docs => {
      const existing = docs.find(d => d.id === id);
      if (existing) {
        return existing.name === name ? docs : docs.map(d => d.id === id ? { id, name } : d);
      }
      return [...docs, { id, name }].slice(-MAX_OPEN);
    });
    if (this.openDocuments() !== before) {
      this.saveToStorage();
    }
  }

  removeDocument(id: string): void {
    if (!this.openDocuments().some(d => d.id === id)) return;
    this.openDocuments.update(docs => docs.filter(d => d.id !== id));
    this.saveToStorage();
  }

  clearAll(): void {
    this.openDocuments.set([]);
    this.saveToStorage();
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

  private loadFromStorage(): OpenDocument[] {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return [];
      const parsed = JSON.parse(raw);
      if (!Array.isArray(parsed)) return [];
      return parsed.filter(
        (d: unknown): d is OpenDocument =>
          typeof d === 'object' && d !== null && typeof (d as OpenDocument).id === 'string' && typeof (d as OpenDocument).name === 'string',
      );
    } catch {
      return [];
    }
  }

  private saveToStorage(): void {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(this.openDocuments()));
  }
}
