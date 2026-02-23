import { Component, ChangeDetectionStrategy, input, inject, effect } from '@angular/core';
import { Router, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs';
import { toSignal } from '@angular/core/rxjs-interop';
import { SidebarHeaderComponent } from '../sidebar-header/sidebar-header.component';
import { SidebarNavComponent } from '../sidebar-nav/sidebar-nav.component';
import { SidebarService } from '@/shared/services/sidebar.service';
import { cn } from '@/shared/utils';
import type { ModuleDefinition } from '@/shared/types/modules';

@Component({
  selector: 'app-sidebar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SidebarHeaderComponent, SidebarNavComponent],
  template: `
    @if (sidebar.isOpen()) {
      <div
        class="fixed inset-0 z-40 bg-black/30 md:hidden"
        (click)="sidebar.close()"
      ></div>
    }

    <aside
      [class]="
        cn(
          'fixed inset-y-0 left-0 z-50 flex h-screen w-64 flex-col border-r border-sidebar-border bg-sidebar transition-transform duration-200',
          sidebar.isOpen() ? 'translate-x-0' : '-translate-x-full',
          'md:static md:z-auto md:translate-x-0 md:shrink-0 md:transition-none'
        )
      "
    >
      <app-sidebar-header [activeModule]="activeModule()" />
      <div class="flex-1 overflow-y-auto py-3">
        @if (activeModule()) {
          <app-sidebar-nav [items]="activeModule()!.sidebar" />
        }
      </div>
    </aside>
  `,
})
export class SidebarComponent {
  activeModule = input<ModuleDefinition | undefined>();

  sidebar = inject(SidebarService);
  cn = cn;

  private router = inject(Router);
  private navEnd = toSignal(
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd)),
  );

  constructor() {
    effect(() => {
      this.navEnd();
      this.sidebar.close();
    });
  }
}
