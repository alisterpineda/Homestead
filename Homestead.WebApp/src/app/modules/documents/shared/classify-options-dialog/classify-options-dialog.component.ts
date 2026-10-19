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
  documentDate: boolean;
  expiryDate: boolean;
}

// Selections stored before the date fields existed have only the first three keys.
type StoredFieldSelection = Pick<ClassifyFieldSelection, 'tags' | 'parties' | 'name'> &
  Partial<Pick<ClassifyFieldSelection, 'documentDate' | 'expiryDate'>>;

const DEFAULT_FIELDS: ClassifyFieldSelection = {
  tags: true,
  parties: true,
  name: true,
  documentDate: true,
  expiryDate: true,
};

function isFieldSelection(value: unknown): value is StoredFieldSelection {
  if (typeof value !== 'object' || value === null) return false;
  const v = value as Record<string, unknown>;
  const isOptionalBoolean = (x: unknown) => x === undefined || typeof x === 'boolean';
  return (
    typeof v['tags'] === 'boolean' &&
    typeof v['parties'] === 'boolean' &&
    typeof v['name'] === 'boolean' &&
    isOptionalBoolean(v['documentDate']) &&
    isOptionalBoolean(v['expiryDate'])
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
              <label class="flex items-center gap-2 text-sm cursor-pointer">
                <hlm-checkbox [checked]="fields().documentDate" (checkedChange)="setField('documentDate', $event)" [aria-label]="'Document date'" />
                <span>Document date</span>
              </label>
              <label class="flex items-center gap-2 text-sm cursor-pointer">
                <hlm-checkbox [checked]="fields().expiryDate" (checkedChange)="setField('expiryDate', $event)" [aria-label]="'Expiry date'" />
                <span>Expiry date</span>
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
  private readonly storedFields = persistedSignal<StoredFieldSelection>({
    key: 'homestead-classify-fields',
    defaultValue: DEFAULT_FIELDS,
    validate: isFieldSelection,
  });

  // Fields missing from an older stored selection fall back to their (ticked) defaults.
  fields = computed<ClassifyFieldSelection>(() => ({ ...DEFAULT_FIELDS, ...this.storedFields() }));

  noFieldsSelected = computed(() => Object.values(this.fields()).every(checked => !checked));
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
    this.storedFields.set({ ...this.fields(), [field]: checked });
  }

  confirm(ctx: { close: () => void }): void {
    if (this.confirmDisabled()) return;

    const fields: DocumentSuggestionField[] = [];
    if (this.showFields()) {
      const f = this.fields();
      if (f.tags) fields.push('Tags');
      if (f.parties) fields.push('Parties');
      if (f.name) fields.push('Name');
      if (f.documentDate) fields.push('DocumentDate');
      if (f.expiryDate) fields.push('ExpiryDate');
    }

    ctx.close();
    this.confirmed.emit({ fields });
  }
}
