import { Component, ChangeDetectionStrategy, model } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideWallet,
  lucideChartLine,
  lucidePackage,
  lucideCreditCard,
  lucideLandmark,
  lucideCircleMinus,
} from '@ng-icons/lucide';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import {
  USER_ACCOUNT_KINDS,
  KIND_LABELS,
  KIND_DESCRIPTIONS,
  KIND_ICONS,
  type UserAccountKind,
} from './account-kind-meta';

@Component({
  selector: 'app-kind-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmIconImports],
  providers: [provideIcons({
    lucideWallet,
    lucideChartLine,
    lucidePackage,
    lucideCreditCard,
    lucideLandmark,
    lucideCircleMinus,
  })],
  template: `
    <div role="radiogroup" aria-label="Account kind" class="grid grid-cols-2 gap-2 sm:grid-cols-3">
      @for (kind of kinds; track kind) {
        <button
          type="button"
          role="radio"
          [attr.aria-checked]="value() === kind"
          (click)="value.set(kind)"
          class="flex flex-col items-start gap-1 rounded-md border p-3 text-left transition-colors cursor-pointer hover:bg-accent"
          [class.border-primary]="value() === kind"
          [class.bg-accent]="value() === kind"
          [class.border-input]="value() !== kind"
        >
          <ng-icon hlm [name]="iconFor(kind)" size="sm" class="text-muted-foreground" />
          <span class="text-sm font-medium">{{ labelFor(kind) }}</span>
          <span class="text-xs text-muted-foreground">{{ descFor(kind) }}</span>
        </button>
      }
    </div>
  `,
})
export class KindPickerComponent {
  value = model<UserAccountKind | null>(null);

  readonly kinds = USER_ACCOUNT_KINDS;

  labelFor(kind: UserAccountKind): string {
    return KIND_LABELS[kind];
  }
  descFor(kind: UserAccountKind): string {
    return KIND_DESCRIPTIONS[kind];
  }
  iconFor(kind: UserAccountKind): string {
    return KIND_ICONS[kind];
  }
}
