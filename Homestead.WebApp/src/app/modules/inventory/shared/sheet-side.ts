import { computed, type Signal } from '@angular/core';
import { injectIsNarrowViewport } from '@/shared/utils/viewport';

/**
 * Which edge a quick-panel sheet should slide from: bottom sheet on phones (full width, top
 * backdrop band to dismiss), right side sheet otherwise. A thin reading of
 * {@link injectIsNarrowViewport} (`@/shared/utils/viewport`), so every surface asks the one
 * breakpoint question the same way.
 *
 * Must be called in an injection context. Environments without matchMedia (jsdom, SSR) get a
 * static 'right'.
 */
export function injectSheetSide(): Signal<'bottom' | 'right'> {
  const narrow = injectIsNarrowViewport();
  return computed(() => (narrow() ? 'bottom' : 'right'));
}
