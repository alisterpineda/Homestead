import { Directive } from '@angular/core';
import { classes } from '@/shared/components/spartan/utils';

/**
 * Styles a hand-rolled popover/combobox trigger button to match Spartan's
 * `hlm-select-trigger` exactly, so custom pickers sit flush beside real select
 * triggers and date pickers (same height, border, focus ring, dark-mode fill).
 *
 * Derived from the regenerated nova@1.1.0 select-trigger recipe with three deltas:
 * full-width (`w-full` instead of `w-fit`), a fixed `h-8` (no `data-[size]` variants),
 * and the `*:data-[slot=select-value]` selectors dropped (pickers project their own
 * label span, not a select-value slot).
 *
 * Applied via the `classes()` helper so a per-instance `class="…"` on the button still
 * merges/overrides cleanly (Tailwind-aware). A single shared directive makes the former
 * six-way copy-paste drift structurally impossible.
 */
@Directive({
  selector: 'button[appPickerTrigger]',
})
export class PickerTriggerDirective {
  constructor() {
    classes(
      () =>
        'border-input data-placeholder:text-muted-foreground dark:bg-input/30 dark:hover:bg-input/50 focus-visible:border-ring focus-visible:ring-ring/50 data-[matches-spartan-invalid=true]:ring-destructive/20 dark:data-[matches-spartan-invalid=true]:ring-destructive/40 data-[matches-spartan-invalid=true]:border-destructive dark:data-[matches-spartan-invalid=true]:border-destructive/50 gap-1.5 rounded-lg border bg-transparent py-2 ps-2.5 pe-2 text-sm transition-colors focus-visible:ring-3 data-[matches-spartan-invalid=true]:ring-3 h-8 flex w-full items-center justify-between whitespace-nowrap outline-none disabled:cursor-not-allowed disabled:opacity-50 [&_ng-icon]:pointer-events-none [&_ng-icon]:shrink-0',
    );
  }
}
