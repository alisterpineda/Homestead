import { Component, ChangeDetectionStrategy, input } from '@angular/core';
import { HlmBadgeImports } from '@/shared/components/spartan/badge';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

@Component({
  selector: 'app-commodity-info-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmBadgeImports],
  template: `
    @if (commodity(); as c) {
      <dl class="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div>
          <dt class="text-xs uppercase tracking-wide text-muted-foreground">Symbol</dt>
          <dd class="mt-1 font-medium">{{ c.symbol }}</dd>
        </div>
        @if (c.exchange) {
          <div>
            <dt class="text-xs uppercase tracking-wide text-muted-foreground">Exchange</dt>
            <dd class="mt-1"><span hlmBadge variant="outline">{{ c.exchange }}</span></dd>
          </div>
        }
        <div>
          <dt class="text-xs uppercase tracking-wide text-muted-foreground">Name</dt>
          <dd class="mt-1">{{ c.name }}</dd>
        </div>
        <div>
          <dt class="text-xs uppercase tracking-wide text-muted-foreground">Kind</dt>
          <dd class="mt-1">{{ c.kind }}</dd>
        </div>
        <div>
          <dt class="text-xs uppercase tracking-wide text-muted-foreground">Display precision</dt>
          <dd class="mt-1 tabular-nums">{{ c.displayPrecision }}</dd>
        </div>
        <div>
          <dt class="text-xs uppercase tracking-wide text-muted-foreground">Source</dt>
          <dd class="mt-1">
            @if (c.isSystemManaged) {
              <span hlmBadge variant="secondary">System managed</span>
            } @else {
              <span class="text-muted-foreground">User created</span>
            }
          </dd>
        </div>
      </dl>
    }
  `,
})
export class CommodityInfoTabComponent {
  commodity = input.required<CommodityResponse>();
}
