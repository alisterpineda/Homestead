import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  LOCALE_ID,
} from '@angular/core';
import { formatDate } from '@angular/common';
import { RouterLink } from '@angular/router';
import { HlmCardImports } from '@/shared/components/spartan/card';
import { AccountRowActionsComponent } from './account-row-actions.component';
import { accountKindLabel } from '../shared/account-kind-meta';
import { formatAccountBalance } from '../shared/sign';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

@Component({
  selector: 'app-account-card-grid',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    HlmCardImports,
    AccountRowActionsComponent,
  ],
  template: `
    <div class="flex flex-col gap-3">
      @for (account of accounts(); track account.id) {
        <a
          hlmCard
          [routerLink]="['/finances/accounts', account.id]"
          class="group relative flex flex-row items-center gap-4 px-4 py-3 transition-shadow hover:shadow-md focus-visible:ring-2 focus-visible:ring-ring"
          [class.italic]="account.isArchived"
          [class.text-muted-foreground]="account.isArchived"
        >
          <div class="min-w-0 flex-1">
            <div class="truncate text-base font-semibold">{{ account.name }}</div>
            <div class="truncate text-xs text-muted-foreground">{{ subtitle(account) }}</div>
          </div>

          <div class="flex flex-col items-end shrink-0">
            @if (balanceText(account); as text) {
              <div class="tabular-nums text-base font-medium">{{ text }}</div>
            } @else if ((account.unpricedHoldingCount ?? 0) > 0) {
              <div class="text-sm font-medium text-amber-600 dark:text-amber-500">Unpriced</div>
            } @else {
              <div class="text-base font-medium text-muted-foreground">—</div>
            }
            <div class="text-xs text-muted-foreground">{{ latestTxText(account) }}</div>
          </div>

          <app-account-row-actions
            class="shrink-0"
            [account]="account"
            (edit)="edit.emit($event)"
            (archiveToggle)="archiveToggle.emit($event)"
            (delete)="delete.emit($event)"
          />
        </a>
      }
    </div>
  `,
})
export class AccountCardGridComponent {
  private readonly locale = inject(LOCALE_ID);

  accounts = input.required<AccountResponse[]>();
  displayCurrency = input<CommodityResponse | null>(null);

  edit = output<AccountResponse>();
  archiveToggle = output<AccountResponse>();
  delete = output<AccountResponse>();

  protected subtitle(a: AccountResponse): string {
    const parts = [a.institution ?? '', accountKindLabel(a.kind)].filter(p => p.length > 0);
    return parts.join(' · ');
  }

  protected balanceText(a: AccountResponse): string | null {
    return formatAccountBalance(a, this.displayCurrency());
  }

  protected latestTxText(a: AccountResponse): string {
    const value = a.latestTransactionDate;
    return value ? formatDate(value, 'mediumDate', this.locale) : '—';
  }
}
