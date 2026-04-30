import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  viewChild,
  TemplateRef,
  afterNextRender,
  OnDestroy,
} from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { HlmCheckbox } from '@/shared/components/spartan/checkbox';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmBreadCrumbImports } from '@/shared/components/spartan/breadcrumb';
import { FinancesPriceSourcesApiService } from '@/api/services/finances-price-sources-api.service';
import { ErrorHandlerService } from '@/shared/services/error-handler.service';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { handleMutationError, handleQueryError } from '@/shared/utils/rxjs-error.utils';
import type { GetPriceSourceResponse } from '@/api/generated/models/get-price-source-response';
import type { CommodityPriceSource } from '@/api/generated/models/commodity-price-source';
import { PriceSourceConfigFormComponent } from './price-source-config-form.component';

@Component({
  selector: 'app-price-source-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule, DatePipe, HlmSpinner,
    HlmButton, HlmInput, HlmLabel, HlmBadge, HlmCheckbox,
    HlmBreadCrumbImports,
    PriceSourceConfigFormComponent,
  ],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <ng-template #headerTitle>
      <nav hlmBreadcrumb class="min-w-0">
        <ol hlmBreadcrumbList class="flex-nowrap overflow-hidden">
          <li hlmBreadcrumbItem><span>Settings</span></li>
          <li hlmBreadcrumbSeparator></li>
          <li hlmBreadcrumbItem>
            <a hlmBreadcrumbLink link="/finances/settings/price-sources">Price sources</a>
          </li>
          <li hlmBreadcrumbSeparator></li>
          <li hlmBreadcrumbItem class="min-w-0">
            <span hlmBreadcrumbPage class="truncate">{{ leafLabel() }}</span>
          </li>
        </ol>
      </nav>
    </ng-template>

    <div class="flex min-h-0 flex-1 flex-col gap-4 max-w-3xl">
      @if (loading()) {
        <div class="flex justify-center py-16"><hlm-spinner /></div>
      } @else if (error()) {
        <div class="rounded-lg border border-destructive bg-card p-4 text-sm text-destructive">
          Failed to load price source.
        </div>
      } @else if (data(); as d) {
        <section class="rounded-lg border border-border bg-card p-4 text-card-foreground space-y-3">
          <div class="flex items-start justify-between gap-2">
            <div>
              <h2 class="text-xl font-semibold">{{ d.displayName }}</h2>
              <p class="text-sm text-muted-foreground">Source: <span class="font-mono">{{ d.source }}</span></p>
            </div>
            <span hlmBadge variant="secondary">{{ d.kind }}</span>
          </div>

          @if (d.kind === 'External') {
            <div class="flex items-center gap-2">
              <hlm-checkbox
                id="enabled"
                [checked]="isEnabled()"
                (checkedChange)="setEnabled($event)"
              />
              <label hlmLabel for="enabled">Enabled</label>
              <button hlmBtn size="sm" class="ml-auto" (click)="saveEnabled()" [disabled]="!enabledDirty() || saving()">
                {{ saving() ? 'Saving...' : 'Save' }}
              </button>
            </div>
            <div class="text-sm grid grid-cols-2 gap-2 pt-2 border-t border-border">
              <div>
                <div class="text-xs uppercase tracking-wide text-muted-foreground">Last import</div>
                <div>
                  @if (d.lastImportAt) {
                    {{ d.lastImportAt | date:'medium' }}
                  } @else {
                    <span class="text-muted-foreground">Never run</span>
                  }
                </div>
              </div>
              <div>
                <div class="text-xs uppercase tracking-wide text-muted-foreground">Status</div>
                <div>
                  @if (!d.lastImportAt) {
                    <span hlmBadge variant="outline">Never run</span>
                  } @else if (d.lastImportStatus === 'Failed') {
                    <span hlmBadge variant="destructive">Failed</span>
                  } @else if (d.lastImportStatus === 'PartialSuccess') {
                    <span hlmBadge variant="secondary">Partial</span>
                  } @else {
                    <span hlmBadge variant="default">Success</span>
                  }
                </div>
              </div>
              @if (d.lastImportMessage) {
                <div class="col-span-2">
                  <div class="text-xs uppercase tracking-wide text-muted-foreground">Last message</div>
                  <pre class="whitespace-pre-wrap break-words text-xs bg-muted text-muted-foreground p-2 rounded">{{ d.lastImportMessage }}</pre>
                </div>
              }
            </div>
          } @else {
            <p class="text-sm text-muted-foreground">No settings — this source is system-managed.</p>
          }
        </section>

        @if (d.kind === 'External' && d.configSchema.fields.length > 0) {
          <section class="rounded-lg border border-border bg-card p-4 text-card-foreground space-y-3">
            <h3 class="text-base font-semibold">Configuration</h3>
            <app-price-source-config-form
              [schema]="d.configSchema"
              [initialJson]="d.config ?? null"
              [saving]="savingConfig()"
              (saved)="saveConfig($event)"
            />
          </section>
        }

        @if (d.kind === 'External') {
          <section class="rounded-lg border border-border bg-card p-4 text-card-foreground space-y-3">
            <h3 class="text-base font-semibold">Schedule</h3>
            <div class="grid grid-cols-1 gap-2">
              <label hlmLabel for="cron">Cron expression</label>
              <input hlmInput id="cron" [(ngModel)]="cronExpression" placeholder="0 6 * * *" />
              <p class="text-xs text-muted-foreground">UTC. Example: <code>0 6 * * *</code> runs daily at 06:00 UTC.</p>
              <div class="flex items-center gap-2">
                <hlm-checkbox id="schedule-enabled" [checked]="scheduleEnabled()" (checkedChange)="scheduleEnabled.set($event)" />
                <label hlmLabel for="schedule-enabled">Enabled</label>
              </div>
              @if (d.schedule; as s) {
                <p class="text-xs text-muted-foreground">Next run: {{ s.nextRunAt | date:'medium' }}</p>
              }
              <div class="flex items-center gap-2">
                <button hlmBtn size="sm" (click)="saveSchedule()" [disabled]="!cronExpression() || savingSchedule()">
                  {{ savingSchedule() ? 'Saving...' : 'Save schedule' }}
                </button>
                @if (d.schedule) {
                  <button hlmBtn variant="destructive" size="sm" (click)="deleteSchedule()" [disabled]="savingSchedule()">
                    Delete schedule
                  </button>
                }
              </div>
            </div>
          </section>

          <section class="rounded-lg border border-border bg-card p-4 text-card-foreground space-y-3">
            <h3 class="text-base font-semibold">Run now</h3>
            <p class="text-sm text-muted-foreground">
              Manually trigger an import. Optional date overrides apply only to this run.
            </p>
            <div class="grid grid-cols-2 gap-2 max-w-md">
              <div>
                <label hlmLabel for="from">From (optional)</label>
                <input hlmInput id="from" type="datetime-local" [(ngModel)]="runFromAt" />
              </div>
              <div>
                <label hlmLabel for="to">To (optional)</label>
                <input hlmInput id="to" type="datetime-local" [(ngModel)]="runToAt" />
              </div>
            </div>
            <div>
              <button hlmBtn (click)="runNow()" [disabled]="!d.isEnabled || running()">
                {{ running() ? 'Enqueuing...' : 'Run now' }}
              </button>
              @if (!d.isEnabled) {
                <p class="text-xs text-destructive mt-1">Enable the source before running an import.</p>
              }
            </div>
          </section>
        }
      }
    </div>
  `,
})
export class PriceSourceDetailComponent implements OnDestroy {
  private route = inject(ActivatedRoute);
  private api = inject(FinancesPriceSourcesApiService);
  private errorHandler = inject(ErrorHandlerService);
  private pageHeader = inject(PageHeaderService);

  source = signal<CommodityPriceSource>('BankOfCanada');
  loading = signal(true);
  error = signal(false);
  data = signal<GetPriceSourceResponse | null>(null);

  isEnabled = signal(false);
  saving = signal(false);
  savingConfig = signal(false);

  cronExpression = signal('');
  scheduleEnabled = signal(true);
  savingSchedule = signal(false);

  runFromAt = signal<string | null>(null);
  runToAt = signal<string | null>(null);
  running = signal(false);

  enabledDirty = computed(() => this.isEnabled() !== (this.data()?.isEnabled ?? false));

  protected leafLabel = computed(() => this.data()?.displayName ?? 'Price source');

  private headerTitleTpl = viewChild.required<TemplateRef<unknown>>('headerTitle');

  constructor() {
    afterNextRender(() => this.pageHeader.setTitle(this.headerTitleTpl()));
    const sourceParam = this.route.snapshot.paramMap.get('source') as CommodityPriceSource | null;
    if (sourceParam) this.source.set(sourceParam);
    this.refresh();
  }

  ngOnDestroy(): void {
    this.pageHeader.clearTitle();
  }

  private refresh(): void {
    this.loading.set(true);
    this.api.get(this.source())
      .pipe(handleQueryError(this.loading, this.error, null))
      .subscribe(r => {
        this.loading.set(false);
        if (r) {
          this.data.set(r);
          this.isEnabled.set(r.isEnabled);
          if (r.schedule) {
            this.cronExpression.set(r.schedule.cronExpression);
            this.scheduleEnabled.set(r.schedule.isEnabled);
          } else {
            this.cronExpression.set('');
            this.scheduleEnabled.set(true);
          }
        }
      });
  }

  setEnabled(value: boolean): void {
    this.isEnabled.set(value);
  }

  saveEnabled(): void {
    this.saving.set(true);
    this.api.patch(this.source(), { isEnabled: this.isEnabled() })
      .pipe(handleMutationError(this.errorHandler, this.saving, 'Failed to save.'))
      .subscribe(() => {
        this.saving.set(false);
        toast.success('Saved.');
        this.refresh();
      });
  }

  saveConfig(json: string): void {
    this.savingConfig.set(true);
    this.api.patch(this.source(), { config: json })
      .pipe(handleMutationError(this.errorHandler, this.savingConfig, 'Failed to save config.'))
      .subscribe(() => {
        this.savingConfig.set(false);
        toast.success('Config saved.');
        this.refresh();
      });
  }

  saveSchedule(): void {
    this.savingSchedule.set(true);
    this.api.upsertSchedule(this.source(), {
      cronExpression: this.cronExpression(),
      isEnabled: this.scheduleEnabled(),
      priority: 0,
    })
      .pipe(handleMutationError(this.errorHandler, this.savingSchedule, 'Failed to save schedule.'))
      .subscribe(() => {
        this.savingSchedule.set(false);
        toast.success('Schedule saved.');
        this.refresh();
      });
  }

  deleteSchedule(): void {
    this.savingSchedule.set(true);
    this.api.deleteSchedule(this.source())
      .pipe(handleMutationError(this.errorHandler, this.savingSchedule, 'Failed to delete schedule.'))
      .subscribe(() => {
        this.savingSchedule.set(false);
        toast.success('Schedule deleted.');
        this.refresh();
      });
  }

  runNow(): void {
    this.running.set(true);
    const fromAt = this.runFromAt();
    const toAt = this.runToAt();
    const body = (fromAt || toAt) ? {
      fromAt: fromAt ? new Date(fromAt).toISOString() : undefined,
      toAt: toAt ? new Date(toAt).toISOString() : undefined,
    } : undefined;
    this.api.runNow(this.source(), body)
      .pipe(handleMutationError(this.errorHandler, this.running, 'Failed to enqueue job.'))
      .subscribe(() => {
        this.running.set(false);
        toast.success('Job enqueued — see Jobs page for progress.');
        this.refresh();
      });
  }
}
