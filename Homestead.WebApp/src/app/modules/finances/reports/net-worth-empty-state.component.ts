import { Component, ChangeDetectionStrategy, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTrendingUp, lucideCircleAlert } from '@ng-icons/lucide';
import { HlmEmptyImports } from '@/shared/components/spartan/empty';
import { HlmButton } from '@/shared/components/spartan/button';

export type NetWorthEmptyReason = 'no_transactions' | 'no_display_currency';

@Component({
  selector: 'app-net-worth-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, HlmEmptyImports, HlmButton],
  providers: [provideIcons({ lucideTrendingUp, lucideCircleAlert })],
  template: `
    <div hlmEmpty>
      <div hlmEmptyHeader>
        <div hlmEmptyMedia variant="icon">
          <ng-icon [name]="reason() === 'no_display_currency' ? 'lucideCircleAlert' : 'lucideTrendingUp'" />
        </div>
        @if (reason() === 'no_display_currency') {
          <div hlmEmptyTitle>Set a display currency</div>
          <div hlmEmptyDescription>
            Net worth is shown in one currency you choose. Pick a display currency in Settings to start
            charting your net worth.
          </div>
        } @else {
          <div hlmEmptyTitle>No activity yet</div>
          <div hlmEmptyDescription>
            Once you record your first transaction or balance entry, your net worth history will appear here.
          </div>
        }
      </div>
      <div hlmEmptyContent>
        @if (reason() === 'no_display_currency') {
          <a hlmBtn routerLink="/finances/settings">Open settings</a>
        } @else {
          <a hlmBtn routerLink="/finances/accounts">Go to accounts</a>
        }
      </div>
    </div>
  `,
})
export class NetWorthEmptyStateComponent {
  readonly reason = input.required<NetWorthEmptyReason>();
}
