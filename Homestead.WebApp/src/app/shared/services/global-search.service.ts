import { Injectable, signal } from '@angular/core';

/**
 * Tiny coordinator that decouples the shell header (the search trigger) from the command palette
 * (the overlay). Both inject this singleton: the header calls {@link openPalette} / {@link toggle},
 * the palette reads {@link open} and calls {@link close}.
 */
@Injectable({ providedIn: 'root' })
export class GlobalSearchService {
  /** Whether the command palette overlay is open. */
  readonly open = signal(false);

  openPalette(): void {
    this.open.set(true);
  }

  close(): void {
    this.open.set(false);
  }

  toggle(): void {
    this.open.update((v) => !v);
  }
}
