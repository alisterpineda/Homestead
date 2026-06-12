import { Component, ChangeDetectionStrategy, inject, computed } from '@angular/core';
import { Router, RouterLink, NavigationEnd } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePackage, lucideMapPin, lucideLayoutDashboard } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSidebarImports } from '@/shared/components/spartan/sidebar';
import { isNavItemActive } from '@/shared/utils';

@Component({
  selector: 'app-inventory-sidebar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, HlmIconImports, HlmSidebarImports],
  providers: [provideIcons({ lucidePackage, lucideMapPin, lucideLayoutDashboard })],
  template: `
    <div hlmSidebarGroup>
      <div hlmSidebarGroupContent>
        <ul hlmSidebarMenu>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/inventory" [isActive]="isActive('/inventory')">
              <ng-icon hlm name="lucideLayoutDashboard" size="sm" />
              <span>Overview</span>
            </a>
          </li>
        </ul>
      </div>
    </div>

    <div hlmSidebarGroup>
      <div hlmSidebarGroupLabel>Manage</div>
      <div hlmSidebarGroupContent>
        <ul hlmSidebarMenu>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/inventory/items" [isActive]="isActive('/inventory/items')">
              <ng-icon hlm name="lucidePackage" size="sm" />
              <span>Items</span>
            </a>
          </li>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/inventory/areas" [isActive]="isActive('/inventory/areas')">
              <ng-icon hlm name="lucideMapPin" size="sm" />
              <span>Areas</span>
            </a>
          </li>
        </ul>
      </div>
    </div>
  `,
})
export class InventorySidebarComponent {
  private router = inject(Router);

  private url = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map((e) => (e as NavigationEnd).urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  private urlPath = computed(() => this.url().split('?')[0]);

  private readonly navPaths = ['/inventory', '/inventory/items', '/inventory/areas'];

  isActive(path: string): boolean {
    // The supply detail route outlived the Items/Supplies list merge; it belongs to Items
    // (its back-link targets /inventory/items), so highlight that entry there.
    const url = this.urlPath().startsWith('/inventory/supplies')
      ? '/inventory/items'
      : this.urlPath();
    return isNavItemActive(url, path, this.navPaths);
  }
}
