import { Component, ChangeDetectionStrategy, inject, computed } from '@angular/core';
import { Router, RouterLink, NavigationEnd } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLayoutDashboard, lucideUsers, lucideUsers2, lucideActivity, lucideCalendarClock, lucideSettings, lucideSparkles, lucideDatabaseBackup } from '@ng-icons/lucide';
import { HlmSidebarImports } from '@/shared/components/spartan/sidebar';
import { isNavItemActive } from '@/shared/utils';

@Component({
  selector: 'app-admin-sidebar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    NgIcon,
    HlmSidebarImports,
  ],
  providers: [provideIcons({ lucideLayoutDashboard, lucideUsers, lucideUsers2, lucideActivity, lucideCalendarClock, lucideSettings, lucideSparkles, lucideDatabaseBackup })],
  template: `
    <div hlmSidebarGroup>
      <div hlmSidebarGroupContent>
        <ul hlmSidebarMenu>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/admin" [isActive]="isActive('/admin')">
              <ng-icon name="lucideLayoutDashboard" class="text-[length:--spacing(4)]" />
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
              <ng-icon name="lucideUsers" class="text-[length:--spacing(4)]" />
              <span>Users</span>
            </a>
          </li>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/admin/groups" [isActive]="isActive('/admin/groups')">
              <ng-icon name="lucideUsers2" class="text-[length:--spacing(4)]" />
              <span>Groups</span>
            </a>
          </li>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/admin/jobs" [isActive]="isActive('/admin/jobs')">
              <ng-icon name="lucideActivity" class="text-[length:--spacing(4)]" />
              <span>Jobs</span>
            </a>
          </li>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/admin/schedules" [isActive]="isActive('/admin/schedules')">
              <ng-icon name="lucideCalendarClock" class="text-[length:--spacing(4)]" />
              <span>Schedules</span>
            </a>
          </li>
        </ul>
      </div>
    </div>

    <div hlmSidebarGroup>
      <div hlmSidebarGroupLabel>Configuration</div>
      <div hlmSidebarGroupContent>
        <ul hlmSidebarMenu>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/admin/settings" [isActive]="isActive('/admin/settings')">
              <ng-icon name="lucideSettings" class="text-[length:--spacing(4)]" />
              <span>System Settings</span>
            </a>
          </li>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/admin/ai" [isActive]="isActive('/admin/ai')">
              <ng-icon name="lucideSparkles" class="text-[length:--spacing(4)]" />
              <span>AI</span>
            </a>
          </li>
          <li hlmSidebarMenuItem>
            <a hlmSidebarMenuButton routerLink="/admin/backup" [isActive]="isActive('/admin/backup')">
              <ng-icon name="lucideDatabaseBackup" class="text-[length:--spacing(4)]" />
              <span>Backup</span>
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

  private readonly navPaths = ['/admin', '/admin/users', '/admin/groups', '/admin/jobs', '/admin/schedules', '/admin/settings', '/admin/ai', '/admin/backup'];

  isActive(path: string): boolean {
    return isNavItemActive(this.urlPath(), path, this.navPaths);
  }
}
