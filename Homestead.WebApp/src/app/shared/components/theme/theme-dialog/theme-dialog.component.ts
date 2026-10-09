import { ChangeDetectionStrategy, Component, inject, viewChild } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSun, lucideMoon, lucideMonitor } from '@ng-icons/lucide';
import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmRadioGroupImports } from '@/shared/components/spartan/radio-group';
import { BrnDialog } from '@spartan-ng/brain/dialog';
import { ThemeService, type ThemeMode } from '@/shared/services/theme.service';

const themeModes: { value: ThemeMode; icon: string; label: string }[] = [
  { value: 'light', icon: 'lucideSun', label: 'Light' },
  { value: 'dark', icon: 'lucideMoon', label: 'Dark' },
  { value: 'system', icon: 'lucideMonitor', label: 'System' },
];

@Component({
  selector: 'app-theme-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmDialogImports, HlmRadioGroupImports],
  providers: [provideIcons({ lucideSun, lucideMoon, lucideMonitor })],
  template: `
    <hlm-dialog>
      <button class="hidden" hlmDialogTrigger>Open</button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>Theme</h2>
          <p hlmDialogDescription>Choose your preferred appearance.</p>
        </div>

        <div class="py-2">
          <div hlmRadioGroup class="grid grid-cols-3 gap-3" [value]="themeService.mode()" (valueChange)="onThemeChange($event)">
            @for (mode of modes; track mode.value) {
              <label
                class="cursor-pointer rounded-lg border border-border p-3 text-center transition-colors has-[:checked]:border-primary has-[:checked]:bg-primary/5 has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-ring"
                [attr.data-disabled]="null"
              >
                <hlm-radio [value]="mode.value" class="sr-only">
                  <hlm-radio-indicator indicator />
                </hlm-radio>
                <div class="flex flex-col items-center gap-2">
                  <ng-icon [name]="mode.icon" class="text-[length:--spacing(4)] text-foreground" />
                  <span class="text-sm font-medium">{{ mode.label }}</span>
                </div>
              </label>
            }
          </div>
        </div>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class ThemeDialogComponent {
  protected themeService = inject(ThemeService);
  protected modes = themeModes;

  private dialog = viewChild(BrnDialog);

  open(): void {
    this.dialog()?.open();
  }

  protected onThemeChange(value: ThemeMode | undefined): void {
    if (value) {
      this.themeService.setMode(value);
    }
  }
}
