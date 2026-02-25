import { Component, ChangeDetectionStrategy, inject, computed } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSun, lucideMoon, lucideMonitor } from '@ng-icons/lucide';
import { HlmIconImports } from '@/components/ui/icon';
import { ThemeService, type ThemeMode } from '@/shared/services/theme.service';

const modes: { value: ThemeMode; icon: string; label: string }[] = [
  { value: 'light', icon: 'lucideSun', label: 'Light' },
  { value: 'dark', icon: 'lucideMoon', label: 'Dark' },
  { value: 'system', icon: 'lucideMonitor', label: 'System' },
];

@Component({
  selector: 'app-theme-toggle',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports],
  providers: [provideIcons({ lucideSun, lucideMoon, lucideMonitor })],
  template: `
    <button
      (click)="theme.cycleTheme()"
      class="cursor-pointer rounded-md p-1.5 text-sidebar-foreground/70 transition-colors hover:bg-sidebar-accent hover:text-sidebar-accent-foreground"
      [title]="'Theme: ' + current().label"
    >
      <ng-icon hlm [name]="current().icon" size="sm" />
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
