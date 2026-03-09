import { Component, ChangeDetectionStrategy, inject, computed } from '@angular/core';
import { Router, RouterLink, NavigationEnd } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLayoutDashboard, lucideUsers, lucideUsers2, lucideActivity } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSidebarImports } from '@/shared/components/spartan/sidebar';
import { isNavItemActive } from '@/shared/utils';

@Component({
  selector: 'app-admin-sidebar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    NgIcon,
    HlmIconImports,
    HlmSidebarImports,
  ],
  providers: [provideIcons({ lucideLayoutDashboard, lucideUsers, lucideUsers2, lucideActivity })],
  template: `
    <div hlmSidebarGroup>
      <div hlmSidebarGroupContent>
        <ul hlmSidebarMenu>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/admin" [isActive]="isActive('/admin')">
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
            <a hlmSidebarMenuButton routerLink="/admin/users" [isActive]="isActive('/admin/users')">
              <ng-icon hlm name="lucideUsers" size="sm" />
              <span>Users</span>
            </a>
          </li>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/admin/groups" [isActive]="isActive('/admin/groups')">
              <ng-icon hlm name="lucideUsers2" size="sm" />
              <span>Groups</span>
            </a>
          </li>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/admin/jobs" [isActive]="isActive('/admin/jobs')">
              <ng-icon hlm name="lucideActivity" size="sm" />
              <span>Jobs</span>
            </a>
          </li>
        </ul>
      </div>
    </div>
  `,
})
export class AdminSidebarComponent {
  private router = inject(Router);

  private url = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map((e) => (e as NavigationEnd).urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  private urlPath = computed(() => this.url().split('?')[0]);

  private readonly navPaths = ['/admin', '/admin/users', '/admin/groups', '/admin/jobs'];

  isActive(path: string): boolean {
    return isNavItemActive(this.urlPath(), path, this.navPaths);
  }
}
