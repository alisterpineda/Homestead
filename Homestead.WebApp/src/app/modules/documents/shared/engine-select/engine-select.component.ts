import { Component, ChangeDetectionStrategy, computed, inject, input, output } from '@angular/core';
import { HlmSelectImports } from '@/shared/components/spartan/select';

import { TextExtractionEnginesService } from '@/modules/documents/shared/text-extraction-engines.service';
import type { TextExtractionEngineResponse } from '@/api/generated/models/text-extraction-engine-response';

/**
 * Picks a configured text extraction engine for the reprocess dialogs, which send the choice as a per-run
 * override.
 *
 * The empty value means "use the configured default" — whatever `Ocr:DefaultEngine` names (or the built-in
 * `tesseract` when unset), which the backend reports via `isDefault`, so the empty option is labelled with
 * that key. Engines the backend reports as unavailable are shown disabled with their reason **on the
 * option itself** rather than hidden: "azure-layout needs an endpoint" is a far more useful answer than the
 * option silently not being there. Putting the reason on the row is load-bearing — a disabled option can
 * never become the selection, so `selectedError` alone leaves it unreachable in the reprocess dialogs.
 */
@Component({
  selector: 'app-engine-select',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmSelectImports],
  template: `
    <hlm-select [value]="value()" (valueChange)="onValueChange($any($event))" [itemToString]="engineToString">
      <hlm-select-trigger [class]="triggerClass()">
        <hlm-select-value [placeholder]="effectiveDefaultLabel()" />
      </hlm-select-trigger>
      <hlm-select-content *hlmSelectPortal>
        <hlm-select-group>
          <hlm-select-item value="">{{ effectiveDefaultLabel() }}</hlm-select-item>
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
  /** Optional override for the empty option's label; unset, it names the configured default engine. */
  defaultLabel = input<string | undefined>(undefined);
  triggerClass = input<string>('w-full');
  valueChange = output<string>();

  private enginesService = inject(TextExtractionEnginesService);

  engines = this.enginesService.engines;

  /**
   * What the empty option reads. Names the default engine's key ("Default (tesseract)") so a user pinning
   * nothing can see what they will get; the bare fallback only shows while the list is loading or failed.
   */
  effectiveDefaultLabel = computed(() => {
    const override = this.defaultLabel();
    if (override !== undefined) return override;
    const defaultEngine = this.enginesService.defaultEngine();
    return defaultEngine ? `Default (${defaultEngine.key})` : 'Default engine';
  });

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
   * Why the current selection cannot run, if it cannot. Today's callers only ever select from enabled
   * rows, so this fires only if a future caller seeds `value` with a key that has since broken — then
   * the reason belongs under the trigger, not only on the (now disabled) row.
   */
  selectedError = computed(() => {
    const key = this.value();
    if (!key) return null;
    const engine = this.enginesService.find(key);
    return engine && !engine.isConfigured ? engine.configurationError : null;
  });

  engineToString = (key: string): string => {
    if (key === '') return this.effectiveDefaultLabel();
    const engine = this.enginesService.find(key);
    return engine ? `${engine.key} — ${engine.name}` : key;
  };

  onValueChange(value: string): void {
    this.valueChange.emit(value);
  }
}
