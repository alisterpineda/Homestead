import { Component, ChangeDetectionStrategy, inject, computed } from '@angular/core';
import { Router, RouterLink, NavigationEnd } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideWallet, lucideSettings, lucideCoins, lucideTrendingUp } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSidebarImports } from '@/shared/components/spartan/sidebar';
import { isNavItemActive } from '@/shared/utils';

@Component({
  selector: 'app-finances-sidebar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, HlmIconImports, HlmSidebarImports],
  providers: [provideIcons({ lucideWallet, lucideSettings, lucideCoins, lucideTrendingUp })],
  template: `
    <div hlmSidebarGroup>
      <div hlmSidebarGroupLabel>Finances</div>
      <div hlmSidebarGroupContent>
        <ul hlmSidebarMenu>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/finances/accounts" [isActive]="isActive('/finances/accounts')">
              <ng-icon hlm name="lucideWallet" size="sm" />
              <span>Accounts</span>
            </a>
          </li>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/finances/commodities" [isActive]="isActive('/finances/commodities')">
              <ng-icon hlm name="lucideCoins" size="sm" />
              <span>Commodities</span>
            </a>
          </li>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/finances/reports" [isActive]="isActive('/finances/reports')">
              <ng-icon hlm name="lucideTrendingUp" size="sm" />
              <span>Reports</span>
            </a>
          </li>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/finances/settings" [isActive]="isActive('/finances/settings')">
              <ng-icon hlm name="lucideSettings" size="sm" />
              <span>Settings</span>
            </a>
          </li>
        </ul>
      </div>
    </div>
  `,
})
export class FinancesSidebarComponent {
  private router = inject(Router);

  private url = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map((e) => (e as NavigationEnd).urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  private urlPath = computed(() => this.url().split('?')[0]);

  private readonly navPaths = ['/finances/accounts', '/finances/commodities', '/finances/reports', '/finances/settings'];

  isActive(path: string): boolean {
    return isNavItemActive(this.urlPath(), path, this.navPaths);
  }
}
