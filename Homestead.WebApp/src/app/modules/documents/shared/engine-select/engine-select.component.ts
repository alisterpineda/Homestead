import { Component, ChangeDetectionStrategy, computed, inject, input, output } from '@angular/core';
import { HlmSelectImports } from '@/shared/components/spartan/select';

import { TextExtractionEnginesService } from '@/modules/documents/shared/text-extraction-engines.service';

/**
 * Picks a configured text extraction engine. Shared by the reprocess dialogs (which send the choice as a
 * per-run override) and the settings page (which saves it as the household default).
 *
 * The empty value means "use the default" — which is the household default in the dialogs, and the
 * built-in `tesseract` engine on the settings page. Engines the backend reports as unavailable are shown
 * disabled with their reason rather than hidden: "azure-layout needs an endpoint" is a far more useful
 * answer than the option silently not being there.
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
            <hlm-select-item [value]="engine.key" [disabled]="!engine.isConfigured">
              {{ engine.key }} — {{ engine.name }}
              @if (!engine.isConfigured) {
                <span class="text-muted-foreground">(unavailable)</span>
              }
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

  /** Why the current selection cannot run, if it cannot. */
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
