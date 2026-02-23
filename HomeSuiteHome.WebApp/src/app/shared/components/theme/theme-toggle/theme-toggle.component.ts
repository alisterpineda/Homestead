import { Component, ChangeDetectionStrategy, inject, computed } from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';
import { Sun, Moon, Monitor } from 'lucide-angular';
import { ThemeService, type ThemeMode } from '@/shared/services/theme.service';
import type { LucideIconData } from 'lucide-angular';

const modes: { value: ThemeMode; icon: LucideIconData; label: string }[] = [
  { value: 'light', icon: Sun, label: 'Light' },
  { value: 'dark', icon: Moon, label: 'Dark' },
  { value: 'system', icon: Monitor, label: 'System' },
];

@Component({
  selector: 'app-theme-toggle',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LucideAngularModule],
  template: `
    <button
      (click)="theme.cycleTheme()"
      class="cursor-pointer rounded-md p-1.5 text-sidebar-foreground/70 transition-colors hover:bg-sidebar-accent hover:text-sidebar-accent-foreground"
      [title]="'Theme: ' + current().label"
    >
      <lucide-icon [img]="current().icon" [size]="18" />
    </button>
  `,
})
export class ThemeToggleComponent {
  theme = inject(ThemeService);

  current = computed(() => {
    const mode = this.theme.mode();
    return modes.find((m) => m.value === mode) ?? modes[0];
  });
}
