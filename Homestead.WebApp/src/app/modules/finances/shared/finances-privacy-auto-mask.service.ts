import { Injectable, effect, inject } from '@angular/core';
import { UserPreferencesService } from '@/shared/services/user-preferences.service';

const REVEALED_AT_KEY = 'homestead-finances-privacy-revealed-at';
const IDLE_TIMEOUT_MS = 5 * 60 * 1000;
const ACTIVITY_THROTTLE_MS = 30 * 1000;

@Injectable({ providedIn: 'root' })
export class FinancesPrivacyAutoMaskService {
  private readonly prefs = inject(UserPreferencesService);

  private idleTimer: ReturnType<typeof setTimeout> | null = null;
  private hiddenAt: number | null = null;
  private lastActivityWriteAt = 0;
  private attached = false;

  constructor() {
    const revealedAt = this.readRevealedAt();
    const stale = revealedAt === null || Date.now() - revealedAt >= IDLE_TIMEOUT_MS;
    if (!this.prefs.financesPrivacyMasked() && stale) {
      this.prefs.financesPrivacyMasked.set(true);
    }

    effect(() => {
      const masked = this.prefs.financesPrivacyMasked();
      if (masked) {
        this.detach();
      } else {
        this.attach();
      }
    });
  }

  private attach(): void {
    if (this.attached) return;
    this.attached = true;

    const now = Date.now();
    this.lastActivityWriteAt = now;
    this.writeRevealedAt(now);

    window.addEventListener('pointermove', this.onActivity, { passive: true });
    window.addEventListener('pointerdown', this.onActivity, { passive: true });
    window.addEventListener('wheel', this.onActivity, { passive: true });
    window.addEventListener('touchstart', this.onActivity, { passive: true });
    window.addEventListener('keydown', this.onActivity);
    document.addEventListener('visibilitychange', this.onVisibilityChange);
    window.addEventListener('pagehide', this.onPageHide);

    this.armIdleTimer(IDLE_TIMEOUT_MS);
  }

  private detach(): void {
    if (!this.attached) return;
    this.attached = false;

    this.clearIdleTimer();
    this.hiddenAt = null;
    this.lastActivityWriteAt = 0;

    window.removeEventListener('pointermove', this.onActivity);
    window.removeEventListener('pointerdown', this.onActivity);
    window.removeEventListener('wheel', this.onActivity);
    window.removeEventListener('touchstart', this.onActivity);
    window.removeEventListener('keydown', this.onActivity);
    document.removeEventListener('visibilitychange', this.onVisibilityChange);
    window.removeEventListener('pagehide', this.onPageHide);

    try {
      localStorage.removeItem(REVEALED_AT_KEY);
    } catch {
      // ignore
    }
  }

  private readonly onActivity = (): void => {
    this.armIdleTimer(IDLE_TIMEOUT_MS);
    const now = Date.now();
    if (now - this.lastActivityWriteAt >= ACTIVITY_THROTTLE_MS) {
      this.lastActivityWriteAt = now;
      this.writeRevealedAt(now);
    }
  };

  private readonly onVisibilityChange = (): void => {
    if (document.hidden) {
      this.clearIdleTimer();
      this.hiddenAt = Date.now();
      this.writeRevealedAt(this.hiddenAt);
    } else {
      const elapsed = this.hiddenAt === null ? 0 : Date.now() - this.hiddenAt;
      this.hiddenAt = null;
      if (elapsed >= IDLE_TIMEOUT_MS) {
        this.remask();
      } else {
        this.armIdleTimer(IDLE_TIMEOUT_MS - elapsed);
      }
    }
  };

  private readonly onPageHide = (): void => {
    this.writeRevealedAt(Date.now());
  };

  private armIdleTimer(ms: number): void {
    this.clearIdleTimer();
    this.idleTimer = setTimeout(() => this.remask(), ms);
  }

  private clearIdleTimer(): void {
    if (this.idleTimer !== null) {
      clearTimeout(this.idleTimer);
      this.idleTimer = null;
    }
  }

  private remask(): void {
    this.prefs.financesPrivacyMasked.set(true);
  }

  private readRevealedAt(): number | null {
    try {
      const raw = localStorage.getItem(REVEALED_AT_KEY);
      if (raw === null) return null;
      const parsed: unknown = JSON.parse(raw);
      return typeof parsed === 'number' && Number.isFinite(parsed) ? parsed : null;
    } catch {
      return null;
    }
  }

  private writeRevealedAt(value: number): void {
    try {
      localStorage.setItem(REVEALED_AT_KEY, JSON.stringify(value));
    } catch {
      // ignore
    }
  }
}
