import { Injectable, signal, computed, effect } from '@angular/core';
import { persistedSignal } from '../utils/persisted-signal';

export type ThemeMode = 'light' | 'dark' | 'system';

/* Hex equivalents of --background (oklch(1 0 0) / oklch(0.145 0 0)) for the theme-color meta,
   which does not accept CSS variables. Keep in sync with styles.css and index.html. */
const THEME_COLOR: Record<'light' | 'dark', string> = { light: '#ffffff', dark: '#0a0a0a' };

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
      // index.html ships media-scoped theme-color metas for the pre-boot paint; once the resolved
      // theme is known (it may be a manual override that disagrees with prefers-color-scheme),
      // point every variant at the resolved colour so iOS keeps the status bar matching the header.
      document
        .querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]')
        .forEach((meta) => meta.setAttribute('content', THEME_COLOR[resolved]));
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
