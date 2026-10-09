import { Component, ChangeDetectionStrategy, inject, signal, OnInit, OnDestroy } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideRefreshCw, lucideTriangleAlert, lucideSearch } from '@ng-icons/lucide';
import { HlmCardImports } from '@/shared/components/spartan/card';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { AdminApiService } from '@/api/services/admin-api.service';
import type { LogLevel } from '@/api/generated/models/log-level';
import type { LogLevelSource } from '@/api/generated/models/log-level-source';
import type { LogLevelRule } from '@/api/generated/models/log-level-rule';
import type { ResolvedLogLevel } from '@/api/generated/models/resolved-log-level';
import type { LogLevelsResponse } from '@/api/generated/models/log-levels-response';

type BadgeVariant = 'default' | 'secondary' | 'outline';

@Component({
  selector: 'app-effective-log-levels',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon, HlmCardImports, HlmButton, HlmInput, HlmLabel,
    HlmSpinner, HlmEmptyImports, HlmBadge,
  ],
  providers: [provideIcons({ lucideRefreshCw, lucideTriangleAlert, lucideSearch })],
  template: `
    <div hlmCard class="mt-6">
      <div hlmCardHeader>
        <h2 hlmCardTitle>Effective log levels</h2>
        <p hlmCardDescription>
          The levels this instance is actually logging at right now, and where each one comes from.
        </p>
        <button hlmCardAction hlmBtn variant="ghost" size="icon" (click)="refresh()" [disabled]="loading()"
          aria-label="Refresh effective log levels">
          <ng-icon name="lucideRefreshCw" class="text-[length:--spacing(4)]" />
        </button>
      </div>

      <div hlmCardContent class="flex flex-col gap-5">
        @if (loading()) {
          <div class="flex justify-center py-10">
            <hlm-spinner />
          </div>
        } @else if (error()) {
          <div hlmEmpty>
            <div hlmEmptyHeader>
              <div hlmEmptyMedia variant="icon">
                <ng-icon name="lucideTriangleAlert" />
              </div>
              <div hlmEmptyTitle>Failed to load effective levels</div>
              <div hlmEmptyDescription>There was an error reading the live logging configuration.</div>
            </div>
            <div hlmEmptyContent>
              <button hlmBtn variant="outline" (click)="load()">Retry</button>
            </div>
          </div>
        } @else {
          <div class="flex flex-col">
            <div class="flex items-center gap-3 border-b border-border pb-2 text-xs font-medium text-muted-foreground">
              <span class="flex-1">Category</span>
              <span class="w-24">Level</span>
              <span class="w-32">Source</span>
            </div>
            @for (rule of rules(); track rule.category) {
              <div class="flex items-center gap-3 border-b border-border/50 py-2">
                <span class="flex-1 truncate font-mono text-sm" [title]="rule.category">{{ rule.category }}</span>
                <span class="w-24 text-sm">{{ rule.level }}</span>
                <span class="w-32">
                  <span hlmBadge [variant]="badgeVariant(rule.source)" [class]="badgeClass(rule.source)">
                    {{ rule.source }}
                  </span>
                </span>
              </div>
            } @empty {
              <p class="py-2 text-sm text-muted-foreground">No active rules.</p>
            }
          </div>

          <div class="flex flex-col gap-2">
            <label hlmLabel for="resolve-category">Resolve a namespace</label>
            <div class="flex gap-2">
              <input hlmInput id="resolve-category" class="flex-1"
                placeholder="Namespace, e.g. Homestead.WebApi.Modules.Documents.Foo"
                [value]="query()" (input)="query.set(asInputValue($event))"
                (keydown.enter)="resolve()" />
              <button hlmBtn variant="secondary" type="button" (click)="resolve()"
                [disabled]="resolving() || !query().trim()">
                @if (resolving()) {
                  <hlm-spinner />
                } @else {
                  <ng-icon name="lucideSearch" class="text-[length:--spacing(4)] mr-1" />
                  Check
                }
              </button>
            </div>

            @if (resolveError()) {
              <p class="text-sm text-destructive">Could not resolve that namespace. Please try again.</p>
            } @else if (resolved(); as r) {
              <div class="flex flex-col gap-1.5 rounded-md border border-border p-3">
                <div class="flex items-center gap-2">
                  <span class="flex-1 truncate font-mono text-sm" [title]="r.category">{{ r.category }}</span>
                  <span hlmBadge [variant]="badgeVariant(r.source)" [class]="badgeClass(r.source)">
                    {{ r.source }}
                  </span>
                </div>
                <p class="text-sm">
                  Effective level: <span class="font-medium">{{ r.effectiveLevel }}</span>
                </p>
                <p class="text-sm text-muted-foreground">
                  @if (r.matchedCategory === r.category) {
                    Set directly on this namespace.
                  } @else {
                    Inherited from <span class="font-mono">{{ r.matchedCategory }}</span>.
                  }
                </p>
              </div>
            }
          </div>
        }
      </div>

      @if (!loading() && !error()) {
        <div hlmCardFooter>
          <p class="text-sm text-muted-foreground">
            Reflects this instance's live configuration. Fallback minimum: {{ minLevel() }}.
          </p>
        </div>
      }
    </div>
  `,
})
export class EffectiveLogLevelsComponent implements OnInit, OnDestroy {
  private adminApi = inject(AdminApiService);

  // Backoff (ms) for converging the table on a save. This instance applies an override asynchronously via
  // the Postgres NOTIFY self-echo (the sole apply path — see SettingsChangeListener), so we poll the live
  // table until its Override rows match what was saved rather than guessing a fixed delay. The echo is
  // normally sub-50ms, so it converges on the first tick; the longer tail is insurance for a loaded DB.
  private static readonly CONVERGENCE_DELAYS_MS = [150, 350, 700, 1400];

  loading = signal(true);
  error = signal(false);
  rules = signal<LogLevelRule[]>([]);
  minLevel = signal<LogLevel>('Information');

  query = signal('');
  resolving = signal(false);
  resolveError = signal(false);
  resolved = signal<ResolvedLogLevel | null>(null);

  // The last namespace successfully resolved, so a post-save refresh can re-probe it and keep the resolved
  // block in step with the freshly-applied overrides.
  private lastResolved = signal<string | null>(null);

  private pollHandle: ReturnType<typeof setTimeout> | null = null;

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.cancelPoll();
  }

  load(): void {
    this.cancelPoll();
    this.loading.set(true);
    this.error.set(false);
    // A plain table reload supersedes any earlier resolve, so drop a stale "couldn't resolve" message.
    this.resolveError.set(false);
    this.adminApi.getLogLevels().subscribe({
      next: (res) => {
        this.loading.set(false);
        this.applyTable(res);
      },
      error: () => {
        this.loading.set(false);
        this.error.set(true);
      },
    });
  }

  /**
   * One-shot re-pull of the live table (the header refresh button). If a namespace is currently resolved,
   * re-resolve it too (one request returns both) so the whole view reflects the latest config.
   */
  refresh(): void {
    this.cancelPoll();
    const category = this.lastResolved();
    if (category) {
      // Re-probe the shown namespace directly — NOT via the query input, so an in-progress (unsubmitted)
      // entry the user is typing is left untouched.
      this.resolveCategory(category);
    } else {
      this.load();
    }
  }

  /**
   * Called by the parent after a save. The override is applied to this instance asynchronously (NOTIFY
   * self-echo), so poll the live table until its Override rows match the just-saved map, then stop. If it
   * never converges within the bounded schedule the table simply shows the latest read — the footer already
   * frames it as an eventually-consistent, per-instance view.
   */
  refreshAfterSave(savedOverrides: Record<string, LogLevel>): void {
    this.cancelPoll();
    const expected = normalizeOverrides(savedOverrides);
    const poll = (attempt: number): void => {
      this.pollHandle = setTimeout(() => {
        this.pollFetch((res) => {
          const converged = overridesConverged(res.rules ?? [], expected);
          if (!converged && attempt + 1 < EffectiveLogLevelsComponent.CONVERGENCE_DELAYS_MS.length) {
            poll(attempt + 1);
          }
        });
      }, EffectiveLogLevelsComponent.CONVERGENCE_DELAYS_MS[attempt]);
    };
    poll(0);
  }

  resolve(): void {
    this.cancelPoll();
    this.resolveCategory(this.query().trim());
  }

  // Resolve a specific category — shared by the Check button (the typed input) and the header refresh (the
  // last-resolved category). Kept separate from the `query` signal so a refresh never overwrites the input.
  private resolveCategory(category: string): void {
    if (!category) return;

    this.resolving.set(true);
    this.resolveError.set(false);
    this.adminApi.getLogLevels(category).subscribe({
      next: (res) => {
        this.resolving.set(false);
        // The same response carries a fresh table — keep it in sync with the probe.
        this.applyTable(res);
        this.resolved.set(res.resolved ?? null);
        this.lastResolved.set(category);
      },
      error: () => {
        this.resolving.set(false);
        this.resolveError.set(true);
      },
    });
  }

  // A quiet table fetch used by the post-save poll: no loading spinner, no error state (the table keeps its
  // last-good data). Re-probes the resolved namespace too when one is shown, so it stays in sync as the
  // override lands. Errors abort the poll silently.
  private pollFetch(onResult: (res: LogLevelsResponse) => void): void {
    const category = this.lastResolved();
    this.adminApi.getLogLevels(category ?? undefined).subscribe({
      next: (res) => {
        this.applyTable(res);
        if (category) this.resolved.set(res.resolved ?? null);
        onResult(res);
      },
      error: () => { /* leave the last-good table; stop polling */ },
    });
  }

  private cancelPoll(): void {
    if (this.pollHandle !== null) {
      clearTimeout(this.pollHandle);
      this.pollHandle = null;
    }
  }

  private applyTable(res: LogLevelsResponse): void {
    this.minLevel.set(res.minLevel);
    this.rules.set(res.rules ?? []);
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  badgeVariant(source: LogLevelSource): BadgeVariant {
    switch (source) {
      case 'Override':
        return 'default';
      case 'AppSettings':
      case 'Environment':
        return 'secondary';
      default: // Code, Default
        return 'outline';
    }
  }

  // The fallback "Default" source is the least emphatic; mute it to set it apart from a real code rule.
  badgeClass(source: LogLevelSource): string {
    return source === 'Default' ? '!text-muted-foreground' : '';
  }
}

// Normalize a saved override map to a case-insensitive category→level map (config keys are case-insensitive).
function normalizeOverrides(overrides: Record<string, LogLevel>): Map<string, LogLevel> {
  const out = new Map<string, LogLevel>();
  for (const [category, level] of Object.entries(overrides)) {
    out.set(category.toLowerCase(), level);
  }
  return out;
}

// The table has converged on a save once its Override-source rows exactly match the saved map. A PATCH fully
// replaces the override set, so a removed category must also have dropped its Override row — hence the exact
// (size + per-entry) match rather than a subset check.
function overridesConverged(rules: LogLevelRule[], expected: Map<string, LogLevel>): boolean {
  const actual = new Map<string, LogLevel>();
  for (const rule of rules) {
    if (rule.source === 'Override') actual.set(rule.category.toLowerCase(), rule.level);
  }
  if (actual.size !== expected.size) return false;
  for (const [category, level] of expected) {
    if (actual.get(category) !== level) return false;
  }
  return true;
}
