import { Injectable } from '@angular/core';
import { Params } from '@angular/router';
import type { SearchHit } from '@/api/generated/models/search-hit';

/**
 * How the palette renders and routes one source's results. Adding a module to universal search is
 * purely additive: implement an {@link SearchSource} on the backend, then register one presenter
 * here keyed by the same `source` string — the palette and the shell never change.
 */
export interface SourcePresenter {
  /** ng-icon name for a hit (varies by `hit.type`). */
  icon: (hit: SearchHit) => string;
  /** Router commands to navigate to a hit's detail. */
  navigate: (hit: SearchHit) => unknown[];
  /** Deep link into the source's own list, pre-filtered to the query ("View all"). */
  viewAll: (query: string) => { commands: unknown[]; queryParams: Params };
}

@Injectable({ providedIn: 'root' })
export class SearchPresenterRegistry {
  // Keyed by SearchHit.source / SearchGroup.source. v1 ships one entry; other modules add their own.
  private readonly presenters: Record<string, SourcePresenter> = {
    inventory: {
      icon: (hit) => (hit.type === 'durable' ? 'lucidePackage' : 'lucideBoxes'),
      navigate: (hit) => ['/inventory/items', hit.id],
      viewAll: (query) => ({ commands: ['/inventory/items'], queryParams: { search: query } }),
    },
    documents: {
      icon: (hit) => (hit.type === 'image' ? 'lucideImage' : 'lucideFileText'),
      navigate: (hit) => ['/documents', hit.id],
      viewAll: (query) => ({ commands: ['/documents'], queryParams: { search: query } }),
    },
  };

  get(source: string): SourcePresenter | undefined {
    return this.presenters[source];
  }
}
