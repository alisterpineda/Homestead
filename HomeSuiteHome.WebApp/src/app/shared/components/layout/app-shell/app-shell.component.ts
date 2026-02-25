import { Component, ChangeDetectionStrategy, inject, computed } from '@angular/core';
import { Router, RouterOutlet, NavigationEnd } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSettings, lucideFileText, lucideLogOut } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import {
  HlmSidebarImports,
  HlmSidebarService,
} from '@/shared/components/spartan/sidebar';
import { HlmSeparator } from '@/shared/components/spartan/separator';
import {
  HlmBreadcrumb,
  HlmBreadcrumbList,
  HlmBreadcrumbItem,
  HlmBreadcrumbPage,
} from '@/shared/components/spartan/breadcrumb';
import { HlmButton } from '@/shared/components/spartan/button';
import { AppSwitcherComponent } from '../app-switcher/app-switcher.component';
import { ThemeToggleComponent } from '@/shared/components/theme/theme-toggle/theme-toggle.component';
import { AuthService } from '@/shared/services/auth.service';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { getModuleByPath, modules } from '@/modules/registry';
import type { SidebarItem } from '@/shared/types/modules';
import { RouterLink, RouterLinkActive } from '@angular/router';

@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    NgIcon,
    HlmIconImports,
    HlmSidebarImports,
    HlmSeparator,
    HlmBreadcrumb,
    HlmBreadcrumbList,
    HlmBreadcrumbItem,
    HlmBreadcrumbPage,
    HlmButton,
    AppSwitcherComponent,
    ThemeToggleComponent,
  ],
  providers: [provideIcons({ lucideSettings, lucideFileText, lucideLogOut })],
  template: `
    <div hlmSidebarWrapper>
      <hlm-sidebar variant="inset">
        <!-- Sidebar Header -->
        <div hlmSidebarHeader>
          <div class="flex items-center gap-2">
            <app-switcher />
            @if (activeModule()) {
              <div class="flex items-center gap-2 overflow-hidden">
                <ng-icon hlm [name]="activeModule()!.icon" size="sm" class="shrink-0 text-sidebar-foreground/70" />
                <span class="truncate text-sm font-semibold text-sidebar-foreground">
                  {{ activeModule()!.label }}
                </span>
              </div>
            }
            <div class="ml-auto">
              <app-theme-toggle />
            </div>
          </div>
        </div>

        <!-- Sidebar Content -->
        <div hlmSidebarContent>
          @if (activeModule()) {
            <div hlmSidebarGroup>
              <div hlmSidebarGroupContent>
                <ul hlmSidebarMenu>
                  @for (item of activeModule()!.sidebar; track item.path) {
                    <li hlmSidebarMenuItem>
                      <a
                        hlmSidebarMenuButton
                        [routerLink]="item.path"
                        routerLinkActive="true"
                        [routerLinkActiveOptions]="{ exact: true }"
                        [isActive]="isItemActive(item)"
                      >
                        @if (item.icon) {
                          <ng-icon hlm [name]="item.icon" size="sm" />
                        }
                        <span>{{ item.label }}</span>
                      </a>
                    </li>
                  }
                </ul>
              </div>
            </div>
          }
        </div>

        <!-- Sidebar Footer -->
        <div hlmSidebarFooter>
          <div class="flex items-center gap-2">
            <div
              class="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-sidebar-accent text-sm font-medium text-sidebar-accent-foreground"
              aria-hidden="true"
            >
              {{ initials() }}
            </div>
            <div class="min-w-0 flex-1">
              <p class="truncate text-sm font-medium text-sidebar-foreground">
                {{ auth.userName() }}
              </p>
              @if (auth.userEmail()) {
                <p class="truncate text-xs text-sidebar-foreground/70">
                  {{ auth.userEmail() }}
                </p>
              }
            </div>
            <button
              hlmBtn
              variant="ghost"
              size="icon-sm"
              (click)="auth.logout()"
              aria-label="Log out"
            >
              <ng-icon hlm name="lucideLogOut" size="sm" />
            </button>
          </div>
        </div>
      </hlm-sidebar>

      <main hlmSidebarInset>
        <!-- Inset Header -->
        <header class="flex h-12 shrink-0 items-center gap-2 border-b border-border px-4">
          <button hlmSidebarTrigger></button>
          <hlm-separator orientation="vertical" class="mr-2 !h-4" />
          <nav hlmBreadcrumb>
            <ol hlmBreadcrumbList>
              <li hlmBreadcrumbItem>
                @if (activeModule()) {
                  <span hlmBreadcrumbPage>{{ activeModule()!.label }}</span>
                }
              </li>
            </ol>
          </nav>
        </header>

        <div class="flex-1 overflow-y-auto p-4 md:p-8">
          <router-outlet />
        </div>
      </main>
    </div>
  `,
})
export class AppShellComponent {
  protected auth = inject(AuthService);
  private router = inject(Router);
  private processingHub = inject(ProcessingHubService);
  private sidebarService = inject(HlmSidebarService);

  constructor() {
    this.processingHub.connect();
  }

  private url = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map((e) => (e as NavigationEnd).urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  activeModule = computed(() => getModuleByPath(this.url()));

  protected initials = computed(() => {
    const name = this.auth.userName();
    if (!name) return '?';
    return name
      .split(/\s+/)
      .map((part) => part[0])
      .join('')
      .toUpperCase()
      .slice(0, 2);
  });

  isItemActive(item: SidebarItem): boolean {
    return this.router.url === item.path || this.router.url.startsWith(item.path + '?');
  }
}
