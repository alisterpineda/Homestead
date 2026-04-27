import { Component, ChangeDetectionStrategy, input, output, signal, computed, inject } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronRight } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmCollapsibleImports } from '@/shared/components/spartan/collapsible';
import { AccountRowActionsComponent } from './account-row-actions.component';
import { FinanceSettingsService } from '../shared/finance-settings.service';
import {
  formatAccountBalance,
  formatNativeBalance,
  toNumberOrNull,
  netWorth,
} from '../shared/sign';
import { formatCurrency } from '../shared/format-currency';
import type { AccountResponse } from '@/api/generated/models/account-response';
import type { CommodityResponse } from '@/api/generated/models/commodity-response';

@Component({
  selector: 'app-account-group-section',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet, RouterLink, NgIcon, HlmIconImports, HlmCollapsibleImports, AccountRowActionsComponent],
  providers: [provideIcons({ lucideChevronRight })],
  template: `
    <ng-template #row let-account="account" let-dim="dim">
      <li class="flex items-center gap-3 px-4 py-3 hover:bg-accent/50" [class.opacity-70]="dim">
        <a class="min-w-0 flex-1 cursor-pointer" [routerLink]="['/finances/accounts', account.id]">
          <div class="flex items-baseline gap-2">
            <span class="truncate text-sm font-medium">{{ account.name }}</span>
            @if (account.institution) {
              <span class="truncate text-xs text-muted-foreground">{{ account.institution }}</span>
            }
          </div>
          @if (unpricedCount(account) > 0) {
            <p class="text-xs text-amber-600 dark:text-amber-500">
              {{ unpricedCount(account) }} unpriced holding{{ unpricedCount(account) === 1 ? '' : 's' }}
            </p>
          }
        </a>
        <div class="text-right shrink-0 tabular-nums">
          <div class="text-sm font-medium">{{ displayText(account) ?? '—' }}</div>
          @if (showNative(account)) {
            <div class="text-xs text-muted-foreground">{{ nativeText(account) }}</div>
          }
        </div>
        <app-account-row-actions
          [account]="account"
          (edit)="edit.emit($event)"
          (archiveToggle)="archiveToggle.emit($event)"
          (delete)="delete.emit($event)"
        />
      </li>
    </ng-template>

    @if (visible().length > 0 || archived().length > 0) {
      <section class="rounded-lg border border-border bg-card text-card-foreground">
        <header class="flex items-center justify-between border-b border-border px-4 py-3">
          <h3 class="text-sm font-semibold">{{ label() }}</h3>
          <span class="text-sm font-medium tabular-nums">{{ groupTotalText() }}</span>
        </header>
        <ul role="list" class="divide-y divide-border">
          @for (account of visible(); track account.id) {
            <ng-container *ngTemplateOutlet="row; context: { account, dim: false }" />
          }
        </ul>

        @if (archived().length > 0) {
          <div hlmCollapsible [(expanded)]="archivedExpanded" class="border-t border-border">
            <button
              hlmCollapsibleTrigger
              type="button"
              class="flex w-full items-center gap-2 px-4 py-2 text-left text-xs text-muted-foreground hover:bg-accent/50"
            >
              <ng-icon
                hlm
                name="lucideChevronRight"
                size="sm"
                class="transition-transform"
                [class.rotate-90]="archivedExpanded()"
              />
              Archived ({{ archived().length }})
            </button>
            <div hlmCollapsibleContent>
              <ul role="list" class="divide-y divide-border">
                @for (account of archived(); track account.id) {
                  <ng-container *ngTemplateOutlet="row; context: { account, dim: true }" />
                }
              </ul>
            </div>
          </div>
        }
      </section>
    }
  `,
})
export class AccountGroupSectionComponent {
  label = input.required<string>();
  accounts = input.required<AccountResponse[]>();
  reportingCurrencies = input<Record<string, CommodityResponse>>({});

  edit = output<AccountResponse>();
  archiveToggle = output<AccountResponse>();
  delete = output<AccountResponse>();

  archivedExpanded = signal(false);

  private settingsService = inject(FinanceSettingsService);

  visible = computed(() => this.accounts().filter(a => !a.isArchived));
  archived = computed(() => this.accounts().filter(a => a.isArchived));

  groupTotalText = computed(() => {
    const dc = this.settingsService.displayCurrency();
    const visible = this.visible();
    if (!dc || visible.length === 0) return '';
    // Beancount-stored sign sums to the right value; flip for liability groups
    // so the section header shows positive "amount owed".
    const anyLiability = visible.some(a => a.accountType === 'Liability');
    const total = netWorth(visible);
    return formatCurrency(anyLiability ? -total : total, dc);
  });

  displayText(account: AccountResponse): string | null {
    return formatAccountBalance(account, this.settingsService.displayCurrency());
  }

  nativeText(account: AccountResponse): string | null {
    const reporting = this.reportingCurrencies()[account.reportingCurrencyId] ?? null;
    return formatNativeBalance(account, reporting);
  }

  showNative(account: AccountResponse): boolean {
    if (account.kind === 'Investment') return false;
    const dc = this.settingsService.displayCurrency();
    if (!dc || dc.id === account.reportingCurrencyId) return false;
    return toNumberOrNull(account.nativeBalance ?? null) !== null;
  }

  unpricedCount(account: AccountResponse): number {
    return Number(account.unpricedHoldingCount ?? 0);
  }
}
