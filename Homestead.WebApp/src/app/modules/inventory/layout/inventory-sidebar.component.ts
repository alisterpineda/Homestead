import { Component, ChangeDetectionStrategy, inject, computed } from '@angular/core';
import { Router, RouterLink, NavigationEnd } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePackage, lucideMapPin, lucideCompass, lucideTags, lucideBookmark } from '@ng-icons/lucide';
import { HlmSidebarImports } from '@/shared/components/spartan/sidebar';
import { isNavItemActive } from '@/shared/utils';

@Component({
  selector: 'app-inventory-sidebar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, NgIcon, HlmSidebarImports],
  providers: [provideIcons({ lucidePackage, lucideMapPin, lucideCompass, lucideTags, lucideBookmark })],
  template: `
    <div hlmSidebarGroup>
      <div hlmSidebarGroupContent>
        <ul hlmSidebarMenu>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/inventory/browse" [isActive]="isActive('/inventory/browse')">
              <ng-icon name="lucideCompass" class="text-[length:--spacing(4)]" />
              <span>Browse</span>
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
              <ng-icon name="lucidePackage" class="text-[length:--spacing(4)]" />
              <span>Items</span>
            </a>
          </li>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/inventory/areas" [isActive]="isActive('/inventory/areas')">
              <ng-icon name="lucideMapPin" class="text-[length:--spacing(4)]" />
              <span>Areas</span>
            </a>
          </li>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/inventory/tags" [isActive]="isActive('/inventory/tags')">
              <ng-icon name="lucideTags" class="text-[length:--spacing(4)]" />
              <span>Tags</span>
            </a>
          </li>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/inventory/brands" [isActive]="isActive('/inventory/brands')">
              <ng-icon name="lucideBookmark" class="text-[length:--spacing(4)]" />
              <span>Brands</span>
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

  private readonly navPaths = ['/inventory/browse', '/inventory/items', '/inventory/areas', '/inventory/tags', '/inventory/brands'];

  isActive(path: string): boolean {
    // The unified item detail route (/inventory/items/:id) sits under Items, so it highlights that
    // entry naturally — no special-casing needed.
    return isNavItemActive(this.urlPath(), path, this.navPaths);
  }
}
