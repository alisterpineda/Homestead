import { signal, effect, type WritableSignal } from '@angular/core';

export interface PersistedSignalOptions<T> {
  key: string;
  defaultValue: T;
  storage?: 'localStorage' | 'sessionStorage';
  validate?: (value: unknown) => value is T;
}

/**
 * Creates a WritableSignal that persists to localStorage/sessionStorage via effect().
 * Must be called within an injection context (e.g., service constructor).
 * Only use in root-scoped services — the effect is never cleaned up.
 */
export function persistedSignal<T>(options: PersistedSignalOptions<T>): WritableSignal<T> {
  const { key, defaultValue, storage = 'localStorage', validate } = options;
  const storageRef = storage === 'sessionStorage' ? sessionStorage : localStorage;

  const initial = loadFromStorage(storageRef, key, defaultValue, validate);
  const sig = signal<T>(initial);

  let initialized = false;
  effect(() => {
    const value = sig();
    if (!initialized) {
      initialized = true;
      return;
    }
    try {
      storageRef.setItem(key, JSON.stringify(value));
    } catch {
      // QuotaExceededError or private browsing — silently ignore
    }
  });

  return sig;
}

function loadFromStorage<T>(
  storageRef: Storage,
  key: string,
  defaultValue: T,
  validate?: (value: unknown) => value is T,
): T {
  try {
    const raw = storageRef.getItem(key);
    if (raw === null) return defaultValue;
    const parsed: unknown = JSON.parse(raw);
    if (validate && !validate(parsed)) return defaultValue;
    return parsed as T;
  } catch {
    return defaultValue;
  }
}
