import { Component, ChangeDetectionStrategy, input, output, computed } from '@angular/core';
import { cn } from '@/shared/utils';

@Component({
  selector: 'app-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-col gap-1.5">
      @if (label()) {
        <label [for]="inputId()" class="text-sm font-medium text-foreground">
          {{ label() }}
        </label>
      }
      <input
        [id]="inputId()"
        [class]="classes()"
        [placeholder]="placeholder()"
        [value]="value()"
        (input)="onInput($event)"
      />
    </div>
  `,
})
export class InputComponent {
  label = input('');
  inputId = input('');
  placeholder = input('');
  value = input('');
  className = input('');

  valueChange = output<string>();

  classes = computed(() =>
    cn(
      'rounded-lg border border-input bg-background px-3 py-2 text-sm text-foreground placeholder:text-muted-foreground transition-colors focus:border-ring focus:outline-none focus:ring-2 focus:ring-ring/20',
      this.className(),
    ),
  );

  onInput(event: Event): void {
    const target = event.target as HTMLInputElement;
    this.valueChange.emit(target.value);
  }
}
