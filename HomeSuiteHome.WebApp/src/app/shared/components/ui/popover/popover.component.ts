import {
  Component,
  ChangeDetectionStrategy,
  input,
  signal,
  ElementRef,
  inject,
} from '@angular/core';

@Component({
  selector: 'app-popover',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(document:mousedown)': 'onDocumentClick($event)',
    '(document:keydown)': 'onDocumentKeydown($event)',
  },
  template: `
    <div class="relative">
      <div (click)="toggle()">
        <ng-content select="[trigger]" />
      </div>
      @if (open()) {
        <div
          [class]="
            'absolute top-full z-50 mt-2 max-w-[calc(100vw-2rem)] rounded-xl border border-border bg-surface-overlay p-3 shadow-lg ' +
            (align() === 'right' ? 'right-0' : 'left-0')
          "
          (click)="close()"
        >
          <ng-content />
        </div>
      }
    </div>
  `,
})
export class PopoverComponent {
  align = input<'left' | 'right'>('left');
  open = signal(false);

  private el = inject(ElementRef);

  toggle(): void {
    this.open.update((v) => !v);
  }

  close(): void {
    this.open.set(false);
  }

  onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this.el.nativeElement.contains(event.target)) {
      this.close();
    }
  }

  onDocumentKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && this.open()) {
      this.close();
    }
  }
}
