import { TestBed } from '@angular/core/testing';
import { persistedSignal } from './persisted-signal';

describe('persistedSignal', () => {
  let mockStorage: Record<string, string>;

  beforeEach(() => {
    mockStorage = {};
    vi.stubGlobal('localStorage', {
      getItem: vi.fn((key: string) => mockStorage[key] ?? null),
      setItem: vi.fn((key: string, value: string) => { mockStorage[key] = value; }),
      removeItem: vi.fn((key: string) => { delete mockStorage[key]; }),
    });
    vi.stubGlobal('sessionStorage', {
      getItem: vi.fn((key: string) => mockStorage[key] ?? null),
      setItem: vi.fn((key: string, value: string) => { mockStorage[key] = value; }),
      removeItem: vi.fn((key: string) => { delete mockStorage[key]; }),
    });
  });

  afterEach(() => vi.unstubAllGlobals());

  it('should return defaultValue when storage is empty', () => {
    TestBed.runInInjectionContext(() => {
      const sig = persistedSignal({ key: 'test', defaultValue: 42 });
      expect(sig()).toBe(42);
    });
  });

  it('should load from storage when a valid value exists', () => {
    mockStorage['test'] = '99';
    TestBed.runInInjectionContext(() => {
      const sig = persistedSignal({ key: 'test', defaultValue: 42 });
      expect(sig()).toBe(99);
    });
  });

  it('should fall back to defaultValue on invalid JSON', () => {
    mockStorage['test'] = 'not-json{';
    TestBed.runInInjectionContext(() => {
      const sig = persistedSignal({ key: 'test', defaultValue: 42 });
      expect(sig()).toBe(42);
    });
  });

  it('should fall back to defaultValue when validate rejects the value', () => {
    mockStorage['test'] = '"invalid"';
    TestBed.runInInjectionContext(() => {
      const sig = persistedSignal({
        key: 'test',
        defaultValue: 'a' as 'a' | 'b',
        validate: (v): v is 'a' | 'b' => v === 'a' || v === 'b',
      });
      expect(sig()).toBe('a');
    });
  });

  it('should accept a value that passes validation', () => {
    mockStorage['test'] = '"b"';
    TestBed.runInInjectionContext(() => {
      const sig = persistedSignal({
        key: 'test',
        defaultValue: 'a' as 'a' | 'b',
        validate: (v): v is 'a' | 'b' => v === 'a' || v === 'b',
      });
      expect(sig()).toBe('b');
    });
  });

  it('should not write to storage on initial effect run', () => {
    mockStorage['test'] = '99';
    TestBed.runInInjectionContext(() => {
      persistedSignal({ key: 'test', defaultValue: 1 });
      TestBed.flushEffects();
      expect(localStorage.setItem).not.toHaveBeenCalled();
    });
  });

  it('should sync to storage when signal is set', () => {
    TestBed.runInInjectionContext(() => {
      const sig = persistedSignal({ key: 'test', defaultValue: 1 });
      TestBed.flushEffects(); // initial skip
      sig.set(42);
      TestBed.flushEffects();
      expect(localStorage.setItem).toHaveBeenCalledWith('test', '42');
    });
  });

  it('should sync to storage when signal is updated', () => {
    TestBed.runInInjectionContext(() => {
      const sig = persistedSignal({ key: 'test', defaultValue: 10 });
      TestBed.flushEffects(); // initial skip
      sig.update(v => v + 5);
      TestBed.flushEffects();
      expect(localStorage.setItem).toHaveBeenCalledWith('test', '15');
    });
  });

  it('should use sessionStorage when storage option is sessionStorage', () => {
    mockStorage['test'] = '"hello"';
    TestBed.runInInjectionContext(() => {
      const sig = persistedSignal({ key: 'test', defaultValue: 'default', storage: 'sessionStorage' });
      expect(sig()).toBe('hello');
      expect(sessionStorage.getItem).toHaveBeenCalledWith('test');
    });
  });

  it('should write to sessionStorage when storage option is sessionStorage', () => {
    TestBed.runInInjectionContext(() => {
      const sig = persistedSignal({ key: 'test', defaultValue: 'a', storage: 'sessionStorage' });
      TestBed.flushEffects(); // initial skip
      sig.set('b');
      TestBed.flushEffects();
      expect(sessionStorage.setItem).toHaveBeenCalledWith('test', '"b"');
    });
  });

  it('should handle QuotaExceededError on setItem gracefully', () => {
    TestBed.runInInjectionContext(() => {
      const sig = persistedSignal({ key: 'test', defaultValue: 1 });
      TestBed.flushEffects(); // initial skip
      (localStorage.setItem as ReturnType<typeof vi.fn>).mockImplementation(() => {
        throw new DOMException('quota exceeded', 'QuotaExceededError');
      });
      expect(() => {
        sig.set(2);
        TestBed.flushEffects();
      }).not.toThrow();
      expect(sig()).toBe(2);
    });
  });

  it('should load objects from storage', () => {
    mockStorage['test'] = '{"a":1,"b":true}';
    TestBed.runInInjectionContext(() => {
      const sig = persistedSignal({ key: 'test', defaultValue: {} as Record<string, unknown> });
      expect(sig()).toEqual({ a: 1, b: true });
    });
  });

  it('should load arrays from storage', () => {
    mockStorage['test'] = '[1,2,3]';
    TestBed.runInInjectionContext(() => {
      const sig = persistedSignal({ key: 'test', defaultValue: [] as number[] });
      expect(sig()).toEqual([1, 2, 3]);
    });
  });
});
