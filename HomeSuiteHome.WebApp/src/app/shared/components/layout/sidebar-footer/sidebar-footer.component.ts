import { Component, ChangeDetectionStrategy, inject, computed } from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';
import { LogOut } from 'lucide-angular';
import { AuthService } from '@/shared/services/auth.service';

@Component({
  selector: 'app-sidebar-footer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LucideAngularModule],
  template: `
    <div class="border-t border-sidebar-border px-3 py-3">
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
          type="button"
          (click)="auth.logout()"
          class="shrink-0 rounded-md p-1.5 text-sidebar-foreground/70 hover:bg-sidebar-accent hover:text-sidebar-accent-foreground"
          aria-label="Log out"
        >
          <lucide-icon [img]="logOutIcon" [size]="16" />
        </button>
      </div>
    </div>
  `,
})
export class SidebarFooterComponent {
  protected auth = inject(AuthService);
  protected logOutIcon = LogOut;

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
}
