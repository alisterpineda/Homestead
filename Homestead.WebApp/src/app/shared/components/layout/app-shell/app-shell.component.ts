import { Component, ChangeDetectionStrategy, inject, computed, viewChild } from '@angular/core';
import { Router, RouterOutlet, NavigationEnd, NavigationStart, ActivatedRoute } from '@angular/router';
import { NgComponentOutlet, NgTemplateOutlet } from '@angular/common';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLogOut, lucideEllipsisVertical, lucideCircleUserRound, lucideSunMoon } from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmSidebarImports } from '@/shared/components/spartan/sidebar';
import { HlmSeparator } from '@/shared/components/spartan/separator';
import { HlmAlertDialog, HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { ProfileDialogComponent } from '../../profile/profile.component';
import { ThemeDialogComponent } from '../../theme/theme-dialog/theme-dialog.component';
import { AppSwitcherComponent } from '../app-switcher/app-switcher.component';
import { AuthService } from '@/shared/services/auth.service';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { getModuleByPath } from '@/modules/registry';

@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterOutlet,
    NgComponentOutlet,
    NgTemplateOutlet,
    NgIcon,
    HlmIconImports,
    HlmSidebarImports,
    HlmSeparator,
    HlmAlertDialogImports,
    HlmDropdownMenuImports,
    ProfileDialogComponent,
    ThemeDialogComponent,
    AppSwitcherComponent,
  ],
  providers: [provideIcons({ lucideLogOut, lucideEllipsisVertical, lucideCircleUserRound, lucideSunMoon })],
  template: `
    <div hlmSidebarWrapper class="max-h-svh">
      <hlm-sidebar variant="inset">
        <!-- Sidebar Header -->
        <div hlmSidebarHeader>
          <div class="flex items-center gap-2">
            <app-switcher />
            @if (activeModule()) {
              <span class="truncate text-sm font-semibold text-sidebar-foreground">
                {{ activeModule()!.label }}
              </span>
            }
          </div>
        </div>

        <!-- Sidebar Content -->
        <div hlmSidebarContent>
          @if (activeModule()?.sidebarComponent) {
            <ng-container *ngComponentOutlet="activeModule()!.sidebarComponent" />
          }
        </div>

        <!-- Sidebar Footer -->
        <div hlmSidebarFooter>
          <ul hlmSidebarMenu>
            <li hlmSidebarMenuItem>
              <button hlmSidebarMenuButton [hlmDropdownMenuTrigger]="userMenu" side="top" align="end">
                <div
                  class="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-sidebar-accent text-sm font-medium text-sidebar-accent-foreground"
                  aria-hidden="true"
                >
                  {{ initials() }}
                </div>
                <div class="min-w-0 flex-1 text-left">
                  <p class="truncate text-sm font-medium">{{ auth.userName() }}</p>
                  <p class="truncate text-xs text-sidebar-foreground/70">{{ auth.userEmail() }}</p>
                </div>
                <ng-icon hlm name="lucideEllipsisVertical" size="sm" class="ml-auto" />
              </button>

              <ng-template #userMenu>
                <hlm-dropdown-menu class="w-56">
                  <hlm-dropdown-menu-label class="font-normal">
                    <div class="flex items-center gap-2">
                      <div
                        class="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-accent text-sm font-medium text-accent-foreground"
                        aria-hidden="true"
                      >
                        {{ initials() }}
                      </div>
                      <div class="min-w-0 flex-1">
                        <p class="truncate text-sm font-medium">{{ auth.userName() }}</p>
                        <p class="truncate text-xs text-muted-foreground">{{ auth.userEmail() }}</p>
                      </div>
                    </div>
                  </hlm-dropdown-menu-label>
                  <hlm-dropdown-menu-separator />
                  <hlm-dropdown-menu-group>
                    <button hlmDropdownMenuItem class="gap-2" (triggered)="profileDialog.open()">
                      <ng-icon hlm name="lucideCircleUserRound" size="sm" />
                      Profile
                    </button>
                    <button hlmDropdownMenuItem class="gap-2" (triggered)="themeDialog.open()">
                      <ng-icon hlm name="lucideSunMoon" size="sm" />
                      Theme
                    </button>
                  </hlm-dropdown-menu-group>
                  <hlm-dropdown-menu-separator />
                  <hlm-dropdown-menu-group>
                    <button hlmDropdownMenuItem class="gap-2" (triggered)="onLogoutRequest()">
                      <ng-icon hlm name="lucideLogOut" size="sm" />
                      Log out
                    </button>
                  </hlm-dropdown-menu-group>
                </hlm-dropdown-menu>
              </ng-template>
            </li>
          </ul>

          <app-profile-dialog #profileDialog />
          <app-theme-dialog #themeDialog />

          <hlm-alert-dialog>
            <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
              <div hlmAlertDialogHeader>
                <h2 hlmAlertDialogTitle>Log out?</h2>
                <p hlmAlertDialogDescription>
                  Are you sure you want to log out of your account?
                </p>
              </div>
              <div hlmAlertDialogFooter>
                <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
                <button hlmAlertDialogAction (click)="onLogout(ctx)">Log out</button>
              </div>
            </hlm-alert-dialog-content>
          </hlm-alert-dialog>
        </div>
      </hlm-sidebar>

      <main hlmSidebarInset class="min-h-0 min-w-0">
        <!-- Inset Header -->
        <header class="flex h-12 shrink-0 items-center gap-2 border-b border-border px-4">
          <button hlmSidebarTrigger></button>
          <hlm-separator orientation="vertical" class="mr-2 !h-4" />
          @if (headerTitle()) {
            <ng-container *ngTemplateOutlet="headerTitle()!" />
          } @else {
            <h1 class="text-sm font-medium">{{ pageTitle() }}</h1>
          }
          <div class="ml-auto flex items-center gap-2">
            @if (headerActions()) {
              <ng-container *ngTemplateOutlet="headerActions()!" />
            }
          </div>
        </header>

        <div class="flex flex-1 flex-col overflow-y-auto p-4">
          <router-outlet />
        </div>
      </main>
    </div>
  `,
})
export class AppShellComponent {
  protected auth = inject(AuthService);
  private router = inject(Router);
  private activatedRoute = inject(ActivatedRoute);
  private processingHub = inject(ProcessingHubService);
  private pageHeaderService = inject(PageHeaderService);

  headerActions = this.pageHeaderService.actions;
  headerTitle = this.pageHeaderService.title;

  constructor() {
    this.processingHub.connect();

    // Auto-clear header actions on navigation to prevent stale buttons
    this.router.events
      .pipe(filter((e) => e instanceof NavigationStart))
      .subscribe(() => {
        this.pageHeaderService.clearActions();
        this.pageHeaderService.clearTitle();
      });
  }

  private url = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map((e) => (e as NavigationEnd).urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  activeModule = computed(() => getModuleByPath(this.url()));

  pageTitle = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map(() => this.getDeepestTitle()),
    ),
    { initialValue: '' },
  );

  private getDeepestTitle(): string {
    let route: ActivatedRoute | null = this.activatedRoute;
    while (route?.firstChild) {
      route = route.firstChild;
    }
    return (route?.snapshot?.data?.['title'] as string) ?? '';
  }

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

  private logoutDialog = viewChild(HlmAlertDialog);

  protected onLogoutRequest(): void {
    this.logoutDialog()?.open();
  }

  protected onLogout(ctx: { close: () => void }): void {
    ctx.close();
    this.auth.logout();
  }
}
