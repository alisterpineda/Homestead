import { Component, ChangeDetectionStrategy, inject, signal, OnInit } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSparkles, lucideCircleCheck, lucideCircleX } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmCardImports } from '@/shared/components/spartan/card';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSpinner } from '@/shared/components/spartan/spinner';
import { HlmBadge } from '@/shared/components/spartan/badge';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { AdminApiService } from '@/api/services/admin-api.service';
import type { AiStatusResponse } from '@/api/generated/models/ai-status-response';
import type { TestAiConnectionResponse } from '@/api/generated/models/test-ai-connection-response';

@Component({
  selector: 'app-ai-status',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon, HlmIconImports, HlmCardImports, HlmButton, HlmSpinner, HlmBadge, HlmEmptyImports,
  ],
  providers: [provideIcons({ lucideSparkles, lucideCircleCheck, lucideCircleX })],
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
            <div hlmEmptyTitle>Failed to load AI status</div>
            <div hlmEmptyDescription>There was an error reading the AI configuration. Please try again.</div>
          </div>
          <div hlmEmptyContent>
            <button hlmBtn variant="outline" (click)="load()">Retry</button>
          </div>
        </div>
      } @else if (status(); as s) {
        <div hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>AI</h2>
            <p hlmCardDescription>Status of the configured AI integration.</p>
            <span hlmCardAction hlmBadge [variant]="s.configured ? 'default' : 'outline'"
              [class]="s.configured ? '' : '!text-muted-foreground'">
              {{ s.configured ? 'Configured' : 'Not configured' }}
            </span>
          </div>

          <div hlmCardContent class="flex flex-col gap-4">
            @if (s.configured) {
              <div class="flex flex-col">
                <div class="flex items-center gap-3 border-b border-border/50 py-2">
                  <span class="w-32 text-sm text-muted-foreground">Endpoint</span>
                  <span class="flex-1 truncate font-mono text-sm" [title]="s.endpoint ?? ''">{{ s.endpoint }}</span>
                </div>
                <div class="flex items-center gap-3 border-b border-border/50 py-2">
                  <span class="w-32 text-sm text-muted-foreground">Model</span>
                  <span class="flex-1 truncate font-mono text-sm" [title]="s.model ?? ''">{{ s.model }}</span>
                </div>
                <div class="flex items-center gap-3 border-b border-border/50 py-2">
                  <span class="w-32 text-sm text-muted-foreground">API key</span>
                  <span class="flex-1 text-sm">{{ s.hasApiKey ? 'Set' : 'Not set' }}</span>
                </div>
                <div class="flex items-center gap-3 border-b border-border/50 py-2">
                  <span class="w-32 text-sm text-muted-foreground">Max input</span>
                  <span class="flex-1 font-mono text-sm">{{ s.maxInputChars }} chars</span>
                </div>
              </div>
            } @else {
              <p class="text-sm text-muted-foreground">
                AI is not configured, so AI-assisted features are disabled. To enable it, set
                <span class="font-mono">Ai:Endpoint</span> and <span class="font-mono">Ai:Model</span>
                (and optionally <span class="font-mono">Ai:ApiKey</span>) in the AppHost user-secrets — see the
                project README for the full workflow.
              </p>
            }
          </div>

          <div hlmCardFooter class="flex-col items-start gap-3">
            <button hlmBtn variant="outline" (click)="test()" [disabled]="testing() || !s.configured">
              @if (testing()) {
                <hlm-spinner class="mr-2 size-4" />
                Testing...
              } @else {
                Test connection
              }
            </button>

            @if (testResult(); as r) {
              @if (r.success) {
                <p class="flex items-center gap-2 text-sm text-muted-foreground">
                  <ng-icon hlm name="lucideCircleCheck" size="sm" class="text-primary" />
                  <span>{{ r.message }}{{ r.latencyMs != null ? ' (' + r.latencyMs + ' ms)' : '' }}</span>
                </p>
              } @else {
                <p class="flex items-center gap-2 text-sm text-destructive">
                  <ng-icon hlm name="lucideCircleX" size="sm" />
                  <span>{{ r.message }}</span>
                </p>
              }
            }
          </div>
        </div>
      }
    </div>
  `,
})
export class AiStatusComponent implements OnInit {
  private adminApi = inject(AdminApiService);

  loading = signal(true);
  error = signal(false);
  status = signal<AiStatusResponse | null>(null);
  testing = signal(false);
  testResult = signal<TestAiConnectionResponse | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.testResult.set(null);
    this.adminApi.getAiStatus().subscribe({
      next: (res) => {
        this.loading.set(false);
        this.status.set(res);
      },
      error: () => {
        this.loading.set(false);
        this.error.set(true);
      },
    });
  }

  test(): void {
    this.testing.set(true);
    this.testResult.set(null);
    // The endpoint always returns 200 — a failed probe lives in the body (success=false). The error branch
    // here only fires on 5xx/network errors, which the error interceptor already auto-toasts; just reset.
    this.adminApi.testAiConnection().subscribe({
      next: (res) => {
        this.testing.set(false);
        this.testResult.set(res);
      },
      error: () => {
        this.testing.set(false);
      },
    });
  }
}
