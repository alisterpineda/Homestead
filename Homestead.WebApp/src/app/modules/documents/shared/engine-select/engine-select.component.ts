import { Component, ChangeDetectionStrategy, computed, inject, input, output } from '@angular/core';
import { HlmSelectImports } from '@/shared/components/spartan/select';

import { TextExtractionEnginesService } from '@/modules/documents/shared/text-extraction-engines.service';
import type { TextExtractionEngineResponse } from '@/api/generated/models/text-extraction-engine-response';

/**
 * Picks a configured text extraction engine. Shared by the reprocess dialogs (which send the choice as a
 * per-run override) and the settings page (which saves it as the household default).
 *
 * The empty value means "use the default" — which is the household default in the dialogs, and the
 * built-in `tesseract` engine on the settings page. Engines the backend reports as unavailable are shown
 * disabled with their reason **on the option itself** rather than hidden: "azure-layout needs an endpoint"
 * is a far more useful answer than the option silently not being there. Putting the reason on the row is
 * load-bearing — a disabled option can never become the selection, so `selectedError` alone leaves it
 * unreachable in the reprocess dialogs.
 */
@Component({
  selector: 'app-engine-select',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSelectImports],
  template: `
    <hlm-select [value]="value()" (valueChange)="onValueChange($any($event))" [itemToString]="engineToString">
      <hlm-select-trigger [class]="triggerClass()">
        <hlm-select-value [placeholder]="defaultLabel()" />
      </hlm-select-trigger>
      <hlm-select-content *hlmSelectPortal>
        <hlm-select-group>
          <hlm-select-item value="">{{ defaultLabel() }}</hlm-select-item>
          @for (engine of engines(); track engine.key) {
            <!--
              The wrapper is a div, not a span, on purpose: hlm-select-item lays its content out with
              "flex items-center" and promotes its LAST SPAN CHILD to a flex row (the *:[span]:last:flex
              rule). A reason span there becomes a sibling column of the name and the two wrap against each
              other, mangling the engine key — the part you scan for. That child selector matches direct
              children only, so a div sidesteps it and lets the reason sit under the name instead.
            -->
            <hlm-select-item [value]="engine.key" [disabled]="!engine.isConfigured">
              <div class="flex flex-col items-start">
                <span>{{ engine.key }} — {{ engine.name }}</span>
                @if (unavailableReason(engine); as reason) {
                  <span class="text-xs text-muted-foreground">{{ reason }}</span>
                }
              </div>
            </hlm-select-item>
          }
        </hlm-select-group>
      </hlm-select-content>
    </hlm-select>

    @if (selectedError(); as error) {
      <p class="mt-1 text-sm text-destructive">{{ error }}</p>
    }
  `,
})
export class EngineSelectComponent {
  value = input<string>('');
  /** Label for the empty option. */
  defaultLabel = input<string>('Household default');
  triggerClass = input<string>('w-full');
  valueChange = output<string>();

  private enginesService = inject(TextExtractionEnginesService);

  engines = this.enginesService.engines;

  /**
   * Why an engine cannot run, for its row in the list — null when it is usable. The backend always sends a
   * reason with `isConfigured: false`, but fall back to a bare marker rather than render an empty `()`.
   *
   * This is separate from `selectedError` because an unavailable engine is `[disabled]` and so can never
   * *be* the selection: without this the reason was unreachable in the dialogs, which is exactly where
   * someone debugging a half-configured engine looks.
   */
  unavailableReason = (engine: TextExtractionEngineResponse): string | null =>
    engine.isConfigured ? null : (engine.configurationError ?? 'unavailable');

  /**
   * Why the current selection cannot run, if it cannot. Still reachable on the settings page, where a saved
   * household default that later breaks *is* the selected value.
   */
  selectedError = computed(() => {
    const key = this.value();
    if (!key) return null;
    const engine = this.enginesService.find(key);
    return engine && !engine.isConfigured ? engine.configurationError : null;
  });

  engineToString = (key: string): string => {
    if (key === '') return this.defaultLabel();
    const engine = this.enginesService.find(key);
    return engine ? `${engine.key} — ${engine.name}` : key;
  };

  onValueChange(value: string): void {
    this.valueChange.emit(value);
  }
}
