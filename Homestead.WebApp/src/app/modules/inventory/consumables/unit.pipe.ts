import { Pipe, type PipeTransform } from '@angular/core';

import { resolveUnit } from './consumable-shared';

/**
 * Renders a consumable's unit for display, falling back to the generic {@link DEFAULT_UNIT} word
 * when the stored unit is null/blank. Lets templates write `{{ s.unit | unit }}` instead of wiring a
 * resolver method into each component.
 */
@Pipe({ name: 'unit' })
export class UnitPipe implements PipeTransform {
  transform(unit: string | null | undefined): string {
    return resolveUnit(unit);
  }
}
