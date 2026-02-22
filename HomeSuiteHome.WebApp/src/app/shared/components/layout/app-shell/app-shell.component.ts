import { Component, ChangeDetectionStrategy, inject, computed } from '@angular/core';
import { Router, RouterOutlet, NavigationEnd } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { LucideAngularModule } from 'lucide-angular';
import { Menu } from 'lucide-angular';
import { SidebarComponent } from '../sidebar/sidebar.component';
import { SidebarService } from '@/shared/services/sidebar.service';
import { getModuleByPath } from '@/modules/registry';

@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, LucideAngularModule, SidebarComponent],
  template: `
    <div class="flex h-screen overflow-hidden">
      <app-sidebar [activeModule]="activeModule()" />
      <div class="flex flex-1 flex-col overflow-hidden">
        <div
          class="sticky top-0 z-40 flex items-center gap-3 border-b border-border bg-surface px-4 py-3 md:hidden"
        >
          <button
            (click)="sidebar.toggle()"
            class="cursor-pointer rounded-lg p-1.5 text-text-secondary hover:bg-surface-raised hover:text-text-primary"
          >
            <lucide-icon [img]="menuIcon" [size]="20" />
          </button>
          @if (activeModule()) {
            <div class="flex items-center gap-2">
              <lucide-icon [img]="activeModule()!.icon" [size]="16" class="text-text-secondary" />
              <span class="text-sm font-semibold text-text-primary">
                {{ activeModule()!.label }}
              </span>
            </div>
          }
        </div>
        <main class="flex-1 overflow-y-auto p-4 md:p-8">
          <router-outlet />
        </main>
      </div>
    </div>
  `,
})
export class AppShellComponent {
  menuIcon = Menu;
  sidebar = inject(SidebarService);
  private router = inject(Router);

  private url = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map((e) => (e as NavigationEnd).urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  activeModule = computed(() => getModuleByPath(this.url()));
}
