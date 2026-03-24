import { Injectable, signal, computed, effect } from '@angular/core';
import { persistedSignal } from '../utils/persisted-signal';

export type ThemeMode = 'light' | 'dark' | 'system';

function getSystemTheme(): 'light' | 'dark' {
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly mode = persistedSignal<ThemeMode>({
    key: 'homestead-theme',
    defaultValue: 'system',
    validate: (v): v is ThemeMode => v === 'light' || v === 'dark' || v === 'system',
  });
  readonly systemTheme = signal<'light' | 'dark'>(getSystemTheme());
  readonly resolved = computed<'light' | 'dark'>(() => {
    const m = this.mode();
    return m === 'system' ? this.systemTheme() : m;
  });

  constructor() {
    effect(() => {
      const resolved = this.resolved();
      if (resolved === 'dark') {
        document.documentElement.classList.add('dark');
      } else {
        document.documentElement.classList.remove('dark');
      }
    });

    const mq = window.matchMedia('(prefers-color-scheme: dark)');
    mq.addEventListener('change', () => {
      this.systemTheme.set(getSystemTheme());
    });
  }

  setMode(mode: ThemeMode): void {
    this.mode.set(mode);
  }
}
