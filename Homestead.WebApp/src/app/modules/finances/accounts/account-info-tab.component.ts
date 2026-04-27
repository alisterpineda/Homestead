import { Component, ChangeDetectionStrategy, input, computed } from '@angular/core';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';
import { KIND_LABELS, isUserAccountKind, type UserAccountKind } from '../shared/account-kind-meta';

@Component({
  selector: 'app-account-info-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (account(); as a) {
      <dl class="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div>
          <dt class="text-xs uppercase tracking-wide text-muted-foreground">Kind</dt>
          <dd class="text-sm">{{ kindLabel() }}</dd>
        </div>
        <div>
          <dt class="text-xs uppercase tracking-wide text-muted-foreground">Reporting currency</dt>
          <dd class="text-sm">{{ currencyLabel() }}</dd>
        </div>
        <div>
          <dt class="text-xs uppercase tracking-wide text-muted-foreground">Institution</dt>
          <dd class="text-sm">{{ a.institution || '—' }}</dd>
        </div>
        <div>
          <dt class="text-xs uppercase tracking-wide text-muted-foreground">Status</dt>
          <dd class="text-sm">{{ a.isArchived ? 'Archived' : 'Active' }}</dd>
        </div>
        <div>
          <dt class="text-xs uppercase tracking-wide text-muted-foreground">Created</dt>
          <dd class="text-sm">{{ formatDate(a.createdAt) }}</dd>
        </div>
        <div>
          <dt class="text-xs uppercase tracking-wide text-muted-foreground">Last updated</dt>
          <dd class="text-sm">{{ formatDate(a.updatedAt) }}</dd>
        </div>
      </dl>
    }
  `,
})
export class AccountInfoTabComponent {
  account = input<AccountResponse | null>(null);
  reportingCurrency = input<CommodityResponse | null>(null);

  kindLabel = computed(() => {
    const k = this.account()?.kind ?? null;
    return isUserAccountKind(k) ? KIND_LABELS[k as UserAccountKind] : '—';
  });

  currencyLabel = computed(() => {
    const c = this.reportingCurrency();
    return c ? `${c.symbol} — ${c.name}` : '—';
  });

  formatDate(dateStr: string): string {
    if (!dateStr) return '—';
    return new Date(dateStr).toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  }
}
