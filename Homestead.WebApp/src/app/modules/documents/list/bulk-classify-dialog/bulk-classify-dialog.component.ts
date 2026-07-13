import { Component, ChangeDetectionStrategy, input, output, computed } from '@angular/core';

import { HlmButton } from '@/shared/components/spartan/button';
import { HlmIconImports } from '@/shared/components/spartan/icon';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSparkles } from '@ng-icons/lucide';
import type { DocumentSuggestionField } from '@/api/generated/models/document-suggestion-field';
import { ClassifyOptionsDialogComponent } from '../../shared/classify-options-dialog/classify-options-dialog.component';

/**
 * Bulk-classify trigger: a button that opens the shared classify-options dialog (with field
 * checkboxes). Presentational — the parent list component performs the API call on `confirmed`.
 */
@Component({
  selector: 'app-bulk-classify-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmButton, HlmIconImports, NgIcon, ClassifyOptionsDialogComponent],
  providers: [provideIcons({ lucideSparkles })],
  template: `
    <button hlmBtn variant="outline" [disabled]="disabled()" (click)="optionsDialog.open()">
      <ng-icon hlm name="lucideSparkles" size="sm" />
      Classify
    </button>

    <app-classify-options-dialog
      #optionsDialog
      mode="bulk"
      [documentCount]="documentCount()"
      [showFields]="true"
      (confirmed)="confirmed.emit($event)"
    />
  `,
})
export class BulkClassifyDialogComponent {
  documentIds = input.required<Set<string>>();
  disabled = input(false);
  confirmed = output<{ fields: DocumentSuggestionField[] }>();

  documentCount = computed(() => this.documentIds().size);
}
