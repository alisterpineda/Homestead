import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import type { BrnDialogState } from '@spartan-ng/brain/dialog';
import { HlmDrawerImports } from '@/shared/components/spartan/drawer';
import { HlmSheetImports } from '@/shared/components/spartan/sheet';
import { injectIsNarrowViewport } from '@/shared/utils/viewport';

/**
 * Shell for the inventory quick panels: a right-side sheet at or above `md`, a real bottom
 * drawer (drag handle, swipe-to-dismiss) on phones. Sheet and drawer are different component
 * trees, so this is one of the sanctioned JS-breakpoint cases — CSS can't pick which one to
 * instantiate (see {@link injectIsNarrowViewport}).
 *
 * The title row is owned here because `hlmSheetTitle` / `hlmDrawerTitle` differ per tree; put
 * an icon before it with `panelTitleStart` and a badge after it with `panelTitleEnd`. Everything
 * else (scroll body, footer) is default-projected into the `flex flex-col` content, so a footer
 * carries its own `mt-auto` + padding. Environments without matchMedia (jsdom, SSR) always get
 * the sheet.
 */
@Component({
  selector: 'app-quick-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet, HlmSheetImports, HlmDrawerImports],
  template: `
    <!-- One <ng-content> per slot, each wrapped in a template: Angular assigns projected nodes to
         the first matching slot in template order, so repeating the slots inside both branches
         would leave the sheet branch empty. Outletting the templates renders them wherever the
         active branch is. -->
    <ng-template #titleStart><ng-content select="[panelTitleStart]" /></ng-template>
    <ng-template #titleEnd><ng-content select="[panelTitleEnd]" /></ng-template>
    <ng-template #body><ng-content /></ng-template>

    @if (narrow()) {
      <hlm-drawer [state]="state()" (stateChanged)="stateChanged.emit($event)">
        <hlm-drawer-content *hlmDrawerPortal>
          <hlm-drawer-header>
            <div class="flex items-center gap-2">
              <ng-container *ngTemplateOutlet="titleStart" />
              <h3 hlmDrawerTitle class="min-w-0 truncate">{{ title() }}</h3>
              <ng-container *ngTemplateOutlet="titleEnd" />
            </div>
          </hlm-drawer-header>
          <ng-container *ngTemplateOutlet="body" />
        </hlm-drawer-content>
      </hlm-drawer>
    } @else {
      <hlm-sheet side="right" [state]="state()" (stateChanged)="stateChanged.emit($event)">
        <hlm-sheet-content *hlmSheetPortal>
          <hlm-sheet-header>
            <div class="flex items-center gap-2">
              <ng-container *ngTemplateOutlet="titleStart" />
              <h3 hlmSheetTitle class="min-w-0 truncate">{{ title() }}</h3>
              <ng-container *ngTemplateOutlet="titleEnd" />
            </div>
          </hlm-sheet-header>
          <ng-container *ngTemplateOutlet="body" />
        </hlm-sheet-content>
      </hlm-sheet>
    }
  `,
})
export class QuickPanelComponent {
  readonly title = input<string | null | undefined>();
  readonly state = input<BrnDialogState>('closed');
  readonly stateChanged = output<BrnDialogState>();

  protected readonly narrow = injectIsNarrowViewport();
}
