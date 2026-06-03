import { Component, ChangeDetectionStrategy, inject, computed, signal, effect } from '@angular/core';
import { Router, RouterLink, NavigationEnd } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideWallet, lucideSettings, lucideCoins, lucideTrendingUp, lucideChevronRight } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSidebarImports } from '@/shared/components/spartan/sidebar';
import { HlmCollapsibleImports } from '@/shared/components/spartan/collapsible';
import { AuthService } from '@/shared/services/auth.service';
import { isNavItemActive } from '@/shared/utils';

@Component({
  selector: 'app-finances-sidebar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, HlmIconImports, HlmSidebarImports, HlmCollapsibleImports],
  providers: [provideIcons({ lucideWallet, lucideSettings, lucideCoins, lucideTrendingUp, lucideChevronRight })],
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
            <div hlmCollapsible [(expanded)]="settingsExpanded">
              <button hlmCollapsibleTrigger hlmSidebarMenuButton [isActive]="isAnySettingsActive()">
                <ng-icon hlm name="lucideSettings" size="sm" />
                <span>Settings</span>
                <ng-icon
                  hlm
                  name="lucideChevronRight"
                  size="sm"
                  class="ml-auto transition-transform"
                  [class.rotate-90]="settingsExpanded()"
                />
              </button>
              <div hlmCollapsibleContent>
                <ul hlmSidebarMenuSub>
                  <li hlmSidebarMenuSubItem>
                    <a
                      hlmSidebarMenuSubButton
                      routerLink="/finances/settings/general"
                      [isActive]="isActive('/finances/settings/general')"
                    >
                      <span>General</span>
                    </a>
                  </li>
                  @if (isAdmin()) {
                    <li hlmSidebarMenuSubItem>
                      <a
                        hlmSidebarMenuSubButton
                        routerLink="/finances/settings/price-sources"
                        [isActive]="isActive('/finances/settings/price-sources')"
                      >
                        <span>Price sources</span>
                      </a>
                    </li>
                  }
                </ul>
              </div>
            </div>
          </li>
        </ul>
      </div>
    </div>
  `,
})
export class FinancesSidebarComponent {
  private router = inject(Router);
  protected readonly isAdmin = inject(AuthService).isAdmin;

  private url = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map((e) => (e as NavigationEnd).urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  private urlPath = computed(() => this.url().split('?')[0]);

  private readonly navPaths = [
    '/finances/accounts',
    '/finances/commodities',
    '/finances/reports',
    '/finances/settings/general',
    '/finances/settings/price-sources',
  ];

  settingsExpanded = signal(false);

  isAnySettingsActive = computed(() =>
    this.isActive('/finances/settings/general') || this.isActive('/finances/settings/price-sources'),
  );

  constructor() {
    effect(() => {
      if (this.isAnySettingsActive() && !this.settingsExpanded()) {
        this.settingsExpanded.set(true);
      }
    });
  }

  isActive(path: string): boolean {
    return isNavItemActive(this.urlPath(), path, this.navPaths);
  }
}
