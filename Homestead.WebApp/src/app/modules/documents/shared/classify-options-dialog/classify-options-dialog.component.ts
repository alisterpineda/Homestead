import { Component, ChangeDetectionStrategy, computed, input, output, viewChild } from '@angular/core';
import { BrnDialog } from '@spartan-ng/brain/dialog';

import { HlmDialogImports } from '@/shared/components/spartan/dialog';
import { HlmCheckboxImports } from '@/shared/components/spartan/checkbox';
import { HlmButton } from '@/shared/components/spartan/button';
import { HlmLabel } from '@/shared/components/spartan/label';
import type { DocumentSuggestionField } from '@/api/generated/models/document-suggestion-field';
import { persistedSignal } from '@/shared/utils/persisted-signal';

interface ClassifyFieldSelection {
  tags: boolean;
  parties: boolean;
  name: boolean;
}

function isFieldSelection(value: unknown): value is ClassifyFieldSelection {
  return (
    typeof value === 'object' && value !== null &&
    typeof (value as ClassifyFieldSelection).tags === 'boolean' &&
    typeof (value as ClassifyFieldSelection).parties === 'boolean' &&
    typeof (value as ClassifyFieldSelection).name === 'boolean'
  );
}

/**
 * Shared, presentational dialog for AI classify/summarize. For classify it lets the user pick which
 * fields to (re)generate. Emits `confirmed`; the consumer makes the API call. Opened programmatically
 * via `open()` — the consumer supplies its own trigger. Set `showFields=false` to back Summarize (no
 * field checkboxes). AI provider selection was removed — the single statically-configured model is used.
 */
@Component({
  selector: 'app-classify-options-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmDialogImports,
    HlmButton,
    HlmLabel,
    HlmCheckboxImports,
  ],
  template: `
    <hlm-dialog>
      <button hlmBtn class="hidden" hlmDialogTrigger>Open</button>

      <hlm-dialog-content *hlmDialogPortal="let ctx">
        <div hlmDialogHeader>
          <h2 hlmDialogTitle>{{ title() }}</h2>
          <p hlmDialogDescription>{{ description() }}</p>
        </div>

        <div class="flex flex-col gap-4 py-4">
          @if (showFields()) {
            <div class="flex flex-col gap-2">
              <label hlmLabel>Fields to classify</label>
              <label class="flex items-center gap-2 text-sm cursor-pointer">
                <hlm-checkbox [checked]="fields().tags" (checkedChange)="setField('tags', $event)" [aria-label]="'Tags'" />
                <span>Tags</span>
              </label>
              <label class="flex items-center gap-2 text-sm cursor-pointer">
                <hlm-checkbox [checked]="fields().parties" (checkedChange)="setField('parties', $event)" [aria-label]="'Parties'" />
                <span>Parties</span>
              </label>
              <label class="flex items-center gap-2 text-sm cursor-pointer">
                <hlm-checkbox [checked]="fields().name" (checkedChange)="setField('name', $event)" [aria-label]="'Name'" />
                <span>Name</span>
              </label>
              @if (noFieldsSelected()) {
                <p class="text-sm text-muted-foreground">Select at least one field.</p>
              }
            </div>
          } @else {
            <p class="text-sm text-muted-foreground">{{ description() }}</p>
          }
        </div>

        <div hlmDialogFooter>
          <button hlmBtn variant="secondary" hlmDialogClose>Cancel</button>
          <button hlmBtn (click)="confirm(ctx)" [disabled]="disabled() || confirmDisabled()">
            {{ confirmLabel() }}
          </button>
        </div>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class ClassifyOptionsDialogComponent {
  mode = input<'single' | 'bulk'>('single');
  documentCount = input(1);
  disabled = input(false);
  showFields = input(true);

  confirmed = output<{ fields: DocumentSuggestionField[] }>();

  // Remembered across surfaces (single/bulk classify) and sessions.
  fields = persistedSignal<ClassifyFieldSelection>({
    key: 'homestead-classify-fields',
    defaultValue: { tags: true, parties: true, name: true },
    validate: isFieldSelection,
  });

  noFieldsSelected = computed(() => {
    const f = this.fields();
    return !f.tags && !f.parties && !f.name;
  });
  confirmDisabled = computed(() => this.showFields() && this.noFieldsSelected());

  private readonly verb = computed(() => (this.showFields() ? 'Classify' : 'Summarize'));

  title = computed(() => (this.showFields() ? 'AI Classification' : 'AI Summary'));

  description = computed(() => {
    const count = this.documentCount();
    const plural = count > 1 ? 's' : '';
    if (this.mode() === 'bulk') {
      return this.showFields()
        ? `This will classify ${count} selected document${plural} using AI. Only the selected fields will be (re)generated, matching existing tags and parties.`
        : `This will regenerate the summary for ${count} selected document${plural} using AI.`;
    }
    return this.showFields()
      ? 'Choose which fields to (re)generate with AI. Only existing tags and parties are matched.'
      : 'Regenerate this document’s summary using AI.';
  });

  confirmLabel = computed(() => {
    if (this.mode() === 'bulk') {
      const count = this.documentCount();
      return `${this.verb()} ${count} document${count > 1 ? 's' : ''}`;
    }
    return this.verb();
  });

  private dialog = viewChild(BrnDialog);

  open(): void {
    this.dialog()?.open();
  }

  setField(field: keyof ClassifyFieldSelection, checked: boolean): void {
    this.fields.update(f => ({ ...f, [field]: checked }));
  }

  confirm(ctx: { close: () => void }): void {
    if (this.confirmDisabled()) return;

    const fields: DocumentSuggestionField[] = [];
    if (this.showFields()) {
      const f = this.fields();
      if (f.tags) fields.push('Tags');
      if (f.parties) fields.push('Parties');
      if (f.name) fields.push('Name');
    }

    ctx.close();
    this.confirmed.emit({ fields });
  }
}
