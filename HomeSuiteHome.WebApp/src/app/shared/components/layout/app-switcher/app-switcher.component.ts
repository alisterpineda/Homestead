import { Component, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LucideAngularModule } from 'lucide-angular';
import { LayoutGrid } from 'lucide-angular';
import { PopoverComponent } from '@/shared/components/ui/popover/popover.component';
import { modules } from '@/modules/registry';

const colorMap: Record<string, string> = {
  slate: 'bg-slate-100 text-slate-600',
  indigo: 'bg-indigo-100 text-indigo-600',
  emerald: 'bg-emerald-100 text-emerald-600',
  amber: 'bg-amber-100 text-amber-600',
};

@Component({
  selector: 'app-switcher',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, LucideAngularModule, PopoverComponent],
  template: `
    <app-popover>
      <button
        trigger
        class="cursor-pointer rounded-md p-1.5 text-text-secondary transition-colors hover:bg-sidebar-hover hover:text-text-primary"
      >
        <lucide-icon [img]="layoutGridIcon" [size]="18" />
      </button>
      <div class="grid w-52 grid-cols-2 gap-2">
        @for (mod of modules; track mod.id) {
          <a
            [routerLink]="mod.basePath"
            class="flex flex-col items-center gap-2 rounded-lg p-3 text-center transition-colors hover:bg-surface-raised"
          >
            <div [class]="'rounded-lg p-2 ' + getColors(mod.labelColor)">
              <lucide-icon [img]="mod.icon" [size]="20" />
            </div>
            <span class="text-xs font-medium text-text-primary">{{ mod.label }}</span>
          </a>
        }
      </div>
    </app-popover>
  `,
})
export class AppSwitcherComponent {
  layoutGridIcon = LayoutGrid;
  modules = modules;

  getColors(labelColor: string): string {
    return colorMap[labelColor] ?? 'bg-gray-100 text-gray-600';
  }
}
