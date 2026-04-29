import { Component, ChangeDetectionStrategy, input, output, computed, signal, effect } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HlmInput } from '@/shared/components/spartan/input';
import { HlmLabel } from '@/shared/components/spartan/label';
import { HlmCheckbox } from '@/shared/components/spartan/checkbox';
import { HlmButton } from '@/shared/components/spartan/button';
import type { PriceProviderConfigSchema } from '@/api/generated/models/price-provider-config-schema';

@Component({
  selector: 'app-price-source-config-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, HlmInput, HlmLabel, HlmCheckbox, HlmButton],
  template: `
    @if (schema().fields.length === 0) {
      <p class="text-sm text-muted-foreground">No configuration required.</p>
    } @else {
      <div class="flex flex-col gap-3">
        @for (field of schema().fields; track field.key) {
          <div class="flex flex-col gap-1">
            <label hlmLabel [for]="'cfg-' + field.key">
              {{ field.label }}
              @if (field.isRequired) { <span class="text-destructive">*</span> }
            </label>
            @if (field.type === 'Boolean') {
              <hlm-checkbox
                [id]="'cfg-' + field.key"
                [checked]="boolValue(field.key)"
                (checkedChange)="setValue(field.key, $event)"
              />
            } @else {
              <input
                hlmInput
                [id]="'cfg-' + field.key"
                [type]="field.type === 'Integer' ? 'number' : 'text'"
                [ngModel]="stringValue(field.key)"
                (ngModelChange)="setValue(field.key, $event)"
              />
            }
            @if (field.help) {
              <p class="text-xs text-muted-foreground">{{ field.help }}</p>
            }
          </div>
        }
        <div class="flex items-center gap-2 mt-2">
          <button hlmBtn (click)="emitSave()" [disabled]="saving() || !dirty()">
            {{ saving() ? 'Saving...' : 'Save' }}
          </button>
        </div>
      </div>
    }
  `,
})
export class PriceSourceConfigFormComponent {
  schema = input.required<PriceProviderConfigSchema>();
  initialJson = input<string | null>(null);
  saving = input<boolean>(false);

  saved = output<string>();

  values = signal<Record<string, string | boolean>>({});
  initial = signal<Record<string, string | boolean>>({});

  constructor() {
    effect(() => {
      const json = this.initialJson();
      const fields = this.schema().fields;
      let parsed: Record<string, unknown> = {};
      if (json) {
        try { parsed = JSON.parse(json) as Record<string, unknown>; } catch { parsed = {}; }
      }
      const next: Record<string, string | boolean> = {};
      for (const field of fields) {
        const raw = parsed[field.key];
        if (field.type === 'Boolean') {
          next[field.key] = typeof raw === 'boolean' ? raw : false;
        } else {
          next[field.key] = raw == null ? (field.defaultValue ?? '') : String(raw);
        }
      }
      this.values.set({ ...next });
      this.initial.set({ ...next });
    });
  }

  dirty = computed(() => {
    const a = this.values();
    const b = this.initial();
    for (const key of Object.keys(a)) {
      if (a[key] !== b[key]) return true;
    }
    return false;
  });

  stringValue(key: string): string {
    const v = this.values()[key];
    return typeof v === 'string' ? v : '';
  }

  boolValue(key: string): boolean {
    const v = this.values()[key];
    return typeof v === 'boolean' ? v : false;
  }

  setValue(key: string, value: string | boolean): void {
    this.values.update(v => ({ ...v, [key]: value }));
  }

  emitSave(): void {
    const json = JSON.stringify(this.values());
    this.saved.emit(json);
  }
}
