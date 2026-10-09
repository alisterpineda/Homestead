import { Component, ChangeDetectionStrategy, computed, inject, signal, OnInit } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSparkles, lucideCircleCheck, lucideCircleX, lucideTriangleAlert } from '@ng-icons/lucide';
import { HlmCardImports } from '@/shared/components/spartan/card';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { AdminApiService } from '@/api/services/admin-api.service';
import type { AiProviderStatusResponse } from '@/api/generated/models/ai-provider-status-response';
import type { AiProviderType } from '@/api/generated/models/ai-provider-type';
import type { TestAiConnectionResponse } from '@/api/generated/models/test-ai-connection-response';

@Component({
  selector: 'app-ai-status',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon, HlmCardImports, HlmButton, HlmSpinner, HlmBadge, HlmEmptyImports,
  ],
  providers: [provideIcons({ lucideSparkles, lucideCircleCheck, lucideCircleX, lucideTriangleAlert })],
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
              <ng-icon name="lucideSparkles" />
            </div>
            <div hlmEmptyTitle>Failed to load AI providers</div>
            <div hlmEmptyDescription>There was an error reading the AI configuration. Please try again.</div>
          </div>
          <div hlmEmptyContent>
            <button hlmBtn variant="outline" (click)="load()">Retry</button>
          </div>
        </div>
      } @else if (providers(); as list) {
        @if (list.length === 0) {
          <div hlmEmpty>
            <div hlmEmptyHeader>
              <div hlmEmptyMedia variant="icon">
                <ng-icon name="lucideSparkles" />
              </div>
              <div hlmEmptyTitle>No AI providers configured</div>
              <div hlmEmptyDescription>
                AI-assisted features are disabled. Define a provider with
                <span class="font-mono">Ai:Providers:&#123;name&#125;:Type</span>,
                <span class="font-mono">:Endpoint</span> and <span class="font-mono">:Model</span>
                (plus <span class="font-mono">:ApiKey</span> where required), and pick one with
                <span class="font-mono">Ai:DefaultProvider</span> — in AppHost user-secrets, or as
                <span class="font-mono">Ai__…</span> environment variables in Docker. See the project README.
              </div>
            </div>
          </div>
        } @else {
          <div hlmCard>
            <div hlmCardHeader>
              <h2 hlmCardTitle>AI Providers</h2>
              <p hlmCardDescription>
                Providers defined under <span class="font-mono">Ai:Providers</span>. The default is what
                classification and statement parsing use.
              </p>
            </div>

            <div hlmCardContent class="flex flex-col">
              @if (noDefault()) {
                <p class="mb-4 flex items-start gap-2 rounded-md border border-destructive/50 p-3 text-sm text-destructive">
                  <ng-icon name="lucideTriangleAlert" class="text-[length:--spacing(4)] mt-0.5 shrink-0" />
                  <span>
                    No default provider is selected, so AI features are <strong>disabled</strong> even though
                    providers are configured — with more than one provider,
                    <span class="font-mono">Ai:DefaultProvider</span> must name the one to use.
                  </span>
                </p>
              }
              @for (p of list; track p.key) {
                <div class="flex flex-col gap-2 border-b border-border/50 py-4 first:pt-0 last:border-b-0 last:pb-0">
                  <div class="flex items-center gap-2">
                    <span class="font-mono text-sm font-medium">{{ p.key }}</span>
                    <span class="text-xs text-muted-foreground">{{ typeLabel(p.type) }}</span>
                    @if (p.isDefault) {
                      <span hlmBadge variant="secondary">Default</span>
                    }
                    <span hlmBadge [variant]="p.isConfigured ? 'default' : 'outline'"
                      [class]="p.isConfigured ? '' : '!text-muted-foreground'">
                      {{ p.isConfigured ? 'Configured' : 'Not configured' }}
                    </span>
                  </div>

                  @if (p.isConfigured) {
                    <div class="flex flex-col">
                      <div class="flex items-center gap-3 py-1">
                        <span class="w-32 shrink-0 text-sm text-muted-foreground">Endpoint</span>
                        <span class="min-w-0 flex-1 truncate font-mono text-sm" [title]="p.endpoint ?? ''">{{ p.endpoint }}</span>
                      </div>
                      <div class="flex items-center gap-3 py-1">
                        <span class="w-32 shrink-0 text-sm text-muted-foreground">Model</span>
                        <span class="min-w-0 flex-1 truncate font-mono text-sm" [title]="p.model ?? ''">{{ p.model }}</span>
                      </div>
                      <div class="flex items-center gap-3 py-1">
                        <span class="w-32 shrink-0 text-sm text-muted-foreground">API key</span>
                        <span class="flex-1 text-sm">{{ p.hasApiKey ? 'Set' : 'Not set' }}</span>
                      </div>
                      <div class="flex items-center gap-3 py-1">
                        <span class="w-32 shrink-0 text-sm text-muted-foreground">Max input</span>
                        <span class="flex-1 font-mono text-sm">{{ p.maxInputChars }} chars</span>
                      </div>
                    </div>
                  } @else {
                    <p class="text-sm text-destructive">{{ p.configurationError ?? 'Unavailable.' }}</p>
                  }

                  <div class="flex flex-col items-start gap-2">
                    <button hlmBtn variant="outline" (click)="test(p.key)"
                      [disabled]="testingKey() !== null || !p.isConfigured">
                      @if (testingKey() === p.key) {
                        <hlm-spinner class="mr-2 size-4" />
                        Testing...
                      } @else {
                        Test connection
                      }
                    </button>

                    @if (testResults()[p.key]; as r) {
                      @if (r.success) {
                        <p class="flex items-center gap-2 text-sm text-muted-foreground">
                          <ng-icon name="lucideCircleCheck" class="text-[length:--spacing(4)] text-primary" />
                          <span>{{ r.message }}{{ r.latencyMs != null ? ' (' + r.latencyMs + ' ms)' : '' }}</span>
                        </p>
                      } @else {
                        <p class="flex items-center gap-2 text-sm text-destructive">
                          <ng-icon name="lucideCircleX" class="text-[length:--spacing(4)]" />
                          <span>{{ r.message }}</span>
                        </p>
                      }
                    }
                  </div>
                </div>
              }
            </div>
          </div>
        }
      }
    </div>
  `,
})
export class AiStatusComponent implements OnInit {
  private adminApi = inject(AdminApiService);

  loading = signal(true);
  error = signal(false);
  providers = signal<AiProviderStatusResponse[] | null>(null);
  testingKey = signal<string | null>(null);
  testResults = signal<Record<string, TestAiConnectionResponse>>({});

  // Providers exist but none is the default: the resolution chain yields nothing, so AI is silently off —
  // the one misconfiguration the per-row badges can't show (every row reads healthy).
  noDefault = computed(() => {
    const list = this.providers();
    return !!list && list.length > 1 && !list.some(p => p.isDefault);
  });

  ngOnInit(): void {
    this.load();
  }

  typeLabel(type: AiProviderType | null): string {
    switch (type) {
      case 'OpenAiCompatible': return 'OpenAI-compatible';
      case 'AzureFoundry': return 'Azure AI Foundry';
      default: return 'Unknown type';
    }
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.testResults.set({});
    this.adminApi.getAiStatus().subscribe({
      next: (res) => {
        this.loading.set(false);
        this.providers.set(res);
      },
      error: () => {
        this.loading.set(false);
        this.error.set(true);
      },
    });
  }

  test(providerKey: string): void {
    this.testingKey.set(providerKey);
    // Clear this provider's previous result first: a stale "Connection successful" must not stay rendered
    // through the new probe — or survive it, if the request itself errors below.
    this.testResults.update(({ [providerKey]: _, ...rest }) => rest);
    // The endpoint always returns 200 — a failed probe lives in the body (success=false). The error branch
    // here only fires on 5xx/network errors, which the error interceptor already auto-toasts; just reset.
    this.adminApi.testAiConnection(providerKey).subscribe({
      next: (res) => {
        this.testingKey.set(null);
        this.testResults.update(results => ({ ...results, [providerKey]: res }));
      },
      error: () => {
        this.testingKey.set(null);
      },
    });
  }
}
