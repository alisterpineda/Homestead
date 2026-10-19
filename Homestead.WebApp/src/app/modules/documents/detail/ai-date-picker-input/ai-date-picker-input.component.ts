import { booleanAttribute, ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCalendar, lucideCheck, lucideX } from '@ng-icons/lucide';
import { provideBrnDatePickerTrigger } from '@spartan-ng/brain/date-picker';
import { HlmDatePickerInput } from '@/shared/components/spartan/date-picker';
import { HlmInputGroup, HlmInputGroupImports } from '@/shared/components/spartan/input-group';
import { HlmTooltipImports } from '@/shared/components/spartan/tooltip';

/**
 * A date-picker input that can carry an AI suggestion's approve/decline actions inside the input group,
 * in place of the clear button — the date-field counterpart of the Name field's inline ✓/✗.
 *
 * It subclasses the local `HlmDatePickerInput` because the helm addon has no projection slot and the
 * generated helm files must not be edited. The template is a copy of `hlm-date-picker-input.ts`'s, changed
 * only in the addon's `@if (aiPending())` branch and the `originalLabel` tooltip on the `<input>` (there, not
 * on the host, so keyboard focus opens it, as on the Name input): **re-sync it after a Spartan regen** (see
 * the `spartan-regen` skill's known gotchas).
 */
@Component({
  selector: 'app-ai-date-picker-input',
  imports: [HlmInputGroupImports, HlmTooltipImports, NgIcon],
  providers: [
    provideIcons({ lucideCalendar, lucideCheck, lucideX }),
    provideBrnDatePickerTrigger(AiDatePickerInputComponent),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  hostDirectives: [HlmInputGroup],
  template: `
    <input
      #input
      hlmInputGroupInput
      [value]="_inputValue()"
      [id]="inputId()"
      [placeholder]="placeholder()"
      [disabled]="_disabled()"
      [forceInvalid]="forceInvalid()"
      [attr.aria-invalid]="_ariaInvalid()"
      [attr.data-invalid]="_ariaInvalid()"
      [attr.data-touched]="_touched?.() ? 'true' : null"
      [attr.data-dirty]="_dirty?.() ? 'true' : null"
      [attr.data-matches-spartan-invalid]="_spartanInvalid() ? 'true' : null"
      [hlmTooltip]="originalLabel() ?? ''"
      [tooltipDisabled]="!originalLabel()"
      (click)="_handleClick()"
      (keydown.arrowDown)="_open()"
      (keydown.enter)="_handleEnter($event)"
      (input)="_handleInputChange($event)"
      (focus)="_handleFocus()"
      (blur)="_handleBlur()"
    />
    <hlm-input-group-addon align="inline-end">
      @if (aiPending()) {
        <button
          hlmInputGroupButton
          size="icon-xs"
          [attr.aria-label]="approveLabel()"
          (click)="approve.emit()"
          [disabled]="_disabled()"
        >
          <ng-icon name="lucideCheck" />
        </button>
        <button
          hlmInputGroupButton
          size="icon-xs"
          [attr.aria-label]="declineLabel()"
          (click)="decline.emit()"
          [disabled]="_disabled()"
        >
          <ng-icon name="lucideX" />
        </button>
      } @else if (_showClearButton()) {
        <button
          hlmInputGroupButton
          size="icon-xs"
          variant="ghost"
          [attr.aria-label]="clearAriaLabel()"
          (click)="_clear()"
          [disabled]="_disabled()"
        >
          <ng-icon name="lucideX" />
        </button>
      }
      <button
        hlmInputGroupButton
        size="icon-xs"
        [attr.aria-label]="calendarAriaLabel()"
        (click)="_popover().open()"
        [disabled]="_disabled()"
      >
        <ng-icon name="lucideCalendar" />
      </button>
    </hlm-input-group-addon>
  `,
})
export class AiDatePickerInputComponent<T> extends HlmDatePickerInput<T> {
  /** Whether an AI suggestion is pending review: shows ✓/✗ in place of the clear button. */
  public readonly aiPending = input(false, { transform: booleanAttribute });
  public readonly approveLabel = input('Approve suggestion');
  public readonly declineLabel = input('Decline suggestion');
  /** Tooltip on the input naming the value a decline restores, e.g. 'Original: Jan 1, 2025'; null for none. */
  public readonly originalLabel = input<string | null>(null);

  public readonly approve = output<void>();
  public readonly decline = output<void>();
}
