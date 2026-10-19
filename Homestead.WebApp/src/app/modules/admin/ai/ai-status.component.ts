import { Component, ChangeDetectionStrategy, inject, signal, OnInit } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSparkles, lucideCircleCheck, lucideCircleX } from '@ng-icons/lucide';
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
    NgIcon, HlmCardImports, HlmButton, HlmSpinner, HlmBadge, HlmEmptyImports,
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
        @if (!s.isPresent) {
          <div hlmEmpty>
            <div hlmEmptyHeader>
              <div hlmEmptyMedia variant="icon">
                <ng-icon name="lucideSparkles" />
              </div>
              <div hlmEmptyTitle>AI is not configured</div>
              <div hlmEmptyDescription>
                AI-assisted features are disabled. Set <span class="font-mono">Ai:ApiKey</span> and
                <span class="font-mono">Ai:Model</span> (AppHost user-secrets, or
                <span class="font-mono">Ai__ApiKey</span> / <span class="font-mono">Ai__Model</span> in Docker).
                See the project README.
              </div>
            </div>
          </div>
        } @else {
          <div hlmCard>
            <div hlmCardHeader>
              <div class="flex items-center gap-2">
                <h2 hlmCardTitle>Claude API</h2>
                <span hlmBadge [variant]="s.isConfigured ? 'default' : 'outline'"
                  [class]="s.isConfigured ? '' : '!text-muted-foreground'">
                  {{ s.isConfigured ? 'Configured' : 'Unavailable' }}
                </span>
              </div>
              <p hlmCardDescription>
                The model behind document classification, summaries, statement parsing and payee suggestions.
              </p>
            </div>

            <div hlmCardContent class="flex flex-col gap-4">
              @if (!s.isConfigured) {
                <p class="text-sm text-destructive">{{ s.configurationError ?? 'Unavailable.' }}</p>
              }

              <div class="flex flex-col">
                <div class="flex items-center gap-3 py-1">
                  <span class="w-36 shrink-0 text-sm text-muted-foreground">Model</span>
                  @if (s.model) {
                    <span class="min-w-0 flex-1 truncate font-mono text-sm" [title]="s.model">{{ s.model }}</span>
                  } @else {
                    <span class="flex-1 text-sm text-muted-foreground">Not set</span>
                  }
                </div>
                <div class="flex items-center gap-3 py-1">
                  <span class="w-36 shrink-0 text-sm text-muted-foreground">Endpoint</span>
                  @if (s.endpoint) {
                    <span class="min-w-0 flex-1 truncate font-mono text-sm" [title]="s.endpoint">{{ s.endpoint }}</span>
                  } @else {
                    <span class="flex-1 text-sm text-muted-foreground">Claude API default</span>
                  }
                </div>
                <div class="flex items-center gap-3 py-1">
                  <span class="w-36 shrink-0 text-sm text-muted-foreground">API key</span>
                  <span class="flex-1 text-sm">{{ s.hasApiKey ? 'Set' : 'Not set' }}</span>
                </div>
                <div class="flex items-center gap-3 py-1">
                  <span class="w-36 shrink-0 text-sm text-muted-foreground">Effort</span>
                  <span class="flex-1 font-mono text-sm">{{ s.effort }}</span>
                </div>
                <div class="flex items-center gap-3 py-1">
                  <span class="w-36 shrink-0 text-sm text-muted-foreground">Refusal fallback</span>
                  <!-- On: a request the model declines is re-run on a fallback model inside the same call. -->
                  <span class="flex-1 text-sm">{{ s.refusalFallback ? 'On' : 'Off' }}</span>
                </div>
                <div class="flex items-center gap-3 py-1">
                  <span class="w-36 shrink-0 text-sm text-muted-foreground">Max input</span>
                  <span class="flex-1 font-mono text-sm">{{ s.maxInputChars }} chars</span>
                </div>
                <div class="flex items-center gap-3 py-1">
                  <span class="w-36 shrink-0 text-sm text-muted-foreground">Statement input</span>
                  <span class="flex-1 font-mono text-sm">{{ s.statementMaxInputChars }} chars</span>
                </div>
                <div class="flex items-center gap-3 py-1">
                  <span class="w-36 shrink-0 text-sm text-muted-foreground">PDF input</span>
                  <!-- On: classification sends a qualifying PDF itself instead of the OCR text and page image —
                       uploaded through the Files API and deleted after the call, or inline in the request. -->
                  <span class="flex-1 text-sm">
                    {{ s.pdfInput ? 'On (up to ' + s.pdfInputMaxPages + ' pages, ' + pdfMegabytes(s.pdfInputMaxBytes) + ' MB, ' + (s.pdfUpload ? 'uploaded' : 'inline') + ')' : 'Off' }}
                  </span>
                </div>
                <div class="flex items-center gap-3 py-1">
                  <span class="w-36 shrink-0 text-sm text-muted-foreground">Page image</span>
                  <span class="flex-1 text-sm">
                    {{ s.imageInput ? 'On (' + s.imageRenderMaxEdge + ' px)' : 'Off' }}
                  </span>
                </div>
              </div>

              <div class="flex flex-col items-start gap-2">
                <button hlmBtn variant="outline" (click)="test()" [disabled]="testing() || !s.isConfigured">
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
  status = signal<AiStatusResponse | null>(null);
  testing = signal(false);
  testResult = signal<TestAiConnectionResponse | null>(null);

  ngOnInit(): void {
    this.load();
  }

  // The size cap in MiB, trimmed to at most one decimal (10485760 -> "10", 1572864 -> "1.5").
  pdfMegabytes(bytes: number): string {
    return String(Math.round((bytes / (1024 * 1024)) * 10) / 10);
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
    // Clear the previous result first: a stale "Connection successful" must not stay rendered through the new
    // probe — or survive it, if the request itself errors below.
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
