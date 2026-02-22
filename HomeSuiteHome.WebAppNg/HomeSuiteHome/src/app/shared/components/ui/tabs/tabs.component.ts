import {
  Component,
  ChangeDetectionStrategy,
  Directive,
  TemplateRef,
  inject,
  input,
  contentChildren,
  signal,
  computed,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { cn } from '@/shared/utils';

@Directive({ selector: '[appTabDef]' })
export class TabDefDirective {
  id = input.required<string>({ alias: 'appTabDef' });
  label = input.required<string>();
  templateRef = inject(TemplateRef);
}

@Component({
  selector: 'app-tabs',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet],
  template: `
    <div [class]="className()">
      <div class="flex border-b border-border">
        @for (tab of tabs(); track tab.id()) {
          <button
            (click)="activeTab.set(tab.id())"
            [class]="
              cn(
                'px-4 py-2 text-sm font-medium transition-colors cursor-pointer',
                activeTab() === tab.id()
                  ? 'border-b-2 border-accent-500 text-text-primary'
                  : 'text-text-secondary hover:text-text-primary'
              )
            "
          >
            {{ tab.label() }}
          </button>
        }
      </div>
      <div class="pt-4">
        @for (tab of tabs(); track tab.id()) {
          @if (activeTab() === tab.id()) {
            <ng-container [ngTemplateOutlet]="tab.templateRef" />
          }
        }
      </div>
    </div>
  `,
})
export class TabsComponent {
  className = input('');
  tabs = contentChildren(TabDefDirective);

  activeTab = signal('');

  cn = cn;

  activeContent = computed(() => {
    const tabs = this.tabs();
    if (tabs.length > 0 && !this.activeTab()) {
      this.activeTab.set(tabs[0].id());
    }
    return tabs.find((t) => t.id() === this.activeTab());
  });
}
