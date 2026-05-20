import { TestBed } from '@angular/core/testing';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';
import { FinancesPrivacyAutoMaskService } from './finances-privacy-auto-mask.service';

const REVEALED_AT_KEY = 'homestead-finances-privacy-revealed-at';
const MASKED_KEY = 'homestead-finances-privacy-masked';
const IDLE_TIMEOUT_MS = 5 * 60 * 1000;

describe('FinancesPrivacyAutoMaskService', () => {
  let mockStorage: Record<string, string>;
  let visibilityValue: boolean;

  function setMockStorage(): void {
    mockStorage = {};
    vi.stubGlobal('localStorage', {
      getItem: vi.fn((key: string) => (key in mockStorage ? mockStorage[key] : null)),
      setItem: vi.fn((key: string, value: string) => { mockStorage[key] = value; }),
      removeItem: vi.fn((key: string) => { delete mockStorage[key]; }),
    });
    vi.stubGlobal('sessionStorage', {
      getItem: vi.fn(() => null),
      setItem: vi.fn(),
      removeItem: vi.fn(),
    });
  }

  function setHidden(hidden: boolean): void {
    visibilityValue = hidden;
    document.dispatchEvent(new Event('visibilitychange'));
  }

  beforeEach(() => {
    setMockStorage();
    visibilityValue = false;
    Object.defineProperty(document, 'hidden', {
      configurable: true,
      get: () => visibilityValue,
    });
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-05-20T12:00:00Z'));
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  function instantiate(): { service: FinancesPrivacyAutoMaskService; prefs: UserPreferencesService } {
    const prefs = TestBed.inject(UserPreferencesService);
    const service = TestBed.inject(FinancesPrivacyAutoMaskService);
    TestBed.flushEffects();
    return { service, prefs };
  }

  it('re-masks after the idle timeout when no activity occurs', () => {
    const { prefs } = instantiate();
    prefs.financesPrivacyMasked.set(false);
    TestBed.flushEffects();
    expect(prefs.financesPrivacyMasked()).toBe(false);

    vi.advanceTimersByTime(IDLE_TIMEOUT_MS);
    TestBed.flushEffects();
    expect(prefs.financesPrivacyMasked()).toBe(true);
  });

  it('activity resets the idle timer', () => {
    const { prefs } = instantiate();
    prefs.financesPrivacyMasked.set(false);
    TestBed.flushEffects();

    vi.advanceTimersByTime(IDLE_TIMEOUT_MS - 1000);
    window.dispatchEvent(new Event('pointermove'));

    // Without the reset above, this second advance would have fired the timer.
    vi.advanceTimersByTime(IDLE_TIMEOUT_MS - 1000);
    TestBed.flushEffects();
    expect(prefs.financesPrivacyMasked()).toBe(false);

    // Now let it idle out.
    vi.advanceTimersByTime(2000);
    TestBed.flushEffects();
    expect(prefs.financesPrivacyMasked()).toBe(true);
  });

  it('re-masks on visibility change when the tab was hidden longer than the timeout', () => {
    const { prefs } = instantiate();
    prefs.financesPrivacyMasked.set(false);
    TestBed.flushEffects();

    setHidden(true);
    // Simulate real-world elapsed time while hidden — we cannot rely on the
    // in-memory timer because we cleared it on hidden.
    vi.setSystemTime(new Date(Date.now() + IDLE_TIMEOUT_MS + 1000));
    setHidden(false);
    TestBed.flushEffects();

    expect(prefs.financesPrivacyMasked()).toBe(true);
  });

  it('stays unmasked when the hidden interval is shorter than the timeout', () => {
    const { prefs } = instantiate();
    prefs.financesPrivacyMasked.set(false);
    TestBed.flushEffects();

    setHidden(true);
    vi.setSystemTime(new Date(Date.now() + 60 * 1000));
    setHidden(false);
    TestBed.flushEffects();

    expect(prefs.financesPrivacyMasked()).toBe(false);
  });

  it('masks immediately on cold start when the persisted revealed-at is stale', () => {
    mockStorage[MASKED_KEY] = 'false';
    mockStorage[REVEALED_AT_KEY] = JSON.stringify(Date.now() - (IDLE_TIMEOUT_MS + 1000));

    const { prefs } = instantiate();
    expect(prefs.financesPrivacyMasked()).toBe(true);
  });

  it('masks immediately on cold start when unmasked but no revealed-at anchor exists', () => {
    mockStorage[MASKED_KEY] = 'false';

    const { prefs } = instantiate();
    expect(prefs.financesPrivacyMasked()).toBe(true);
  });

  it('keeps revealed state on cold start when the anchor is fresh', () => {
    mockStorage[MASKED_KEY] = 'false';
    mockStorage[REVEALED_AT_KEY] = JSON.stringify(Date.now() - 1000);

    const { prefs } = instantiate();
    expect(prefs.financesPrivacyMasked()).toBe(false);
  });

  it('clears the persisted revealed-at when re-masking', () => {
    const { prefs } = instantiate();
    prefs.financesPrivacyMasked.set(false);
    TestBed.flushEffects();
    expect(mockStorage[REVEALED_AT_KEY]).toBeDefined();

    vi.advanceTimersByTime(IDLE_TIMEOUT_MS);
    TestBed.flushEffects();
    expect(mockStorage[REVEALED_AT_KEY]).toBeUndefined();
  });
});
