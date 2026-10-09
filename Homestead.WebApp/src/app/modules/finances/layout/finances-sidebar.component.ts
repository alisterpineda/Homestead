import { Component, ChangeDetectionStrategy, inject, computed, signal, effect } from '@angular/core';
import { Router, RouterLink, NavigationEnd } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideWallet, lucideSettings, lucideCoins, lucideTrendingUp, lucideChevronRight, lucideFolderTree, lucideContact } from '@ng-icons/lucide';
import { HlmSidebarImports } from '@/shared/components/spartan/sidebar';
import { HlmCollapsibleImports } from '@/shared/components/spartan/collapsible';
import { AuthService } from '@/shared/services/auth.service';
import { isNavItemActive } from '@/shared/utils';

@Component({
  selector: 'app-finances-sidebar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, HlmSidebarImports, HlmCollapsibleImports],
  providers: [provideIcons({ lucideWallet, lucideSettings, lucideCoins, lucideTrendingUp, lucideChevronRight, lucideFolderTree, lucideContact })],
  template: `
    <div hlmSidebarGroup>
      <div hlmSidebarGroupLabel>Finances</div>
      <div hlmSidebarGroupContent>
        <ul hlmSidebarMenu>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/finances/accounts" [isActive]="isActive('/finances/accounts')">
              <ng-icon name="lucideWallet" class="text-[length:--spacing(4)]" />
              <span>Accounts</span>
            </a>
          </li>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/finances/commodities" [isActive]="isActive('/finances/commodities')">
              <ng-icon name="lucideCoins" class="text-[length:--spacing(4)]" />
              <span>Commodities</span>
            </a>
          </li>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/finances/categories" [isActive]="isActive('/finances/categories')">
              <ng-icon name="lucideFolderTree" class="text-[length:--spacing(4)]" />
              <span>Categories</span>
            </a>
          </li>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/finances/payees" [isActive]="isActive('/finances/payees')">
              <ng-icon name="lucideContact" class="text-[length:--spacing(4)]" />
              <span>Payees</span>
            </a>
          </li>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/finances/reports" [isActive]="isActive('/finances/reports')">
              <ng-icon name="lucideTrendingUp" class="text-[length:--spacing(4)]" />
              <span>Reports</span>
            </a>
          </li>
          <li hlmSidebarMenuItem>
            <div hlmCollapsible [(expanded)]="settingsExpanded">
              <button hlmCollapsibleTrigger hlmSidebarMenuButton [isActive]="isAnySettingsActive()">
                <ng-icon name="lucideSettings" class="text-[length:--spacing(4)]" />
                <span>Settings</span>
                <ng-icon
                  name="lucideChevronRight"
                  class="text-[length:--spacing(4)] ml-auto transition-transform"
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
    '/finances/categories',
    '/finances/payees',
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
