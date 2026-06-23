import { Component, ChangeDetectionStrategy, inject, computed, viewChild } from '@angular/core';
import { Router, RouterOutlet, NavigationEnd, NavigationStart, ActivatedRoute } from '@angular/router';
import { NgComponentOutlet, NgTemplateOutlet } from '@angular/common';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideLogOut,
  lucideEllipsisVertical,
  lucideCircleUserRound,
  lucideSunMoon,
  lucideSearch,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmSidebarImports } from '@/shared/components/spartan/sidebar';
import { HlmSeparator } from '@/shared/components/spartan/separator';
import { HlmAlertDialog, HlmAlertDialogImports } from '@/shared/components/spartan/alert-dialog';
import { HlmDropdownMenuImports } from '@/shared/components/spartan/dropdown-menu';
import { ProfileDialogComponent } from '../../profile/profile.component';
import { ThemeDialogComponent } from '../../theme/theme-dialog/theme-dialog.component';
import { AppSwitcherComponent } from '../app-switcher/app-switcher.component';
import { CommandPaletteComponent } from '@/shared/components/search/command-palette.component';
import { GlobalSearchService } from '@/shared/services/global-search.service';
import { AuthService } from '@/shared/services/auth.service';
import { ProcessingHubService } from '@/shared/services/processing-hub.service';
import { PageHeaderService } from '@/shared/services/page-header.service';
import { FinancesPrivacyAutoMaskService } from '@/modules/finances/shared/finances-privacy-auto-mask.service';
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
    HlmButton,
    HlmSidebarImports,
    HlmSeparator,
    HlmAlertDialogImports,
    HlmDropdownMenuImports,
    ProfileDialogComponent,
    ThemeDialogComponent,
    AppSwitcherComponent,
    CommandPaletteComponent,
  ],
  providers: [
    provideIcons({ lucideLogOut, lucideEllipsisVertical, lucideCircleUserRound, lucideSunMoon, lucideSearch }),
  ],
  template: `
    <div hlmSidebarWrapper class="max-h-svh">
      <hlm-sidebar variant="inset">
        <!-- Sidebar Header -->
        <div hlmSidebarHeader>
          <app-switcher [activeModule]="activeModule()" />
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
        <header class="flex h-12 shrink-0 items-center gap-2 border-b border-border px-4 select-none">
          <button hlmSidebarTrigger></button>
          <hlm-separator orientation="vertical" class="mr-2 !h-4" />
          @if (headerTitle()) {
            <div class="min-w-0">
              <ng-container *ngTemplateOutlet="headerTitle()!" />
            </div>
          } @else {
            <h1 class="min-w-0 truncate text-sm font-medium">{{ pageTitle() }}</h1>
          }

          <div class="ml-auto flex shrink-0 items-center gap-2">
            <!-- Global search trigger: faux-input on desktop, icon button on mobile. ⌘K opens it too. -->
            <button
              type="button"
              (click)="globalSearch.openPalette()"
              class="hidden h-8 w-64 items-center gap-2 rounded-md border border-input bg-transparent px-3 text-sm text-muted-foreground shadow-xs transition-colors hover:bg-accent/50 md:flex"
              aria-label="Search"
            >
              <ng-icon hlm name="lucideSearch" size="sm" />
              <span>Search…</span>
              <kbd
                class="ml-auto hidden items-center gap-0.5 rounded border border-border bg-muted px-1.5 font-mono text-[10px] font-medium text-muted-foreground lg:inline-flex"
                aria-hidden="true"
              >⌘K</kbd>
            </button>

            <button
              hlmBtn
              variant="ghost"
              size="icon"
              type="button"
              (click)="globalSearch.openPalette()"
              class="flex md:hidden"
              aria-label="Search"
            >
              <ng-icon hlm name="lucideSearch" size="sm" />
            </button>

            @if (headerSecondaryActions()) {
              <ng-container *ngTemplateOutlet="headerSecondaryActions()!" />
            }
            @if (headerActions()) {
              <ng-container *ngTemplateOutlet="headerActions()!" />
            }
          </div>
        </header>

        <!-- Global command palette, mounted once for the whole shell. -->
        <app-command-palette />

        <div class="flex flex-1 flex-col overflow-x-hidden overflow-y-auto overscroll-y-contain p-4">
          <router-outlet />
        </div>
      </main>
    </div>
  `,
})
export class AppShellComponent {
  protected auth = inject(AuthService);
  protected globalSearch = inject(GlobalSearchService);
  private router = inject(Router);
  private activatedRoute = inject(ActivatedRoute);
  private processingHub = inject(ProcessingHubService);
  private pageHeaderService = inject(PageHeaderService);

  headerActions = this.pageHeaderService.actions;
  headerSecondaryActions = this.pageHeaderService.secondaryActions;
  headerTitle = this.pageHeaderService.title;

  constructor() {
    this.processingHub.connect();
    inject(FinancesPrivacyAutoMaskService);

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
