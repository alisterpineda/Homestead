import { Injectable, signal } from '@angular/core';

const MD_BREAKPOINT = 768;

@Injectable({ providedIn: 'root' })
export class SidebarService {
  readonly isOpen = signal(false);

  constructor() {
    const mql = window.matchMedia(`(min-width: ${MD_BREAKPOINT}px)`);
    mql.addEventListener('change', (e) => {
      if (e.matches) this.isOpen.set(false);
    });
  }

  open(): void {
    this.isOpen.set(true);
  }

  close(): void {
    this.isOpen.set(false);
  }

  toggle(): void {
    this.isOpen.update((v) => !v);
  }
}
