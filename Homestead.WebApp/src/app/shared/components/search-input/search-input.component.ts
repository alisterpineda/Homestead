import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSearch } from '@ng-icons/lucide';
import { HlmInputGroupImports } from '@/shared/components/spartan/input-group';

/**
 * Shared search box: a leading search icon + text input, composed from Spartan's
 * input-group. Height and font-size are inherited from the vendored helm
 * (`hlmInputGroupInput` hosts `HlmInput`: `h-8` and `text-base md:text-sm`, so iOS
 * Safari never zooms on focus). This component adds NO height or font-size classes
 * of its own — every search box across the app stays pixel-identical.
 *
 * Consumers hold a writable signal and two-way bind it: `[(value)]="searchInput"`.
 * A one-way form is also supported: `[value]="x" (valueChange)="…"`.
 */
@Component({
  selector: 'app-search-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgIcon, HlmInputGroupImports],
  providers: [provideIcons({ lucideSearch })],
  host: { class: 'block min-w-0' },
  template: `
    <hlm-input-group>
      <hlm-input-group-addon>
        <ng-icon name="lucideSearch" class="text-[length:--spacing(4)]" />
      </hlm-input-group-addon>
      <input
        hlmInputGroupInput
        [id]="inputId() ?? undefined"
        [placeholder]="placeholder()"
        [attr.aria-label]="ariaLabel() ?? placeholder()"
        [value]="value()"
        (input)="value.set(asValue($event))"
      />
    </hlm-input-group>
  `,
})
export class SearchInputComponent {
  /** Two-way bound search text. */
  readonly value = model('');
  /** Placeholder text; also the accessible-name fallback. */
  readonly placeholder = input('Search...');
  /** Accessible name for the input; falls back to `placeholder` when null. */
  readonly ariaLabel = input<string | null>(null);
  /** DOM id for the inner input so an external `<label for>` can target it. */
  readonly inputId = input<string | null>(null);

  protected asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }
}
