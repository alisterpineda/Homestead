import { Component, ChangeDetectionStrategy, input, output, ElementRef, viewChild } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideX } from '@ng-icons/lucide';
import { isLightColor } from '@/shared/utils/color-utils';

export const PRESET_COLORS = [
  { name: 'Red', hex: '#ef4444' },
  { name: 'Orange', hex: '#f97316' },
  { name: 'Amber', hex: '#f59e0b' },
  { name: 'Yellow', hex: '#eab308' },
  { name: 'Lime', hex: '#84cc16' },
  { name: 'Green', hex: '#22c55e' },
  { name: 'Teal', hex: '#14b8a6' },
  { name: 'Cyan', hex: '#06b6d4' },
  { name: 'Blue', hex: '#3b82f6' },
  { name: 'Indigo', hex: '#6366f1' },
  { name: 'Violet', hex: '#8b5cf6' },
  { name: 'Pink', hex: '#ec4899' },
] as const;

@Component({
  selector: 'app-color-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon],
  providers: [provideIcons({ lucideCheck, lucideX })],
  template: `
    <div class="flex flex-wrap items-center gap-2">
      @for (preset of presets; track preset.hex) {
        <button
          type="button"
          [attr.aria-label]="preset.name"
          [title]="preset.name"
          (click)="selectColor(preset.hex)"
          class="relative h-7 w-7 shrink-0 cursor-pointer rounded-full border-2 transition-all"
          [style.background-color]="preset.hex"
          [class]="color() === preset.hex ? 'border-foreground scale-110' : 'border-transparent hover:scale-110'"
        >
          @if (color() === preset.hex) {
            <ng-icon name="lucideCheck" class="text-[length:--spacing(3)] absolute inset-0 m-auto drop-shadow-sm" [class]="isLight(preset.hex) ? 'text-foreground' : 'text-white'" />
          }
        </button>
      }

      <!-- Custom color picker -->
      <button
        type="button"
        title="Custom color"
        (click)="openCustomPicker()"
        class="relative h-7 w-7 shrink-0 cursor-pointer rounded-full border-2 transition-all"
        [style.background]="isCustomColor() ? color() : 'conic-gradient(red, yellow, lime, aqua, blue, magenta, red)'"
        [class]="isCustomColor() ? 'border-foreground scale-110' : 'border-transparent hover:scale-110'"
      >
        @if (isCustomColor()) {
          <ng-icon name="lucideCheck" class="text-[length:--spacing(3)] absolute inset-0 m-auto drop-shadow-sm" [class]="isLight(color()!) ? 'text-foreground' : 'text-white'" />
        }
        <input
          #colorInput
          type="color"
          [value]="color() ?? '#3b82f6'"
          (input)="onCustomColorInput($event)"
          class="sr-only"
          aria-label="Custom color"
          tabindex="-1"
        />
      </button>

      <!-- Clear button -->
      @if (color()) {
        <button
          type="button"
          title="Remove color"
          (click)="selectColor(null)"
          class="flex h-7 w-7 shrink-0 cursor-pointer items-center justify-center rounded-full border-2 border-border text-muted-foreground transition-all hover:border-foreground hover:text-foreground"
        >
          <ng-icon name="lucideX" class="text-[length:--spacing(3)]" />
        </button>
      }
    </div>
  `,
})
export class ColorPickerComponent {
  color = input<string | null>(null);
  colorChange = output<string | null>();

  readonly presets = PRESET_COLORS;
  private colorInputRef = viewChild<ElementRef<HTMLInputElement>>('colorInput');

  private presetHexes = new Set<string>(PRESET_COLORS.map((p) => p.hex));

  isLight = isLightColor;

  isCustomColor = () => {
    const c = this.color();
    return c !== null && !this.presetHexes.has(c);
  };

  selectColor(hex: string | null): void {
    this.colorChange.emit(hex);
  }

  openCustomPicker(): void {
    this.colorInputRef()?.nativeElement.click();
  }

  onCustomColorInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.colorChange.emit(value);
  }
}
