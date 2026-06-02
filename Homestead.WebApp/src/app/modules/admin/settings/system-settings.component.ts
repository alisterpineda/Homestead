import { Component, ChangeDetectionStrategy, inject, signal, computed, viewChild, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus, lucideTrash2, lucideTriangleAlert, lucideSettings } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmCardImports } from '@/shared/components/spartan/card';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmSelectImports } from '@/shared/components/spartan/select';
import { AdminApiService } from '@/api/services/admin-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { toast } from '@spartan-ng/brain/sonner';
import type { LogLevel } from '@/api/generated/models/log-level';
import { EffectiveLogLevelsComponent } from './effective-log-levels.component';

interface LogLevelRow {
  category: string;
  level: LogLevel;
}

const DEFAULT_CATEGORY = 'Default';
const LOG_LEVELS: LogLevel[] = ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical', 'None'];
// Default (the catch-all) is offered as a quick-add too, so it can be re-added once removed.
const WELL_KNOWN_CATEGORIES = [DEFAULT_CATEGORY, 'Microsoft.AspNetCore', 'Microsoft.EntityFrameworkCore', 'Homestead'];

@Component({
  selector: 'app-system-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule, NgIcon, HlmIconImports, HlmCardImports, HlmButton, HlmInput, HlmLabel,
    HlmSpinner, HlmEmptyImports, HlmSelectImports, EffectiveLogLevelsComponent,
  ],
  providers: [provideIcons({ lucidePlus, lucideTrash2, lucideTriangleAlert, lucideSettings })],
  template: `
    <div class="mx-auto w-full max-w-3xl">
      @if (loading()) {
        <div class="flex justify-center py-16">
          <hlm-spinner />
        </div>
      } @else if (error()) {
        <div hlmEmpty>
          <div hlmEmptyHeader>
            <div hlmEmptyMedia variant="icon">
              <ng-icon name="lucideSettings" />
            </div>
            <div hlmEmptyTitle>Failed to load settings</div>
            <div hlmEmptyDescription>There was an error loading system settings. Please try again.</div>
          </div>
          <div hlmEmptyContent>
            <button hlmBtn variant="outline" (click)="load()">Retry</button>
          </div>
        </div>
      } @else {
        <div hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>Logging</h2>
            <p hlmCardDescription>
              Set the backend log verbosity per category. Changes apply immediately, system-wide, and persist
              across restarts.
            </p>
          </div>

          <div hlmCardContent class="flex flex-col gap-4">
            <div class="flex flex-col gap-2">
              @for (row of rows(); track row.category) {
                <div class="flex items-center gap-3">
                  <span class="flex-1 truncate font-mono text-sm" [title]="row.category">{{ row.category }}</span>
                  <hlm-select [ngModel]="row.level" (ngModelChange)="setLevel(row.category, $event)"
                    [itemToString]="levelLabel">
                    <hlm-select-trigger class="w-40">
                      <hlm-select-value />
                    </hlm-select-trigger>
                    <hlm-select-content *hlmSelectPortal>
                      <hlm-select-group>
                        @for (level of levels; track level) {
                          <hlm-select-item [value]="level">{{ level }}</hlm-select-item>
                        }
                      </hlm-select-group>
                    </hlm-select-content>
                  </hlm-select>
                  <button hlmBtn variant="ghost" size="icon" (click)="removeRow(row.category)"
                    [attr.aria-label]="'Remove ' + row.category">
                    <ng-icon hlm name="lucideTrash2" size="sm" />
                  </button>
                </div>
              } @empty {
                <p class="text-sm text-muted-foreground">
                  No overrides set — every category uses the application defaults.
                </p>
              }
            </div>

            @if (availableWellKnown().length > 0) {
              <div class="flex flex-wrap items-center gap-2">
                <span class="text-sm text-muted-foreground">Quick add:</span>
                @for (cat of availableWellKnown(); track cat) {
                  <button hlmBtn variant="outline" size="sm" (click)="addCategory(cat)">
                    <ng-icon hlm name="lucidePlus" size="sm" class="mr-1" />
                    {{ cat }}
                  </button>
                }
              </div>
            }

            <div class="flex flex-col gap-1.5">
              <label hlmLabel for="custom-category">Add custom category</label>
              <div class="flex gap-2">
                <input hlmInput id="custom-category" class="flex-1"
                  placeholder="Namespace, e.g. Homestead.WebApi.Modules.Documents"
                  [value]="newCategory()" (input)="newCategory.set(asInputValue($event))"
                  (keydown.enter)="addCustomCategory()" />
                <button hlmBtn variant="secondary" type="button" (click)="addCustomCategory()"
                  [disabled]="!canAddCustom()">Add</button>
              </div>
            </div>

            <p class="flex items-start gap-2 text-sm text-muted-foreground">
              <ng-icon hlm name="lucideTriangleAlert" size="sm" class="mt-0.5 shrink-0" />
              <span>Trace/Debug can record verbose internal data, including AI prompts and responses.</span>
            </p>
          </div>

          <div hlmCardFooter class="justify-end">
            <button hlmBtn (click)="save()" [disabled]="saving()">
              {{ saving() ? 'Saving...' : 'Save' }}
            </button>
          </div>
        </div>

        <app-effective-log-levels />
      }
    </div>
  `,
})
export class SystemSettingsComponent implements OnInit {
  private adminApi = inject(AdminApiService);
  private errorHandler = inject(ErrorHandlerService);

  private effectiveLogLevels = viewChild(EffectiveLogLevelsComponent);

  readonly levels = LOG_LEVELS;
  levelLabel = (value: string): string => value;

  loading = signal(true);
  error = signal(false);
  saving = signal(false);
  rows = signal<LogLevelRow[]>([]);
  newCategory = signal('');

  availableWellKnown = computed(() => {
    const existing = new Set(this.rows().map(r => r.category.toLowerCase()));
    return WELL_KNOWN_CATEGORIES.filter(c => !existing.has(c.toLowerCase()));
  });

  canAddCustom = computed(() => {
    const name = this.newCategory().trim();
    if (name.length === 0) return false;
    return !this.rows().some(r => r.category.toLowerCase() === name.toLowerCase());
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.adminApi.getSystemSettings().subscribe({
      next: (res) => {
        this.loading.set(false);
        this.seedRows(res.logLevelOverrides ?? null);
      },
      error: () => {
        this.loading.set(false);
        this.error.set(true);
      },
    });
  }

  private seedRows(overrides: { [key: string]: LogLevel } | null): void {
    const entries = overrides ? Object.entries(overrides) : [];
    const isDefault = (category: string) => category.toLowerCase() === DEFAULT_CATEGORY.toLowerCase();

    // Render exactly what's stored — Default is no longer forced. Keep Default (when present) at the top
    // since it's the catch-all; other categories follow in their stored order.
    const rows: LogLevelRow[] = [
      ...entries.filter(([category]) => isDefault(category)),
      ...entries.filter(([category]) => !isDefault(category)),
    ].map(([category, level]) => ({ category, level }));

    this.rows.set(rows);
  }

  asInputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  setLevel(category: string, level: LogLevel): void {
    this.rows.update(rows => rows.map(r => r.category === category ? { ...r, level } : r));
  }

  addCategory(category: string): void {
    const name = category.trim();
    if (name.length === 0) return;
    if (this.rows().some(r => r.category.toLowerCase() === name.toLowerCase())) return;
    this.rows.update(rows => [...rows, { category: name, level: 'Information' }]);
  }

  addCustomCategory(): void {
    if (!this.canAddCustom()) return;
    this.addCategory(this.newCategory());
    this.newCategory.set('');
  }

  removeRow(category: string): void {
    this.rows.update(rows => rows.filter(r => r.category !== category));
  }

  save(): void {
    const logLevelOverrides: { [key: string]: LogLevel } = {};
    for (const row of this.rows()) {
      logLevelOverrides[row.category] = row.level;
    }

    this.saving.set(true);
    this.adminApi.patchSystemSettings({ body: { logLevelOverrides } }).subscribe({
      next: (res) => {
        this.saving.set(false);
        const savedOverrides = res.logLevelOverrides ?? {};
        this.seedRows(savedOverrides);
        toast.success('Logging settings saved.');
        // Overrides apply to this instance asynchronously (Postgres NOTIFY self-echo). Rather than guess a
        // delay, let the effective view poll until its live table reflects exactly what we just saved.
        this.effectiveLogLevels()?.refreshAfterSave(savedOverrides);
      },
      error: (err: unknown) => {
        this.saving.set(false);
        if (!this.errorHandler.isServerOrNetworkError(err)) {
          this.errorHandler.handleError(err, 'Failed to save logging settings.');
        }
      },
    });
  }
}
